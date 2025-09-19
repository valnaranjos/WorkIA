using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Models;
using KommoAIAgent.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

       
        public KommoApiService(HttpClient httpClient, ILogger<KommoApiService> logger, ITenantContext tenant)
        {
            _httpClient = httpClient;
            _logger = logger;
            _tenant = tenant;
        }

        /// <summary>
        /// Construye un HttpRequestMessage con URL absoluta + Authorization por tenant.
        /// </summary>
        private HttpRequestMessage BuildRequest(HttpMethod method, string relativePath, HttpContent? content = null)
        {
            // BaseUrl del tenant actual
            var baseUrl = _tenant.Config.Kommo.BaseUrl.TrimEnd('/');
            var url = new Uri(new Uri(baseUrl + "/"), relativePath.TrimStart('/'));

            var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tenant.Config.Kommo.AccessToken);
            if (content != null) req.Content = content;
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
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // PATCH unitario
            var path = $"/api/v4/leads/{leadId}";
            using var req = BuildRequest(HttpMethod.Patch, path, content);

            _logger.LogInformation("PATCH (unitario) lead {LeadId} field {FieldId} (tenant={Tenant})",
                leadId, fieldId, _tenant.CurrentTenantId);

            using var res = await _httpClient.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogError("PATCH unitario error {Status} (lead={LeadId}, field={FieldId}) → {Body}",
                    (int)res.StatusCode, leadId, fieldId, body);
                return;
            }

            _logger.LogInformation("PATCH unitario OK (lead={LeadId}, field={FieldId}) → {Body}",
                leadId, fieldId, body);

            // Verificación con 3 reintentos (200ms, 700ms, 1500ms)
            var delays = new[] { 200, 700, 1500 };
            string? seen = null;

            for (int i = 0; i < delays.Length; i++)
            {
                await Task.Delay(delays[i]);

                using var getReq = BuildRequest(HttpMethod.Get, $"/api/v4/leads/{leadId}");
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
