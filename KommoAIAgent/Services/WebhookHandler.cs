using KommoAIAgent.Models;
using KommoAIAgent.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using OpenAI.Chat;

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
        private readonly IChatMemoryStore _conv;

        public WebhookHandler(
            IKommoApiService kommoService,
            IAiService aiService,
            ILogger<WebhookHandler> logger,
            IConfiguration configuration, IMemoryCache cache,
            IChatMemoryStore conv)
        {
            _kommoService = kommoService;
            _aiService = aiService;
            _logger = logger;
            _configuration = configuration;
            _cache = cache;
            _conv = conv;
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
                //Construimos el historial de mensajes para contexto.
                var messages = new List<ChatMessage>
                {
                    ChatMessage.CreateSystemMessage(
                        "Eres un asistente útil, conciso y amable. Usa el contexto previo si el usuario hace referencia a algo ya dicho.")
                };

                // Traemos los últimos turnos guardados para este lead y los agregamos al prompt (NUEVO)
                var history = _conv.Get(leadId, 10);
                foreach (var turn in history)
                {
                    if (turn.Role == "assistant")
                        messages.Add(ChatMessage.CreateAssistantMessage(turn.Content));
                    else
                        messages.Add(ChatMessage.CreateUserMessage(turn.Content));
                }

                string aiResponse; // Declaramos la variable que guardará la respuesta de la IA.

                // Heurística: detecta imagen aunque 'Type' no venga exactamente como "image"
                var imageAttachment = messageDetails.Attachments.FirstOrDefault(IsImageAttachment);

                if (imageAttachment != null && !string.IsNullOrWhiteSpace(imageAttachment.Url))
                {
                    _logger.LogInformation("El mensaje contiene una imagen. Usando el servicio de análisis de imagen.");

                    // Bajamos el adjunto desde Kommo con token
                    var (bytes, mime, fileName) = await _kommoService.DownloadAttachmentAsync(imageAttachment.Url!);

                    //  Si no viene MIME, lo inferimos por extensión
                    if (string.IsNullOrWhiteSpace(mime))
                        mime = GuessMimeFromUrlOrName(imageAttachment.Url!, fileName);

                    // Prompt (si no hay caption, usa uno por defecto)
                    var prompt = string.IsNullOrWhiteSpace(userMessage)
                        ? "Describe la imagen y extrae cualquier texto visible."
                        : userMessage;



                        messages.Add(
                        ChatMessage.CreateUserMessage(
                            ChatMessageContentPart.CreateTextPart(prompt),
                            ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(bytes), mime)
                        )
                    );

                    // Llamamos a la IA con todo el historial
                    aiResponse = await _aiService.CompleteAsync(messages, maxTokens: 600, model: "gpt-4o");

                    // Guardamos el turno en memoria para el siguiente mensaje (NUEVO)
                    _conv.AppendUser(leadId, $"{prompt} [imagen]");
                    _conv.AppendAssistant(leadId, aiResponse);
                }
                else
                {
                    _logger.LogInformation("El mensaje es solo texto. Usando el servicio de respuesta contextual.");
              
                    // Turno actual: texto (NUEVO)
                    messages.Add(ChatMessage.CreateUserMessage(userMessage));

                    // Llamamos a la IA con todo el historial (modelo por defecto)
                    aiResponse = await _aiService.CompleteAsync(messages, maxTokens: 400);

                    // Guardamos el turno en memoria para el siguiente mensaje
                    _conv.AppendUser(leadId, userMessage);
                    _conv.AppendAssistant(leadId, aiResponse);

                }

                _logger.LogInformation("Respuesta de IA recibida.");

                // Esscribimos la respuesta al campo personalizado en Kommo.
                var fieldIdString = _configuration["Kommo:CustomFieldIds:MensajeIA"];
                if (!long.TryParse(fieldIdString, out var fieldId))
                {
                    _logger.LogError("El ID del campo personalizado 'MensajeIA' no está configurado correctamente.");
                    return;
                }

                const int KOMMO_FIELD_MAX = 8000; //  tope real??
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


