using System.Net.Http.Headers;
using System.Text;
using KommoAIAgent.Models;
using KommoAIAgent.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KommoAIAgent.Services
{
    /// <summary>
    /// Implementación concreta para interactuar con la API V4 de Kommo.
    /// </summary>
    public class KommoApiService : IKommoApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<KommoApiService> _logger;
        private readonly IConfiguration _configuration;

        public KommoApiService(HttpClient httpClient, IConfiguration configuration, ILogger<KommoApiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;

            // Configuramos el HttpClient con la URL base y el token de acceso desde appsettings.json
            var baseUrl = _configuration["Kommo:BaseUrl"];
            var accessToken = _configuration["Kommo:AccessToken"];

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(accessToken))
            {
                throw new InvalidOperationException("La BaseUrl o el AccessToken de Kommo no están configurados.");
            }

            _httpClient.BaseAddress = new Uri(baseUrl);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        /// <summary>
        /// Actualiza un campo personalizado del lead. Este es el método clave para nuestro objetivo.
        /// </summary>
        public async Task UpdateLeadFieldAsync(long leadId, long fieldId, string value)
        {
            var endpoint = $"/api/v4/leads/{leadId}";
            _logger.LogInformation("Actualizando campo {FieldId} para el lead {LeadId}...", fieldId, leadId);

            try
            {
                // Kommo requiere una estructura JSON específica y anidada para actualizar campos personalizados.
                var payload = new
                {
                    custom_fields_values = new[]
                    {
                    new
                    {
                        field_id = fieldId,
                        values = new[]
                        {
                            new { value }
                        }
                    }
                }
                };

                var jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // La API de Kommo usa el método PATCH para actualizaciones parciales.
                var response = await _httpClient.PatchAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Campo personalizado del lead {LeadId} actualizado exitosamente.", leadId);
                }
                else
                {
                    // Si falla, registramos el error que nos devuelve Kommo para poder depurarlo.
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Error al actualizar el lead {LeadId}. Status: {StatusCode}. Response: {ErrorBody}",
                        leadId, response.StatusCode, errorBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ocurrió una excepción al intentar actualizar el lead {LeadId}.", leadId);
            }
        }

        /// <summary>
        /// Obtiene el contexto de un lead. No lo usaremos en V1.0 pero lo dejamos preparado.
        /// </summary>
        public async Task<KommoLead?> GetLeadContextByIdAsync(long leadId)
        {
            var endpoint = $"/api/v4/leads/{leadId}?with=contacts";
            _logger.LogInformation("Obteniendo contexto para el lead {LeadId}...", leadId);

            try
            {
                var response = await _httpClient.GetAsync(endpoint);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("No se pudo obtener el lead {LeadId}. Status: {StatusCode}", leadId, response.StatusCode);
                    return null;
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                var leadJson = JObject.Parse(jsonString);

                // Extraemos los datos que nos interesan del JSON de Kommo.
                var lead = new KommoLead
                {
                    Id = leadJson["id"]?.Value<long>() ?? 0,
                    Name = leadJson["name"]?.Value<string>() ?? "Cliente"
                    // Aquí podríamos añadir lógica para extraer etiquetas (tags) si las necesitáramos.
                };

                return lead;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ocurrió una excepción al obtener el lead {LeadId}", leadId);
                return null;
            }
        }

        public async Task<(byte[] bytes, string mime, string? fileName)> DownloadAttachmentAsync(string url)
        {
             // Si Kommo manda URL relativa, convierte a absoluta usando la BaseAddress
            var absolute = Uri.TryCreate(url, UriKind.Absolute, out var u)
                ? u
                : new Uri(_httpClient.BaseAddress!, url);

            using var req = new HttpRequestMessage(HttpMethod.Get, absolute);

            using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();

            var len = res.Content.Headers.ContentLength;
            if (len.HasValue && len.Value > 15_000_000) // 15 MB aprox.
                throw new InvalidOperationException($"Adjunto demasiado grande: {len} bytes");
            var mime = res.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"MIME no soportado para visión: {mime}");
           
            var name = res.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            var bytes = await res.Content.ReadAsByteArrayAsync();

            _logger.LogInformation("Descargado adjunto {Name} ({Mime}) tamaño={Len} bytes", name ?? "(sin nombre)", mime, bytes.Length);
            return (bytes, mime, name);

            //Kommo a veces no envía Content-Type así que se infiere el MIME.
        }
    }
}
