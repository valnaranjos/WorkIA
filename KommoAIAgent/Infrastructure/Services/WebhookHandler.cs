using KommoAIAgent.Api.Contracts;
using KommoAIAgent.Application.Common;
using KommoAIAgent.Application.Connectors;
using KommoAIAgent.Application.Interfaces;
using KommoAIAgent.Domain.Tenancy;
using KommoAIAgent.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using NpgsqlTypes;
using OpenAI.Chat;
using System.Collections.Generic;
using System.Data.Common;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

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
        private readonly IRagRetriever _ragRetriever;
        private readonly ITokenBudget _tokenBudget;
        private readonly IConnectorFactory _connectorFactory;
        private readonly IIntentDetector _intentDetector;
        private readonly NpgsqlDataSource _dataSource;



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
            IRagRetriever ragRetriever,
            ITokenBudget tokenBudget,
            IConnectorFactory connectorFactory,
            IIntentDetector intentDetector,
            NpgsqlDataSource dataSource)
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
            _ragRetriever = ragRetriever;
            _tokenBudget = tokenBudget;
            _connectorFactory = connectorFactory;
            _intentDetector = intentDetector;
            _dataSource = dataSource;
        }

        /// <summary>
        /// Procesa el payload del webhook entrante de Kommo.
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        public async Task ProcessIncomingMessageAsync(KommoWebhookPayload payload)
        {
            _logger.LogWarning("🔍 DEBUG: ProcessIncomingMessageAsync INICIADO");
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

                // 🆕 ========== DETECCIÓN DE INTENCIÓN EXTERNA ==========
                // Solo si NO hay imágenes adjuntas (las imágenes van directo a IA)
                var hasImage = attachments?.Any(AttachmentHelper.IsImage) ?? false;

                if (!hasImage && !string.IsNullOrWhiteSpace(userText))
                {
                    var intent = await _intentDetector.DetectAsync(userText, tenantSlug, ct);

                    if (intent.RequiresConnector && intent.Confidence >= 0.7f)
                    {
                        _logger.LogInformation(
                            "External intent detected: capability={Capability}, confidence={Confidence}",
                            intent.Capability, intent.Confidence
                        );

                        var connectorResult = await HandleExternalActionAsync(
                            leadId,
                            intent,
                            userText,
                            ct
                        );

                        if (connectorResult.Success)
                        {
                            // Conector manejó exitosamente la acción
                            await _conv.AppendUserAsync(_tenant, leadId, userText, ct);
                            await _conv.AppendAssistantAsync(_tenant, leadId, connectorResult.Message!, ct);

                            await _kommoService.UpdateLeadMensajeIAAsync(
                                leadId,
                                TextUtil.Truncate(connectorResult.Message!, KOMMO_FIELD_MAX),
                                ct
                            );

                            _logger.LogInformation(
                                "External action completed successfully (lead={LeadId}, capability={Capability})",
                                leadId, intent.Capability
                            );

                            return; // Salir aquí, no llamar a IA
                        }
                        else
                        {
                            // Conector falló, continuar con respuesta de IA (fallback)
                            _logger.LogWarning(
                                "External connector failed, falling back to AI (lead={LeadId}, error={Error})",
                                leadId, connectorResult.ErrorDetails
                            );

                            // Opcional: agregar contexto del error al prompt de la IA
                            // Para que pueda decir "Lo siento, tuve un problema al procesar tu solicitud..."
                        }
                    }
                }



                // Construir el historial de mensajes para enviar a la IA
                var messages = await ChatComposer.BuildHistoryMessagesAsync(
                _conv, _tenant, leadId, historyTurns: 10, ct);


                var inputEstimate = AiUtil.EstimateTokens(string.Join("\n", messages.Select(m => m.Content?.ToString() ?? string.Empty)));
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

                    // ---- GUARDRAIL: presupuesto antes de invocar IA (multimodal) ----
                    var estTotalImg = inputEstimate + 600; // 600 = el mismo maxTokens que usarás abajo
                    if (!await _tokenBudget.CanConsumeAsync(_tenant, estTotalImg, ct))
                    {
                        const string budgetMsg = "He alcanzado el presupuesto de uso asignado. Intentémoslo más tarde.";
                        await _conv.AppendAssistantAsync(_tenant, leadId, budgetMsg, ct);
                        await _kommoService.UpdateLeadMensajeIAAsync(
                            leadId, TextUtil.Truncate(budgetMsg, KOMMO_FIELD_MAX), CancellationToken.None);
                        _logger.LogWarning("Budget guardrail (imagen) tenant={Tenant} estTokens={Est}", _tenant.CurrentTenantId, estTotalImg);
                        return;
                    }
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
                    float topScore = 0f;
                    try
                    {
                        // 🔹 Declaramos cacheKey ANTES de usarlo
                        var cacheKey = $"kbhits:{tenantSlug}:{userText}:{6}";

                        // Cache por tenant+query+k
                        if (!_cache.TryGetValue(cacheKey, out hits))
                        {
                            var (hitsList, ts) = await _ragRetriever.RetrieveAsync(tenantSlug, userText, topK: 6, ct);
                            topScore = ts;
                            hits = hitsList.ToList(); // Reasigna (no redeclares)
                            _cache.Set(cacheKey, hits, TimeSpan.FromSeconds(30));
                        }
                        else
                        {
                            topScore = (hits.Count > 0) ? hits[0].Score : 0f;
                        }

                        if (hits.Count > 0)
                            _logger.LogInformation("RAG hits={Count}, topScore={Top:0.000}", hits.Count, topScore);
                        else
                            _logger.LogInformation("RAG sin resultados para tenant={Tenant}", tenantSlug);

                        if (hits.Count == 0 || topScore < GuardrailScore)
                        {
                            var safeReply =
                                "No encuentro información suficiente en la base de conocimiento para responder con precisión. " +
                                "¿Podrías darme más detalle (por ejemplo, producto, ciudad o tipo de trámite)?";

                            await _conv.AppendUserAsync(_tenant, leadId, userText, ct);
                            await _conv.AppendAssistantAsync(_tenant, leadId, safeReply, ct);

                            await _kommoService.UpdateLeadMensajeIAAsync(
                                leadId,
                                TextUtil.Truncate(safeReply, KOMMO_FIELD_MAX),
                                CancellationToken.None);

                            _logger.LogInformation("RAG guardrail: respuesta segura enviada sin invocar IA (lead={LeadId})", leadId);
                            return;
                        }

                        // Inyectar contexto RAG si el score es suficiente
                        const float MinScore = 0.28f;
                        if (topScore >= MinScore)
                        {
                            var sbCtx = new StringBuilder();
                            sbCtx.AppendLine(
                                "CONTEXT (RAG): Usa EXCLUSIVAMENTE estos fragmentos para responder. " +
                                "Si el contexto no contiene la respuesta, dilo explícitamente y pide más detalles.\n"
                            );

                            for (int i = 0; i < hits.Count; i++)
                            {
                                var h = hits[i];
                                var title = string.IsNullOrWhiteSpace(h.Title) ? "Documento" : h.Title!;
                                sbCtx.AppendLine($"[{i + 1}] ({title}) {TextUtil.Truncate(h.Text, 450)}");
                            }

                            messages.Add(ChatMessage.CreateSystemMessage(sbCtx.ToString()));
                        }
                    }
                    catch (Exception rex)
                    {
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
                        ChatComposer.AppendUserTextAndImage(messages, userText, last.Bytes, last.Mime);
                        aiResponse = await _aiService.CompleteAsync(messages, maxTokens: 600, model: "gpt-4o", ct);
                    }
                    else
                    {
                        // Solo texto
                        ChatComposer.AppendUserText(messages, userText);

                        // ---- GUARDRAIL: presupuesto antes de invocar IA (texto) ----
                        var estTotalText = inputEstimate + 400; // 400 = el mismo maxTokens que usarás abajo
                        if (!await _tokenBudget.CanConsumeAsync(_tenant, estTotalText, ct))
                        {
                            const string budgetMsg = "He alcanzado el presupuesto de uso asignado. Intentémoslo más tarde.";
                            await _conv.AppendAssistantAsync(_tenant, leadId, budgetMsg, ct);
                            await _kommoService.UpdateLeadMensajeIAAsync(
                                leadId, TextUtil.Truncate(budgetMsg, KOMMO_FIELD_MAX), CancellationToken.None);
                            _logger.LogWarning("Budget guardrail (texto) tenant={Tenant} estTokens={Est}", _tenant.CurrentTenantId, estTotalText);
                            return;
                        }
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


        /// <summary>
        /// Maneja una acción externa invocando el conector apropiado
        /// </summary>
        private async Task<ConnectorResponse> HandleExternalActionAsync(
            long leadId,
            ExternalIntent intent,
            string userMessage,
            CancellationToken ct = default)
        {
            var tenantSlug = _tenant.CurrentTenantId.Value;

            try
            {
                // 1. Obtener el conector
                IExternalConnector? connector = null;

                if (!string.IsNullOrWhiteSpace(intent.ConnectorType))
                {
                    connector = await _connectorFactory.GetConnectorAsync(
                        tenantSlug,
                        intent.ConnectorType,
                        ct
                    );
                }
                else if (!string.IsNullOrWhiteSpace(intent.Capability))
                {
                    // Buscar por capability si no se especificó conector
                    connector = await _connectorFactory.FindConnectorByCapabilityAsync(
                        tenantSlug,
                        intent.Capability,
                        ct
                    );
                }

                if (connector is null)
                {
                    _logger.LogWarning(
                        "No connector found for intent (tenant={Tenant}, type={Type}, capability={Cap})",
                        tenantSlug, intent.ConnectorType, intent.Capability
                    );

                    return new ConnectorResponse
                    {
                        Success = false,
                        Message = "Lo siento, no tengo acceso a ese sistema en este momento.",
                        ErrorCode = "CONNECTOR_NOT_FOUND"
                    };
                }

                // 2. Invocar el conector
                var startTime = DateTime.UtcNow;

                var response = await connector.InvokeAsync(
                    intent.Capability!,
                    intent.Parameters,
                    ct
                );

                var duration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

                // 3. Registrar log de invocación
                await LogConnectorInvocationAsync(
                    tenantSlug,
                    connector,
                    intent.Capability!,
                    intent.Parameters,
                    response,
                    duration,
                    leadId,
                    userMessage,
                    ct
                );

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error handling external action (tenant={Tenant}, capability={Cap})",
                    tenantSlug, intent.Capability
                );

                return new ConnectorResponse
                {
                    Success = false,
                    Message = "Ocurrió un error al procesar tu solicitud. Un agente te ayudará pronto.",
                    ErrorDetails = ex.Message,
                    ErrorCode = "INTERNAL_ERROR"
                };
            }
        }

        /// <summary>
        /// Registra la invocación de un conector en la BD (para auditoría y métricas)
        /// </summary>
        private async Task LogConnectorInvocationAsync(
            string tenantSlug,
            IExternalConnector connector,
            string capability,
            Dictionary<string, object> parameters,
            ConnectorResponse response,
            int durationMs,
            long leadId,
            string userMessage,
            CancellationToken ct = default)
        {
            try
            {
                await using var conn = await _dataSource.OpenConnectionAsync(ct);

                const string sql = @"
            INSERT INTO connector_invocation_logs (
                tenant_slug,
                connector_id,
                connector_type,
                capability,
                request_params,
                success,
                status_code,
                response_data,
                error_message,
                duration_ms,
                lead_id,
                user_message
            )
            SELECT 
                @tenant,
                tc.id,
                @type,
                @capability,
                @params::jsonb,
                @success,
                @statusCode,
                @responseData::jsonb,
                @errorMsg,
                @duration,
                @leadId,
                @userMsg
            FROM tenant_connectors tc
            WHERE tc.tenant_slug = @tenant AND tc.connector_type = @type
            LIMIT 1;
        ";

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("tenant", NpgsqlDbType.Text, tenantSlug);
                cmd.Parameters.AddWithValue("type", NpgsqlDbType.Text, connector.ConnectorType);
                cmd.Parameters.AddWithValue("capability", NpgsqlDbType.Text, capability);
                cmd.Parameters.AddWithValue("params", NpgsqlDbType.Jsonb,
                    JsonSerializer.Serialize(parameters));
                cmd.Parameters.AddWithValue("success", NpgsqlDbType.Boolean, response.Success);
                cmd.Parameters.AddWithValue("statusCode", NpgsqlDbType.Integer,
                    response.Metadata?.GetValueOrDefault("statusCode") ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("responseData", NpgsqlDbType.Jsonb,
                    response.Data is not null ? JsonSerializer.Serialize(response.Data) : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("errorMsg", NpgsqlDbType.Text,
                    response.ErrorDetails ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("duration", NpgsqlDbType.Integer, durationMs);
                cmd.Parameters.AddWithValue("leadId", NpgsqlDbType.Bigint, leadId);
                cmd.Parameters.AddWithValue("userMsg", NpgsqlDbType.Text,
                    TextUtil.Truncate(userMessage, 500));

                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log connector invocation");
                // No lanzar excepción, es solo logging
            }
        }
    }
}


