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
        ///  soporte completo para extracción de imágenes + intent detection + conectores + RAG.
        /// </summary>
        /// <param name="leadId"></param>
        /// <param name="userText"></param>
        /// <param name="attachments"></param>
        /// <params name="ct"></params> 
        /// <returns></returns>
        private async Task ProcessAggregatedTurnAsync(
             long leadId,
             string userText,
             List<AttachmentInfo> attachments,
             CancellationToken ct = default)
        {
            _logger.LogInformation(
                "Procesando TURNO AGREGADO para Lead {LeadId}. Texto='{Text}', Adjuntos={AdjCount}",
                leadId, userText, attachments?.Count ?? 0
            );

            const int KOMMO_FIELD_MAX = 8000;
            const float GuardrailScore = 0.22f;

            try
            {
                // Limitar bursts por tenant/lead
                var tenantSlug = _tenant.CurrentTenantId.Value;
                if (!await _limiter.TryConsumeAsync(tenantSlug, leadId, ct))
                {
                    _logger.LogWarning("Rate limited: tenant={Tenant}, lead={LeadId}", tenantSlug, leadId);
                    return;
                }

                // ========== PASO 1: EXTRACCIÓN DE IMÁGENES Y DETECCIÓN DE INTENCIÓN ==========

                // Variables para almacenar datos del conector (si se invoca)
                object? externalData = null;
                string? externalDataSource = null;
                string? extractedTextFromImage = null;

                var hasImage = attachments?.Any(AttachmentHelper.IsImage) ?? false;
                var hasText = !string.IsNullOrWhiteSpace(userText);

                if (hasText && !hasImage)
                {
                    _lastImage.Remove(tenantSlug, leadId);
                    _logger.LogInformation("Cleared image cache for lead={Lead} (text without image)", leadId);
                }

                // CASO 1: Solo imagen (sin texto) - Extraer texto primero
                if (hasImage && !hasText && _tenant.Config?.AI?.EnableImageOCR == true)
                {
                    _logger.LogInformation("Image without text detected, extracting content first");

                    var imageAttachment = attachments!.First(AttachmentHelper.IsImage);
                    var (bytes, mime, fileName) = await _kommoService.DownloadAttachmentAsync(imageAttachment.Url!);

                    if (string.IsNullOrWhiteSpace(mime))
                        mime = AttachmentHelper.GuessMimeFromUrlOrName(imageAttachment.Url!, fileName);

                    // FASE 1: Extracción estructurada de la imagen
                    var extractionMessages = new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage(@"
Analiza esta imagen y extrae información estructurada.

Responde SOLO en formato JSON:
{
  ""description"": ""descripción breve"",
  ""extracted_text"": ""todo el texto visible"",
  ""entities"": {
    ""document_type"": ""tipo de documento si aplica (CC, TI, pasaporte, receta, factura, etc.)"",
    ""document_number"": ""número si es visible"",
    ""person_name"": ""nombre si es visible"",
    ""date"": ""fecha si es visible (formato ISO)"",
    ""amount"": ""monto si es visible"",
    ""other_key_info"": ""cualquier otra info relevante""
  },
  ""implicit_intent"": ""qué parece querer hacer el usuario"",
  ""search_terms"": ""términos clave para búsqueda""
}

IMPORTANTE: Responde SOLO el JSON, sin texto adicional.
")
            };

                    ChatComposer.AppendUserTextAndImage(
                        extractionMessages,
                        "Extrae información de esta imagen",
                        bytes,
                        mime
                    );

                    var extractionResponse = await _aiService.CompleteAsync(
                        extractionMessages,
                        maxTokens: 400,
                        model: "gpt-4o",
                        ct
                    );

                    _logger.LogInformation("Image extraction: {Response}", extractionResponse);

                    try
                    {
                        // Parsear JSON de extracción
                        var jsonMatch = System.Text.RegularExpressions.Regex.Match(
                            extractionResponse,
                            @"\{[\s\S]*\}"
                        );

                        if (jsonMatch.Success)
                        {
                            var extraction = JsonDocument.Parse(jsonMatch.Value);
                            var root = extraction.RootElement;

                            extractedTextFromImage = root.GetProperty("extracted_text").GetString() ?? "";

                            var implicitIntent = root.TryGetProperty("implicit_intent", out var intent)
                                ? intent.GetString()
                                : "";

                            // Combinar texto extraído + intención implícita
                            userText = $"{extractedTextFromImage} {implicitIntent}".Trim();

                            _logger.LogInformation(
                                "Synthesized userText from image: {Text}",
                                userText
                            );
                            // Borrar caché de img
                            _lastImage.Remove(tenantSlug, leadId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse image extraction JSON");
                    }
                }

                // PASO 2: DETECCIÓN DE INTENCIÓN (con o sin imagen)
                if (!string.IsNullOrWhiteSpace(userText)
     && _tenant.Config?.AI?.EnableAutoConnectorInvocation == true
     && !userText.Contains("no puedo identificar")) { 
                    var intent = await _intentDetector.DetectAsync(userText, tenantSlug, ct);

                    if (intent.RequiresConnector && intent.Confidence >= 0.7f)
                    {
                        _logger.LogInformation(
                            "External intent detected: capability={Capability}, confidence={Confidence}, source={Source}",
                            intent.Capability,
                            intent.Confidence,
                            extractedTextFromImage != null ? "image-extraction" : "user-text"
                        );

                        bool hasAllParams = CheckRequiredParameters(intent);

                        if (hasAllParams)
                        {
                            var connectorResult = await HandleExternalActionAsync(
                                leadId,
                                intent,
                                userText,
                                ct
                            );

                            if (connectorResult.Success)
                            {
                                _logger.LogInformation(
                                    "Connector invoked successfully (lead={LeadId}, capability={Capability})",
                                    leadId,
                                    intent.Capability
                                );

                                externalData = connectorResult.Data;
                                externalDataSource = $"{intent.ConnectorType}.{intent.Capability}";
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Connector failed (lead={LeadId}, error={Error})",
                                    leadId,
                                    connectorResult.ErrorDetails
                                );
                            }
                        }
                        else
                        {
                            _logger.LogInformation(
                                "Missing parameters for {Capability}, AI will handle",
                                intent.Capability
                            );
                        }
                    }
                }

                // PASO 3: Guardar mensaje del usuario (real o sintetizado)
                await _conv.AppendUserAsync(_tenant, leadId, userText ?? "", ct);

                // PASO 4: Construir contexto para la IA
                var messages = await ChatComposer.BuildHistoryMessagesAsync(
                    _conv, _tenant, leadId, historyTurns: 10, ct
                );

                var inputEstimate = AiUtil.EstimateTokens(
                    string.Join("\n", messages.Select(m => m.Content?.ToString() ?? string.Empty))
                );

                string aiResponse;
                var img = attachments?.FirstOrDefault(AttachmentHelper.IsImage);

                // ========== RAMA: IMAGEN ==========
                if (img != null && !string.IsNullOrWhiteSpace(img.Url))
                {
                    var (bytes, mime, fileName) = await _kommoService.DownloadAttachmentAsync(img.Url!);
                    if (string.IsNullOrWhiteSpace(mime))
                        mime = AttachmentHelper.GuessMimeFromUrlOrName(img.Url!, fileName);

                    _lastImage.SetLastImage(tenantSlug, leadId, bytes, mime);

                    var prompt = string.IsNullOrWhiteSpace(userText)
                        ? "Describe la imagen y extrae cualquier texto visible."
                        : userText;

                    // Guardrail de presupuesto
                    var estTotalImg = inputEstimate + 600;
                    if (!await _tokenBudget.CanConsumeAsync(_tenant, estTotalImg, ct))
                    {
                        const string budgetMsg = "He alcanzado el presupuesto de uso asignado. Intentémoslo más tarde.";
                        await _conv.AppendAssistantAsync(_tenant, leadId, budgetMsg, ct);
                        await _kommoService.UpdateLeadMensajeIAAsync(
                            leadId, TextUtil.Truncate(budgetMsg, KOMMO_FIELD_MAX), ct);
                        _logger.LogWarning("Budget guardrail (imagen) tenant={Tenant}", tenantSlug);
                        return;
                    }

                    // Si hay datos del conector, agregarlos al contexto
                    if (externalData != null)
                    {
                        var connectorContext = BuildConnectorContext(externalData, externalDataSource!);
                        messages.Add(ChatMessage.CreateSystemMessage(connectorContext));
                    }

                    ChatComposer.AppendUserTextAndImage(messages, prompt, bytes, mime);
                    aiResponse = await _aiService.CompleteAsync(messages, maxTokens: 600, model: "gpt-4o", ct);
                    
                    _lastImage.Remove(tenantSlug, leadId);
                    _logger.LogInformation("Cleared image cache after processing (lead={Lead})", leadId);
                }
                // ========== RAMA: SOLO TEXTO ==========
                else
                {
                    // PASO 4.1: RAG (SIEMPRE, incluso si hubo conector)
                    List<KbChunkHit>? hits = null;
                    float topScore = 0f;

                    try
                    {
                        var cacheKey = $"kbhits:{tenantSlug}:{userText}:{6}";

                        if (!_cache.TryGetValue(cacheKey, out hits))
                        {
                            var (hitsList, ts) = await _ragRetriever.RetrieveAsync(
                                tenantSlug, userText ?? "", topK: 6, ct);
                            topScore = ts;
                            hits = hitsList.ToList();
                            _cache.Set(cacheKey, hits, TimeSpan.FromSeconds(30));
                        }
                        else
                        {
                            topScore = (hits.Count > 0) ? hits[0].Score : 0f;
                        }

                        if (hits.Count > 0)
                            _logger.LogInformation("RAG hits={Count}, topScore={Top:0.000}", hits.Count, topScore);

                        // Si hay datos de conector, NO aplicar guardrail de RAG
                        if (externalData == null && (hits.Count == 0 || topScore < GuardrailScore))
                        {
                            var safeReply =
                                "No encuentro información suficiente en la base de conocimiento para responder con precisión. " +
                                "¿Podrías darme más detalle?";

                            await _conv.AppendAssistantAsync(_tenant, leadId, safeReply, ct);
                            await _kommoService.UpdateLeadMensajeIAAsync(
                                leadId, TextUtil.Truncate(safeReply, KOMMO_FIELD_MAX), ct);

                            _logger.LogInformation("RAG guardrail activado (lead={LeadId})", leadId);
                            return;
                        }

                        // Inyectar contexto RAG si el score es suficiente
                        const float MinScore = 0.28f;
                        if (topScore >= MinScore && hits.Count > 0)
                        {
                            var ragContext = BuildRagContext(hits);
                            messages.Add(ChatMessage.CreateSystemMessage(ragContext));
                        }
                    }
                    catch (Exception rex)
                    {
                        _logger.LogWarning(rex, "RAG search falló; continuando sin contexto");
                    }

                    // PASO 4.2: AGREGAR DATOS DEL CONECTOR (si los hay)
                    if (externalData != null)
                    {
                        var connectorContext = BuildConnectorContext(externalData, externalDataSource!);
                        messages.Add(ChatMessage.CreateSystemMessage(connectorContext));

                        _logger.LogInformation(
                            "Added external data context to IA (source={Source})",
                            externalDataSource
                        );
                    }

                    // PASO 4.3: AGREGAR MENSAJE DEL USUARIO
                    if (_lastImage.TryGetLastImage(tenantSlug, leadId, out LastImageCache.ImageCtx last)
     && last.Bytes?.Length > 0
     && last.Mime?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        // Reuso de imagen reciente en caché
                        ChatComposer.AppendUserTextAndImage(messages, userText ?? "", last.Bytes, last.Mime);
                        aiResponse = await _aiService.CompleteAsync(messages, maxTokens: 600, model: "gpt-4o", ct);
                    }
                    else
                    {
                        // Solo texto
                        ChatComposer.AppendUserText(messages, userText ?? "");

                        // Guardrail de presupuesto
                        var estTotalText = inputEstimate + 400;
                        if (!await _tokenBudget.CanConsumeAsync(_tenant, estTotalText, ct))
                        {
                            const string budgetMsg = "He alcanzado el presupuesto de uso asignado. Intentémoslo más tarde.";
                            await _conv.AppendAssistantAsync(_tenant, leadId, budgetMsg, ct);
                            await _kommoService.UpdateLeadMensajeIAAsync(
                                leadId, TextUtil.Truncate(budgetMsg, KOMMO_FIELD_MAX), ct);
                            _logger.LogWarning("Budget guardrail (texto) tenant={Tenant}", tenantSlug);
                            return;
                        }

                        aiResponse = await _aiService.CompleteAsync(messages, maxTokens: 400, model: null, ct);
                    }
                }

                // PASO 5: GUARDAR Y ENVIAR RESPUESTA
                await _conv.AppendAssistantAsync(_tenant, leadId, aiResponse, ct);

                try
                {
                    await SendResponseToKommoAsync(leadId, aiResponse, ct);
                    
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("AccessToken"))
                {
                    _logger.LogWarning("No se pudo actualizar Kommo (sin token) - lead={LeadId}", leadId);
                }

                _logger.LogInformation("Turn completed successfully (lead={LeadId})", leadId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando TURNO AGREGADO para Lead {LeadId}", leadId);
            }
        }

        // ========== MÉTODOS AUXILIARES ==========

        /// <summary>
        /// Verifica si el intent tiene todos los parámetros requeridos.
        /// </summary>
        private bool CheckRequiredParameters(ExternalIntent intent)
        {
            if (intent.Capability == "get_patient_appointments")
            {
                return intent.Parameters.ContainsKey("patientDocument") ||
                       intent.Parameters.ContainsKey("document");
            }

            if (intent.Capability == "cancel_appointment")
            {
                return intent.Parameters.ContainsKey("agendaId") ||
                       intent.Parameters.ContainsKey("agenda_id");
            }

            // Por defecto, asumir que tiene los parámetros necesarios
            return true;
        }

        /// <summary>
        /// Construye el contexto de RAG para la IA.
        /// </summary>
        private string BuildRagContext(List<KbChunkHit> hits)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CONTEXTO RELEVANTE DE LA BASE DE CONOCIMIENTO:");

            for (int i = 0; i < hits.Count; i++)
            {
                var h = hits[i];
                var title = string.IsNullOrWhiteSpace(h.Title) ? "Documento" : h.Title;
                sb.AppendLine($"\n[{i + 1}] {title}");
                sb.AppendLine(TextUtil.Truncate(h.Text, 400));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Construye el contexto de datos del conector para la IA.
        /// </summary>
        private string BuildConnectorContext(object data, string source)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            return $@"
DATOS DEL SISTEMA EXTERNO (fuente: {source}):
```json
{json}
```

INSTRUCCIONES:
- Estos datos provienen de un sistema externo y son información actualizada y confiable
- Analiza estos datos y responde al usuario en lenguaje natural y conversacional
- Presenta la información de forma clara, organizada y fácil de entender
- Usa un tono profesional pero cercano
- NO inventes información que no esté en los datos
- Si el usuario pregunta por detalles adicionales que no están en los datos, indícalo claramente
";
        }

        // ========== MÉTODOS AUXILIARES ==========

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

        /// <summary>
        /// Envía respuesta a Kommo, dividiéndola en múltiples mensajes si es necesario
        /// </summary>
        private async Task SendResponseToKommoAsync(long leadId, string message, CancellationToken ct)
        {
            const int MAX_KOMMO_LENGTH = 1000; // Límite seguro para Kommo

            // Si cabe en un mensaje, enviar directamente
            if (message.Length <= MAX_KOMMO_LENGTH)
            {
                await _kommoService.UpdateLeadMensajeIAAsync(leadId, message, ct);
                return;
            }

            _logger.LogInformation(
                "Message too long ({Length} chars), splitting for lead={Lead}",
                message.Length, leadId
            );

            // Dividir por dobles saltos de línea (párrafos)
            var paragraphs = message.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            var currentPart = new StringBuilder();
            int partNumber = 1;

            foreach (var paragraph in paragraphs)
            {
                var trimmedParagraph = paragraph.Trim();

                // Si agregar este párrafo excede el límite
                if (currentPart.Length + trimmedParagraph.Length + 2 > MAX_KOMMO_LENGTH)
                {
                    // Enviar parte acumulada
                    if (currentPart.Length > 0)
                    {
                        var part = currentPart.ToString().Trim();

                        _logger.LogInformation(
                            "Sending part {Part} ({Length} chars) to lead={Lead}",
                            partNumber, part.Length, leadId
                        );

                        await _kommoService.UpdateLeadMensajeIAAsync(leadId, part, ct);
                        await Task.Delay(500, ct); // Pausa entre mensajes

                        currentPart.Clear();
                        partNumber++;
                    }
                }

                currentPart.AppendLine(trimmedParagraph);
                currentPart.AppendLine(); // Espacio entre párrafos
            }

            // Enviar última parte
            if (currentPart.Length > 0)
            {
                var part = currentPart.ToString().Trim();

                _logger.LogInformation(
                    "Sending final part {Part} ({Length} chars) to lead={Lead}",
                    partNumber, part.Length, leadId
                );

                await _kommoService.UpdateLeadMensajeIAAsync(leadId, part, ct);
            }
        }
    }
}


