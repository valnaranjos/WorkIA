using KommoAIAgent.Api.Contracts;
using KommoAIAgent.Application.Common;
using KommoAIAgent.Application.Interfaces;
using KommoAIAgent.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using OpenAI.Chat;
using System.Collections.Generic;
using System.Net.Mail;
using System.Text;

namespace KommoAIAgent.Infrastructure.Services
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
        private readonly ITenantContext _tenant;
        private readonly IRateLimiter _limiter;
        private readonly IKnowledgeStore _kb;

        public WebhookHandler(
            IKommoApiService kommoService,
            IAiService aiService,
            ILogger<WebhookHandler> logger,
            IConfiguration configuration,
            IMemoryCache cache,
            IChatMemoryStore conv,
            IMessageBuffer msgBuffer,
            LastImageCache lastImage,
            ITenantContext tenant,
            IRateLimiter limiter,
            IKnowledgeStore kb
            )
        {
            _kommoService = kommoService;
            _aiService = aiService;
            _logger = logger;
            _configuration = configuration;
            _cache = cache;
            _conv = conv;
            _msgBuffer = msgBuffer;
            _lastImage = lastImage;
            _tenant = tenant;
            _limiter = limiter;
            _kb = kb;
        }

        /// <summary>
        /// Procesa el payload del webhook entrante de Kommo.
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        public async Task ProcessIncomingMessageAsync(KommoWebhookPayload payload)
        {
            // Extraer y validar el mensaje
            var messageDetails = payload?.Message?.AddedMessages?.FirstOrDefault();
            if (messageDetails is null || !messageDetails.LeadId.HasValue)
            {
                _logger.LogWarning("Webhook recibido pero no contenía un mensaje válido con LeadId. Se ignora.");
                return;
            }

            // Deduplicación simple por messageId (evita reprocesar el mismo webhook)
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
            var attachmentCount = messageDetails.Attachments?.Count ?? 0;

            // Debounce ON/OFF (bypass para pruebas)
            var debounceEnabled = _configuration.GetValue("Debounce:Enabled", true);
            if (!debounceEnabled)
            {
                _logger.LogInformation("Debounce deshabilitado; procesando turno inmediato (LeadId={LeadId})", leadId);
                await ProcessAggregatedTurnAsync(leadId, userMessage, messageDetails.Attachments ?? new List<AttachmentInfo>());
                return;
            }

            // Debounce: encolar y salir; el buffer hará flush y llamará al orquestador.
            _logger.LogInformation(
                "Debounce habilitado; encolando para flush (LeadId={LeadId}, text='{Text}', atts={Atts})",
                leadId, userMessage, attachmentCount
            );

            // Fire-and-forget CON manejo de errores
            _ = Task.Run(async () =>
            {
                try
                {
                    await _msgBuffer.OfferAsync(
                        leadId,
                        userMessage,
                        messageDetails.Attachments ?? new List<AttachmentInfo>(),
                        async (id, agg) => await ProcessAggregatedTurnAsync(id, agg.Text, agg.Attachments)
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al encolar mensaje en buffer (lead={LeadId})", leadId);
                }
            });
        }

        /// <summary>
        /// Porcesa un turno agregado (después de debounce)
        /// Es quien llama a la IA y actualiza Kommo.
        /// </summary>
        /// <param name="leadId"></param>
        /// <param name="userText"></param>
        /// <param name="attachments"></param>
        /// <returns></returns>
        private async Task ProcessAggregatedTurnAsync(long leadId, string userText, List<AttachmentInfo> attachments, CancellationToken ct = default)
        {
            _logger.LogInformation("Procesando TURNO AGREGADO para Lead {LeadId}. Texto='{Text}', Adjuntos={AdjCount}",
                leadId, userText, attachments?.Count ?? 0);

            const int KOMMO_FIELD_MAX = 8000;      // único lugar
            const float GuardrailScore = 0.22f;

            try
            {
                // Limitar bursts por tenant/lead (lee Budgets.BurstPerMinute)
                var tenantSlug = _tenant.CurrentTenantId.Value;
                if (!await _limiter.TryConsumeAsync(tenantSlug, leadId, ct))
                {
                    _logger.LogWarning("Rate limited: tenant={Tenant}, lead={LeadId}", tenantSlug, leadId);                   
                    return; // se detiene el procesamiento para no llamar a OpenAI.
                }

                // Construir el historial de mensajes para enviar a la IA
                var messages = await ChatComposer.BuildHistoryMessagesAsync(
                _conv, _tenant, leadId, historyTurns: 10, ct);

                string aiResponse;

                var img = attachments?.FirstOrDefault(AttachmentHelper.IsImage);

                if (img != null && !string.IsNullOrWhiteSpace(img.Url))
                {
                    // Procesar imagen + texto, desde URL (descargar primero)
                    var (bytes, mime, fileName) = await _kommoService.DownloadAttachmentAsync(img.Url!);
                    if (string.IsNullOrWhiteSpace(mime))
                        mime = AttachmentHelper.GuessMimeFromUrlOrName(img.Url!, fileName);


                    // Guardar en caché la última imagen para este lead
                    _lastImage.SetLastImage(tenantSlug, leadId, bytes, mime);

                    // Prompt por defecto si la imagen viene sin texto
                    var prompt = string.IsNullOrWhiteSpace(userText)
                        ? "Describe la imagen y extrae cualquier texto visible."
                        : userText;

                    ChatComposer.AppendUserTextAndImage(messages, prompt, bytes, mime);
                    aiResponse = await _aiService.CompleteAsync(messages, maxTokens: 600, model: "gpt-4o", ct);

                    await _conv.AppendUserAsync(_tenant, leadId, userText, ct);
                    await _conv.AppendAssistantAsync(_tenant, leadId, aiResponse, ct);
                }
                else
                {
                    //Rama de solo texto (sin imagen)
                    // Recuperación semántica (RAG) antes de llamar a la IA
                    // Buscamos topK=6 fragmentos relevantes en la KB del tenant
                    // y los añadimos como un system message de "CONTEXT".

                    List<KbChunkHit>? hits = null;
                    try
                    {
                        // Cache por tenant+query+k (opcional; usa IMemoryCache inyectado)
                        var cacheKey = $"kbhits:{tenantSlug}:{userText}:{6}";
                        if (!_cache.TryGetValue(cacheKey, out hits))
                        {
                            hits = (await _kb.SearchAsync(tenantSlug, userText, topK: 6, ct: ct)).ToList();
                            _cache.Set(cacheKey, hits, TimeSpan.FromSeconds(30));
                        }

                        //Logs de la búsqueda
                        if (hits.Count > 0)
                            _logger.LogInformation("RAG hits={Count}, topScore={Top:0.000}", hits.Count, hits[0].Score);
                        else
                            _logger.LogInformation("RAG sin resultados para tenant={Tenant}", tenantSlug);

                        if (hits.Count == 0 || hits[0].Score < GuardrailScore)
                        {
                            var safeReply =
                                "No encuentro información suficiente en la base de conocimiento para responder con precisión. " +
                                "¿Podrías darme más detalle (por ejemplo, producto, ciudad o tipo de trámite)?";

                            // Persistimos conversación igualmente
                            await _conv.AppendUserAsync(_tenant, leadId, userText, ct);
                            await _conv.AppendAssistantAsync(_tenant, leadId, safeReply, ct);

                            // Publicamos en Kommo usando el mismo campo largo de siempre
                          
                            await _kommoService.UpdateLeadMensajeIAAsync(
                                leadId,
                                TextUtil.Truncate(safeReply, KOMMO_FIELD_MAX),
                                CancellationToken.None);

                            _logger.LogInformation("RAG guardrail: respuesta segura enviada sin invocar IA (lead={LeadId})", leadId);
                            return;
                        }
                            // Umbral de confianza para decidir si aportamos CONTEXT o pedimos más datos, se recomienda >=0.3
                            const float MinScore = 0.28f;

                        //Si hay hits buenos, los añadimos al prompt
                        if (hits.Count > 0 && hits[0].Score >= MinScore)
                        {
                            // Construimos BLOQUE CONTEXTO
                            var sbCtx = new StringBuilder();
                            sbCtx.AppendLine(
                                "CONTEXT (RAG): Usa EXCLUSIVAMENTE estos fragmentos para responder. " +
                                "Si el contexto no contiene la respuesta, dilo explícitamente y pide más detalles.\n"
                            );

                            // Añadir cada fragmento con su índice y título
                            for (int i = 0; i < hits.Count; i++)
                            {
                                var h = hits[i];
                                var title = string.IsNullOrWhiteSpace(h.Title) ? "Documento" : h.Title!;
                                // Limitar tamaño de cada trozo para cuidar tokens
                                sbCtx.AppendLine($"[{i + 1}] ({title}) {TextUtil.Truncate(h.Text, 450)}");
                            }

                            
                            // Inyectamos el contexto ANTES del mensaje de usuario
                            messages.Add(ChatMessage.CreateSystemMessage(sbCtx.ToString()));
                        }
                    }
                    catch (Exception rex)
                    {
                        // Falla en retrieval: seguimos sin contexto, pero conservadores
                        _logger.LogWarning(rex, "RAG search falló; se continúa sin contexto.");
                        messages.Add(ChatMessage.CreateSystemMessage(
                            "El contexto no está disponible por un error de recuperación. " +
                            "Responde de forma general y pide detalles; no inventes datos específicos."
                        ));
                    }

                    //  Finalmente añadimos el mensaje del usuario y pedimos a la IA
                    if (_lastImage.TryGetLastImage(tenantSlug, leadId, out LastImageCache.ImageCtx last))
                    {
                        // Si hay una imagen reciente en cache, reusamos multimodal
                        ChatComposer.AppendUserTextAndImage(messages, userText, last.  Bytes, last.Mime);
                        aiResponse = await _aiService.CompleteAsync(messages, maxTokens: 600, model: "gpt-4o", ct);
                    }
                    else
                    {
                        // Solo texto
                        ChatComposer.AppendUserText(messages, userText);
                        aiResponse = await _aiService.CompleteAsync(messages, maxTokens: 400, model: null, ct);
                    }

                    // Persistimos conversación
                    await _conv.AppendUserAsync(_tenant, leadId, userText, ct);
                    await _conv.AppendAssistantAsync(_tenant, leadId, aiResponse, ct);
                }

                await _kommoService.UpdateLeadMensajeIAAsync(
                    leadId,
                    TextUtil.Truncate(aiResponse, KOMMO_FIELD_MAX),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando TURNO AGREGADO para Lead {LeadId}", leadId);
            }
        }
    }
}


