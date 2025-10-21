using KommoAIAgent.Api.Contracts;
using KommoAIAgent.Application.Common;
using KommoAIAgent.Application.Connectors;
using KommoAIAgent.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace KommoAIAgent.Api.Controllers;

/// <summary>
/// 🧪 Controller de prueba para simular webhooks de Kommo SIN tener cuenta
/// DELETE este archivo en producción
/// </summary>
[ApiController]
[Route("test/webhook")]
public class TestWebhookController : ControllerBase
{
    private readonly IWebhookHandler _webhookHandler;
    private readonly ILogger<TestWebhookController> _logger;

    public TestWebhookController(
        IWebhookHandler webhookHandler,
        ILogger<TestWebhookController> logger)
    {
        _webhookHandler = webhookHandler;
        _logger = logger;
    }

    /// <summary>
    /// Simula un mensaje entrante de Kommo
    /// POST /test/webhook/simulate
    /// </summary>
    [HttpPost("simulate")]
    public async Task<IActionResult> SimulateIncomingMessage([FromBody] TestMessageRequest request)
    {
        _logger.LogInformation(
            "🧪 TEST: Simulando mensaje de Kommo (leadId={LeadId}, texto='{Text}')",
            request.LeadId,
            request.Text
        );

        var payload = new KommoWebhookPayload
        {
            Message = new MessageData
            {
                AddedMessages = new List<MessageDetails>
            {
                new MessageDetails
                {
                    MessageId = Guid.NewGuid().ToString(),
                    Type = "incoming",
                    Text = request.Text,
                    ChatId = "test_chat_" + request.LeadId,
                    LeadId = request.LeadId,
                    EntityType = "leads",
                    Attachments = new List<AttachmentInfo>()
                }
            }
            }
        };

        // 🔧 CAMBIO: AWAIT en lugar de fire-and-forget (solo para test)
        try
        {
            await _webhookHandler.ProcessIncomingMessageAsync(payload);

            _logger.LogInformation("🧪 TEST: Procesamiento completado");

            return Ok(new
            {
                message = "Webhook procesado exitosamente",
                leadId = request.LeadId,
                text = request.Text,
                note = "Revisa los logs de connector_invocation_logs para ver el resultado."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🧪 TEST: Error procesando webhook");

            return StatusCode(500, new
            {
                error = "Error procesando webhook",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Lista los últimos logs de invocaciones a conectores
    /// GET /test/webhook/logs?tenant=contactcentercalculaser&limit=10
    /// </summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetRecentLogs(
        [FromQuery] string tenant,
        [FromQuery] int limit = 10,
        [FromServices] Npgsql.NpgsqlDataSource dataSource = null!)
    {
        if (string.IsNullOrWhiteSpace(tenant))
            return BadRequest(new { error = "Parámetro 'tenant' requerido" });

        await using var conn = await dataSource.OpenConnectionAsync();
        const string sql = @"
            SELECT 
                invoked_at,
                connector_type,
                capability,
                request_params,
                success,
                response_data,
                error_message,
                duration_ms,
                lead_id,
                user_message
            FROM connector_invocation_logs
            WHERE tenant_slug = @tenant
            ORDER BY invoked_at DESC
            LIMIT @limit;
        ";

        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant", tenant);
        cmd.Parameters.AddWithValue("limit", limit);

        var logs = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            logs.Add(new
            {
                invokedAt = reader.GetDateTime(0),
                connectorType = reader.GetString(1),
                capability = reader.GetString(2),
                requestParams = reader.GetString(3),
                success = reader.GetBoolean(4),
                responseData = reader.IsDBNull(5) ? null : reader.GetString(5),
                errorMessage = reader.IsDBNull(6) ? null : reader.GetString(6),
                durationMs = reader.GetInt32(7),
                leadId = reader.IsDBNull(8) ? (long?)null : reader.GetInt64(8),
                userMessage = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return Ok(new { tenant, count = logs.Count, logs });
    }

    /// <summary>
    /// Limpia el historial conversacional de un lead (para tests repetidos)
    /// DELETE /test/webhook/conversation/{leadId}?tenant=contactcentercalculaser
    /// </summary>
    [HttpDelete("conversation/{leadId}")]
    public async Task<IActionResult> ClearConversation(
        long leadId,
        [FromQuery] string tenant,
        [FromServices] IChatMemoryStore memoryStore = null!)
    {
        if (string.IsNullOrWhiteSpace(tenant))
            return BadRequest(new { error = "Parámetro 'tenant' requerido" });

        await memoryStore.ClearAsync(tenant, leadId);

        _logger.LogInformation("🧪 TEST: Historial limpiado (tenant={Tenant}, lead={LeadId})", tenant, leadId);

        return Ok(new { message = "Historial conversacional limpiado", tenant, leadId });
    }

    /// <summary>
    /// 🧪 Prueba directa del conector (sin IntentDetector)
    /// POST /test/webhook/direct-connector
    /// </summary>
    [HttpPost("direct-connector")]
    public async Task<IActionResult> TestConnectorDirectly(
        [FromQuery] string tenant,
        [FromQuery] string connectorType,
        [FromQuery] string capability,
        [FromBody] Dictionary<string, object> parameters,
        [FromServices] IConnectorFactory connectorFactory)
    {
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(connectorType))
            return BadRequest(new { error = "Parámetros 'tenant' y 'connectorType' requeridos" });

        _logger.LogInformation(
            "🧪 TEST: Invocación directa de conector (tenant={Tenant}, type={Type}, capability={Cap})",
            tenant, connectorType, capability
        );

        var connector = await connectorFactory.GetConnectorAsync(tenant, connectorType);
        if (connector is null)
            return NotFound(new { error = "Conector no encontrado o inactivo" });

        var response = await connector.InvokeAsync(capability, parameters);

        return Ok(new
        {
            connector = new
            {
                type = connector.ConnectorType,
                displayName = connector.DisplayName
            },
            request = new { capability, parameters },
            response
        });
    }
}

/// <summary>
/// DTO para simular mensajes
/// </summary>
public record TestMessageRequest
{
    public long LeadId { get; init; } = 99999; // ID de prueba
    public required string Text { get; init; }
}