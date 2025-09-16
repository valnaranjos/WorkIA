using KommoAIAgent.Models;
using KommoAIAgent.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace KommoAIAgent.Services
{
    /// <summary>
    /// Implementación del orquestador de webhooks. Conecta la entrada de Kommo con la IA y la salida a Kommo.
    /// </summary>
    public class WebhookHandler : IWebhookHandler
    {
        private readonly IKommoApiService _kommoService;
        private readonly IAiService _aiService;
        private readonly ILogger<WebhookHandler> _logger;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;

        public WebhookHandler(
            IKommoApiService kommoService,
            IAiService aiService,
            ILogger<WebhookHandler> logger,
            IConfiguration configuration, IMemoryCache cache)
        {
            _kommoService = kommoService;
            _aiService = aiService;
            _logger = logger;
            _configuration = configuration;
            _cache = cache;
        }

        /// <summary>
        /// Procesa el webhook entrante siguiendo la lógica de negocio definida.
        public async Task ProcessIncomingMessageAsync(KommoWebhookPayload payload)
        {
            // 1. Extraer y validar el mensaje (esto no cambia).
            var messageDetails = payload?.Message?.AddedMessages?.FirstOrDefault();
            if (messageDetails is null || !messageDetails.LeadId.HasValue)
            {
                _logger.LogWarning("Webhook recibido pero no contenía un mensaje válido con LeadId. Se ignora.");
                return;
            }

            // Evitar procesar duplicados usando memoria en vez de base, y así evita costos innecesarios incluso en tokens.
            var msgId = messageDetails.MessageId ?? $"{messageDetails.LeadId}-{messageDetails.Text}";
            if (_cache.TryGetValue(msgId, out _))
            {
                _logger.LogInformation("Dup detectado. Ignorando messageId={MsgId}", msgId);
                return;
            }
            _cache.Set(msgId, true, TimeSpan.FromHours(6));

            // Si no hay texto Y no hay adjuntos, no hay nada que hacer.
            if (string.IsNullOrWhiteSpace(messageDetails.Text) && messageDetails.Attachments.Count == 0)
            {
                _logger.LogWarning("Webhook para el Lead {LeadId} no contenía texto ni adjuntos. Se ignora.", messageDetails.LeadId.Value);
                return;
            }

            var leadId = messageDetails.LeadId.Value;
            var userMessage = messageDetails.Text ?? ""; // Usamos "" si el texto es nulo.

            _logger.LogInformation("Procesando mensaje entrante para el Lead ID: {LeadId}.", leadId);

            try
            {
                string aiResponse; // Declaramos la variable que guardará la respuesta de la IA.

                // Heurística: detecta imagen aunque 'Type' no venga exactamente como "image"
                var imageAttachment = messageDetails.Attachments.FirstOrDefault(IsImageAttachment);

                if (imageAttachment != null && !string.IsNullOrWhiteSpace(imageAttachment.Url))
                {
                    _logger.LogInformation("El mensaje contiene una imagen. Usando el servicio de análisis de imagen.");

                    // 1) Bajamos el adjunto desde Kommo con token
                    var (bytes, mime, fileName) = await _kommoService.DownloadAttachmentAsync(imageAttachment.Url!);

                    // 2) Si no viene MIME, lo inferimos por extensión
                    if (string.IsNullOrWhiteSpace(mime))
                        mime = GuessMimeFromUrlOrName(imageAttachment.Url!, fileName);

                    // 3) Prompt (si no hay caption, usa uno por defecto)
                    var prompt = string.IsNullOrWhiteSpace(userMessage)
                        ? "Describe la imagen y extrae cualquier texto visible."
                        : userMessage;

                    // 4) IA visión por BYTES
                    aiResponse = await _aiService.AnalyzeImageFromBytesAsync(prompt, bytes, mime);
                }
                else
                {
                    _logger.LogInformation("El mensaje es solo texto. Usando el servicio de respuesta contextual.");
                    aiResponse = await _aiService.GenerateContextualResponseAsync(userMessage);
                }

                _logger.LogInformation("Respuesta de IA recibida.");

                // El resto del flujo para actualizar Kommo no cambia.
                var fieldIdString = _configuration["Kommo:CustomFieldIds:MensajeIA"];
                if (!long.TryParse(fieldIdString, out var fieldId))
                {
                    _logger.LogError("El ID del campo personalizado 'MensajeIA' no está configurado correctamente.");
                    return;
                }



                const int KOMMO_FIELD_MAX = 8000; // ajusta si conoces el tope real
                static string Truncate(string s) => s.Length <= KOMMO_FIELD_MAX ? s : s[..KOMMO_FIELD_MAX];

                _logger.LogInformation("Enviando respuesta de IA al campo {FieldId} del Lead {LeadId}...", fieldId, leadId);
                await _kommoService.UpdateLeadFieldAsync(leadId, fieldId, Truncate(aiResponse));                             
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ocurrió un error inesperado durante el procesamiento para el Lead ID {LeadId}", leadId);
            }
        }

        private static bool IsImageAttachment(AttachmentInfo a)
        {
            // MIME explícito
            if (!string.IsNullOrWhiteSpace(a.MimeType) &&
                a.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return true;

            // Tipo generoso (a veces Kommo manda "file", "picture", etc.)
            if (!string.IsNullOrWhiteSpace(a.Type) &&
                a.Type.Contains("image", StringComparison.OrdinalIgnoreCase))
                return true;

            // Extensión en URL o nombre
            var s = (a.Url ?? a.Name ?? "").ToLowerInvariant();
            return s.EndsWith(".jpg") || s.EndsWith(".jpeg") || s.EndsWith(".png") ||
                   s.EndsWith(".webp") || s.EndsWith(".gif") || s.EndsWith(".bmp") ||
                   s.EndsWith(".tif") || s.EndsWith(".tiff");
        }

        private static string GuessMimeFromUrlOrName(string url, string? name)
        {
            var s = (name ?? url).ToLowerInvariant();
            if (s.EndsWith(".png")) return "image/png";
            if (s.EndsWith(".jpg") || s.EndsWith(".jpeg")) return "image/jpeg";
            if (s.EndsWith(".webp")) return "image/webp";
            if (s.EndsWith(".gif")) return "image/gif";
            if (s.EndsWith(".bmp")) return "image/bmp";
            if (s.EndsWith(".tif") || s.EndsWith(".tiff")) return "image/tiff";
            return "image/jpeg";
        }
    }
}


