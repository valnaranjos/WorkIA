using KommoAIAgent.Helpers;
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
        private readonly IMessageBuffer _msgBuffer;
        private readonly LastImageCache _lastImage;

        public WebhookHandler(
            IKommoApiService kommoService,
            IAiService aiService,
            ILogger<WebhookHandler> logger,
            IConfiguration configuration, IMemoryCache cache,
            IChatMemoryStore conv, IMessageBuffer msgBuffer, LastImageCache lastImage)
        {
            _kommoService = kommoService;
            _aiService = aiService;
            _logger = logger;
            _configuration = configuration;
            _cache = cache;
            _conv = conv;
            _msgBuffer = msgBuffer;
            _lastImage = lastImage;
        }

        /// <summary>
        /// Procesa el payload del webhook entrante de Kommo.
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
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

            _logger.LogInformation("Procesando mensaje entrante para el Lead ID: {LeadId}, Encolando para debounce...", leadId);

            // Debounce: acumulamos y procesamos cuando toque (ventana o imagen)
            await _msgBuffer.OfferAsync(
                leadId,
                userMessage,
                messageDetails.Attachments,
                async (id, agg) => await ProcessAggregatedTurnAsync(id, agg.Text, agg.Attachments)
            );

            return;
        }

        /// <summary>
        /// Porcesa un turno agregado (después de debounce).
        /// </summary>
        /// <param name="leadId"></param>
        /// <param name="userText"></param>
        /// <param name="attachments"></param>
        /// <returns></returns>
        private async Task ProcessAggregatedTurnAsync(long leadId, string userText, List<AttachmentInfo> attachments)
        {
            _logger.LogInformation("Procesando TURNO AGREGADO para Lead {LeadId}. Texto='{Text}', Adjuntos={AdjCount}",
                leadId, userText, attachments?.Count ?? 0);

            try
            {
                var messages = ChatComposer.BuildHistoryMessages(
                    _conv,
                    leadId,
                    "Eres un asistente útil, conciso y amable. Usa el contexto previo si el usuario hace referencia a algo ya dicho.",
                    historyTurns: 10
                );

                string aiResponse;

                var img = attachments?.FirstOrDefault(AttachmentHelper.IsImage);

                if (img != null && !string.IsNullOrWhiteSpace(img.Url))
                {
                    // Procesar imagen + texto, desde URL (descargar primero)
                    var (bytes, mime, fileName) = await _kommoService.DownloadAttachmentAsync(img.Url!);
                    if (string.IsNullOrWhiteSpace(mime))
                        mime = AttachmentHelper.GuessMimeFromUrlOrName(img.Url!, fileName);



                    _lastImage.SetLastImage(leadId, bytes, mime);
                    var prompt = string.IsNullOrWhiteSpace(userText)
                        ? "Describe la imagen y extrae cualquier texto visible."
                        : userText;

                    ChatComposer.AppendUserTextAndImage(messages, prompt, bytes, mime);
                    aiResponse = await _aiService.CompleteAsync(messages, maxTokens: 600, model: "gpt-4o");

                    _conv.AppendUser(leadId, $"{prompt} [imagen]");
                    _conv.AppendAssistant(leadId, aiResponse);
                }
                else
                {
                    if (_lastImage.TryGetLastImage(leadId, out var last))
                    {
                        // Reusa la imagen reciente ⇒ el modelo sí “ve” la misma foto de la pregunta anterior
                        ChatComposer.AppendUserTextAndImage(messages, userText, last.Bytes, last.Mime);
                        aiResponse = await _aiService.CompleteAsync(messages, maxTokens: 600, model: "gpt-4o");
                    }
                    else
                    {
                        ChatComposer.AppendUserText(messages, userText);
                        aiResponse = await _aiService.CompleteAsync(messages, maxTokens: 400);
                    }

                    _conv.AppendUser(leadId, userText);
                    _conv.AppendAssistant(leadId, aiResponse);
                }

                var fieldIdString = _configuration["Kommo:CustomFieldIds:MensajeIA"];
                if (!long.TryParse(fieldIdString, out var fieldId))
                {
                    _logger.LogError("El ID del campo personalizado 'MensajeIA' no está configurado correctamente.");
                    return;
                }

                const int KOMMO_FIELD_MAX = 8000;
                await _kommoService.UpdateLeadFieldAsync(leadId, fieldId, TextUtil.Truncate(aiResponse, KOMMO_FIELD_MAX));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando TURNO AGREGADO para Lead {LeadId}", leadId);
            }
        }
    }
}


