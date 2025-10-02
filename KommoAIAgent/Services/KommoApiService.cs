using KommoAIAgent.Application;
using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace KommoAIAgent.Services
{
    /// <summary>
    /// Cliente de Kommo V4 adaptado a multi-tenant.
    /// NO configura HttpClient en el ctor; arma URL absoluta y Authorization por request.
    /// </summary>
    public class KommoApiService : IKommoApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<KommoApiService> _logger;
        private readonly ITenantContext _tenant;

       
        public KommoApiService(HttpClient httpClient,
            ILogger<KommoApiService> logger, 
            ITenantContext tenant)
        {
            _httpClient = httpClient;
            _logger = logger;
            _tenant = tenant;
        }

        /// <summary>
        /// Construye un HttpRequestMessage con URL absoluta + Authorization por tenant.
        /// </summary>
        private HttpRequestMessage BuildRequest(HttpMethod method, string relativeOrAbsoluteUrl, HttpContent? content = null)
        {
            // Permite url absoluta (https://sub.amocrm.com/...) o relativa (/api/v4/leads/...)
            string url;
            if (relativeOrAbsoluteUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = relativeOrAbsoluteUrl;
            }
            else
            {
                var baseUrl = _tenant.Config.Kommo.BaseUrl?.TrimEnd('/')
                              ?? throw new InvalidOperationException($"Kommo BaseUrl vacío para tenant={_tenant.CurrentTenantId}");
                url = $"{baseUrl}/{relativeOrAbsoluteUrl.TrimStart('/')}";
            }

            var token = _tenant.Config.Kommo.AccessToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException($"Kommo AccessToken vacío para tenant={_tenant.CurrentTenantId}");

            // Fuerza HTTP/1.1 para evitar coalescing/mezcla entre subdominios por limitaciones Kommo
            var req = new HttpRequestMessage(method, url) { Version = HttpVersion.Version11 };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (content != null) req.Content = content;

            // Log de diagnóstico (host + cola de token)
            var tail = token.Length >= 6 ? token[^6..] : token;
            _logger.LogInformation("Kommo → {Method} {Url} (tenant={Tenant}, token=***{Tail})",
                method, url, _tenant.CurrentTenantId, tail);

            return req;
        }


        /// <summary>
        /// Actualiza un campo personalizado del lead "Mensaje IA". Método clave.
        /// De aquí envía la respuesta de la IA al campo personalizado Kommo.
        /// Se podrían actualizar más campos si fuese necesario.
        /// </summary>
        public async Task UpdateLeadFieldAsync(long leadId, long fieldId, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _logger.LogWarning("UpdateLeadFieldAsync: value vacío; skip (lead={LeadId}, field={FieldId}, tenant={Tenant})",
                    leadId, fieldId, _tenant.CurrentTenantId);
                return;
            }

            // Construye el payload.
            var payload = new
            {
                custom_fields_values = new[]
                {
            new
            {
                field_id = fieldId,
                values = new[] { new { value } } // texto/textarea largo.
            }
        }
            };

            var json = JsonConvert.SerializeObject(payload);

            // Helper local para crear SIEMPRE una nueva request + content (necesario para retry)
            HttpRequestMessage CreatePatchRequest()
            {
                var path = $"/api/v4/leads/{leadId}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var req = BuildRequest(HttpMethod.Patch, path, content);
                req.Version = HttpVersion.Version11; // fuerza HTTP/1.1 por request para que no haya coalescing por los tokens de Kommo por la rápida alternancia entre requests.
                return req;
            }

            _logger.LogInformation("PATCH (unitario) lead {LeadId} field {FieldId} (tenant={Tenant})",
                leadId, fieldId, _tenant.CurrentTenantId);

            // -------- Primer intento --------
            using (var req1 = CreatePatchRequest())
            using (var res1 = await _httpClient.SendAsync(req1))
            {
                var body1 = await res1.Content.ReadAsStringAsync();

                if (res1.IsSuccessStatusCode)
                {
                    _logger.LogInformation("PATCH unitario OK (lead={LeadId}, field={FieldId}) → {Body}",
                        leadId, fieldId, body1);
                }
                else if (res1.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // -------- Retry breve solo si 401 --------
                    _logger.LogWarning("PATCH 401 (lead={LeadId}, field={FieldId}, tenant={Tenant}) → retry breve",
                        leadId, fieldId, _tenant.CurrentTenantId);

                    await Task.Delay(150); // pequeña espera para alternancias rápidas entre tenants

                    using var req2 = CreatePatchRequest();
                    using var res2 = await _httpClient.SendAsync(req2);
                    var body2 = await res2.Content.ReadAsStringAsync();

                    if (res2.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("PATCH unitario OK tras retry (lead={LeadId}, field={FieldId}) → {Body}",
                            leadId, fieldId, body2);
                    }
                    else
                    {
                        _logger.LogError("PATCH 401 tras retry (lead={LeadId}, field={FieldId}, tenant={Tenant}) → {Body}",
                            leadId, fieldId, _tenant.CurrentTenantId, body2);
                        return;
                    }
                }
                else
                {
                    _logger.LogError("PATCH unitario error {Status} (lead={LeadId}, field={FieldId}, tenant={Tenant}) → {Body}",
                        (int)res1.StatusCode, leadId, fieldId, _tenant.CurrentTenantId, body1);
                    return;
                }
            }



            // -------- Verificación con 3 reintentos (200ms, 700ms, 1500ms) --------
            var delays = new[] { 200, 700, 1500 };
            string? seen = null;

            for (int i = 0; i < delays.Length; i++)
            {
                await Task.Delay(delays[i]);

                using var getReq = BuildRequest(HttpMethod.Get, $"/api/v4/leads/{leadId}");
                getReq.Version = HttpVersion.Version11; // 👈 también forzar HTTP/1.1 en GET
                using var getRes = await _httpClient.SendAsync(getReq);
                var getBody = await getRes.Content.ReadAsStringAsync();

                if (!getRes.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GET verify fallo {Status} (lead={LeadId}) → {Body}",
                        (int)getRes.StatusCode, leadId, getBody);
                    continue;
                }

                var lead = Newtonsoft.Json.Linq.JObject.Parse(getBody);
                var cfv = (Newtonsoft.Json.Linq.JArray?)lead["custom_fields_values"];
                var field = (Newtonsoft.Json.Linq.JObject?)cfv?
                    .FirstOrDefault(x => x["field_id"]?.Value<long>() == fieldId);

                seen = field?["values"]?.FirstOrDefault()?["value"]?.Value<string>();

                _logger.LogInformation("Verificación[{Try}] CF {FieldId} en lead {LeadId} → «{Value}» (tenant={Tenant})",
                    i + 1, fieldId, leadId, seen, _tenant.CurrentTenantId);

                if (!string.IsNullOrWhiteSpace(seen)) break;
            }

            if (string.IsNullOrWhiteSpace(seen))
            {
                _logger.LogWarning("Tras PATCH unitario, el CF {FieldId} de lead {LeadId} quedó vacío (tenant={Tenant}).",
                    fieldId, leadId, _tenant.CurrentTenantId);
            }
        }

        /// <summary>
        /// Helper para mejorar ergonomía en el método anterior, más fácil de usar.
        /// </summary>
        /// <param name="leadId"></param>
        /// <param name="texto"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public Task UpdateLeadMensajeIAAsync(long leadId, string texto, CancellationToken ct = default)
    => UpdateLeadFieldAsync(leadId, _tenant.Config.Kommo.FieldIds.MensajeIA, texto);

        /// <summary>
        /// Obtiene el contexto de un lead. No se usa ya, pero queda preparado para futuras mejoras.
        /// </summary>
        public async Task<KommoLead?> GetLeadContextByIdAsync(long leadId)
        {

            _logger.LogInformation("Obteniendo contexto para lead {LeadId} (tenant={Tenant})...",
                leadId, _tenant.CurrentTenantId);


            try
            {
                using var req = BuildRequest(HttpMethod.Get, $"/api/v4/leads/{leadId}?with=contacts");
                using var res = await _httpClient.SendAsync(req);

                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Kommo GET lead {LeadId} falló ({Status}) (tenant={Tenant})",
                        leadId, res.StatusCode, _tenant.CurrentTenantId);
                    return null;
                }

                var json = await res.Content.ReadAsStringAsync();
                var leadJson = JObject.Parse(json);

                return new KommoLead
                {
                    Id = leadJson["id"]?.Value<long>() ?? 0,
                    Name = leadJson["name"]?.Value<string>() ?? "Cliente"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción al obtener lead {LeadId} (tenant={Tenant})",
                     leadId, _tenant.CurrentTenantId);
                return null;
            }
        }

        /// <summary>
        /// Descarga un adjunto desde una URL pública que se obtiene de Kommo.
        /// Valida el tamaño y el tipo MIME para asegurarse de que es una imagen adecuada. 
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<(byte[] bytes, string mime, string? fileName)> DownloadAttachmentAsync(string url)
        {
            // Si Kommo manda URL relativa, convierte a absoluta usando la BaseUrl del tenant.
            var absolute = Uri.TryCreate(url, UriKind.Absolute, out var u)
                ? u
                : new Uri(new Uri(_tenant.Config.Kommo.BaseUrl.TrimEnd('/') + "/"), url.TrimStart('/'));

            using var req = new HttpRequestMessage(HttpMethod.Get, absolute);

            // Si la descarga requiere auth (algunas lo piden)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tenant.Config.Kommo.AccessToken);

            using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();

            var len = res.Content.Headers.ContentLength;

            // Limitamos el tamaño de la imagen para evitar demasiado, sino envía error.
            if (len.HasValue && len.Value > 15_000_000) // 15 MB aprox.
                throw new InvalidOperationException($"Adjunto demasiado grande: {len} bytes");


            // Validamos que sea una imagen por el MIME, sino envía error.
            var mime = res.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"MIME no soportado para visión: {mime}");

            var name = res.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            var bytes = await res.Content.ReadAsByteArrayAsync();

            _logger.LogInformation("Descargado adjunto {Name} ({Mime}) tamaño={Len} bytes", name ?? "(sin nombre)", mime, bytes.Length);
            return (bytes, mime, name);

            //Kommo A VECES no envía Content-Type así que se infiere el MIME.
        }
    }
}
