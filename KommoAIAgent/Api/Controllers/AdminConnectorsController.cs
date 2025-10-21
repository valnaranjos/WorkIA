using KommoAIAgent.Api.Security;
using KommoAIAgent.Application.Connectors;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace KommoAIAgent.Api.Controllers;

/// <summary>
/// Gestión de conectores externos por tenant, para conectar con microservicios externos a través de la API, a cualquier tenant.
/// 
/// </summary>
[ApiController]
[Route("admin/connectors")]
[AdminApiKey]
public class AdminConnectorsController : ControllerBase
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IConnectorFactory _connectorFactory;
    private readonly ILogger<AdminConnectorsController> _logger;

    public AdminConnectorsController(
        NpgsqlDataSource dataSource,
        IConnectorFactory connectorFactory,
        ILogger<AdminConnectorsController> logger)
    {
        _dataSource = dataSource;
        _connectorFactory = connectorFactory;
        _logger = logger;
    }

    /// <summary>
    /// Lista todos los conectores de un tenant
    /// GET /admin/connectors?tenant=calculaser
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListConnectors([FromQuery] string tenant, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenant))
            return BadRequest(new { error = "Parámetro 'tenant' requerido" });

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        const string sql = @"
            SELECT 
                id,
                connector_type,
                display_name,
                description,
                endpoint_url,
                auth_type,
                is_active,
                timeout_ms,
                retry_count,
                capabilities,
                failure_count,
                cooldown_until,
                created_utc,
                updated_utc
            FROM tenant_connectors
            WHERE tenant_slug = @tenant
            ORDER BY display_name;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant", tenant);

        var connectors = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            connectors.Add(new
            {
                id = reader.GetInt64(0),
                connectorType = reader.GetString(1),
                displayName = reader.GetString(2),
                description = reader.IsDBNull(3) ? null : reader.GetString(3),
                endpointUrl = reader.GetString(4),
                authType = reader.GetString(5),
                isActive = reader.GetBoolean(6),
                timeoutMs = reader.GetInt32(7),
                retryCount = reader.GetInt32(8),
                capabilities = reader.GetString(9), // JSON
                failureCount = reader.GetInt32(10),
                cooldownUntil = reader.IsDBNull(11) ? (DateTime?)null : reader.GetDateTime(11),
                createdUtc = reader.GetDateTime(12),
                updatedUtc = reader.GetDateTime(13)
            });
        }

        return Ok(new { tenant, count = connectors.Count, connectors });
    }

    /// <summary>
    /// Obtiene un conector específico por ID
    /// GET /admin/connectors/{id}
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetConnector(long id, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        const string sql = @"
            SELECT 
                id,
                tenant_slug,
                connector_type,
                display_name,
                description,
                endpoint_url,
                auth_type,
                auth_config,
                is_active,
                timeout_ms,
                retry_count,
                retry_delay_ms,
                capabilities,
                metadata,
                failure_count,
                failure_threshold,
                cooldown_until,
                created_utc,
                updated_utc,
                created_by,
                updated_by
            FROM tenant_connectors
            WHERE id = @id;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return NotFound(new { error = "Conector no encontrado" });

        var connector = new
        {
            id = reader.GetInt64(0),
            tenantSlug = reader.GetString(1),
            connectorType = reader.GetString(2),
            displayName = reader.GetString(3),
            description = reader.IsDBNull(4) ? null : reader.GetString(4),
            endpointUrl = reader.GetString(5),
            authType = reader.GetString(6),
            authConfig = reader.GetString(7), // JSON (⚠️ sensible)
            isActive = reader.GetBoolean(8),
            timeoutMs = reader.GetInt32(9),
            retryCount = reader.GetInt32(10),
            retryDelayMs = reader.GetInt32(11),
            capabilities = reader.GetString(12),
            metadata = reader.IsDBNull(13) ? null : reader.GetString(13),
            failureCount = reader.GetInt32(14),
            failureThreshold = reader.GetInt32(15),
            cooldownUntil = reader.IsDBNull(16) ? (DateTime?)null : reader.GetDateTime(16),
            createdUtc = reader.GetDateTime(17),
            updatedUtc = reader.GetDateTime(18),
            createdBy = reader.IsDBNull(19) ? null : reader.GetString(19),
            updatedBy = reader.IsDBNull(20) ? null : reader.GetString(20)
        };

        return Ok(connector);
    }

    /// <summary>
    /// Crea un nuevo conector con los datos: 
    /// POST /admin/connectors
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateConnector([FromBody] ConnectorCreateRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        const string sql = @"
            INSERT INTO tenant_connectors (
                tenant_slug,
                connector_type,
                display_name,
                description,
                endpoint_url,
                auth_type,
                auth_config,
                is_active,
                timeout_ms,
                retry_count,
                retry_delay_ms,
                capabilities,
                metadata,
                failure_threshold,
                created_by
            )
            VALUES (
                @tenant,
                @type,
                @name,
                @desc,
                @url,
                @authType,
                @authConfig::jsonb,
                @active,
                @timeout,
                @retry,
                @retryDelay,
                @capabilities::jsonb,
                @metadata::jsonb,
                @threshold,
                @createdBy
            )
            RETURNING id;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant", request.TenantSlug);
        cmd.Parameters.AddWithValue("type", request.ConnectorType);
        cmd.Parameters.AddWithValue("name", request.DisplayName);
        cmd.Parameters.AddWithValue("desc", request.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("url", request.EndpointUrl);
        cmd.Parameters.AddWithValue("authType", request.AuthType);
        cmd.Parameters.AddWithValue("authConfig", JsonSerializer.Serialize(request.AuthConfig));
        cmd.Parameters.AddWithValue("active", request.IsActive);
        cmd.Parameters.AddWithValue("timeout", request.TimeoutMs);
        cmd.Parameters.AddWithValue("retry", request.RetryCount);
        cmd.Parameters.AddWithValue("retryDelay", request.RetryDelayMs);
        cmd.Parameters.AddWithValue("capabilities", JsonSerializer.Serialize(request.Capabilities));
        cmd.Parameters.AddWithValue("metadata", request.Metadata is not null
            ? JsonSerializer.Serialize(request.Metadata)
            : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("threshold", request.FailureThreshold);
        cmd.Parameters.AddWithValue("createdBy", request.CreatedBy ?? "api");

        var newId = (long)(await cmd.ExecuteScalarAsync(ct))!;

        _logger.LogInformation(
            "Connector created: id={Id}, type={Type}, tenant={Tenant}",
            newId, request.ConnectorType, request.TenantSlug
        );

        return CreatedAtAction(
            nameof(GetConnector),
            new { id = newId },
            new { id = newId, message = "Conector creado exitosamente" }
        );
    }

    /// <summary>
    /// Actualiza un conector existente
    /// PUT /admin/connectors/{id}
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateConnector(
        long id,
        [FromBody] ConnectorUpdateRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        const string sql = @"
            UPDATE tenant_connectors
            SET 
                display_name = COALESCE(@name, display_name),
                description = COALESCE(@desc, description),
                endpoint_url = COALESCE(@url, endpoint_url),
                auth_type = COALESCE(@authType, auth_type),
                auth_config = COALESCE(@authConfig::jsonb, auth_config),
                is_active = COALESCE(@active, is_active),
                timeout_ms = COALESCE(@timeout, timeout_ms),
                retry_count = COALESCE(@retry, retry_count),
                retry_delay_ms = COALESCE(@retryDelay, retry_delay_ms),
                capabilities = COALESCE(@capabilities::jsonb, capabilities),
                metadata = COALESCE(@metadata::jsonb, metadata),
                failure_threshold = COALESCE(@threshold, failure_threshold),
                updated_by = @updatedBy,
                updated_utc = now()
            WHERE id = @id
            RETURNING id;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", request.DisplayName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("desc", request.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("url", request.EndpointUrl ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("authType", request.AuthType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("authConfig", request.AuthConfig is not null
            ? JsonSerializer.Serialize(request.AuthConfig)
            : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("active", request.IsActive ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("timeout", request.TimeoutMs ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("retry", request.RetryCount ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("retryDelay", request.RetryDelayMs ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("capabilities", request.Capabilities is not null
            ? JsonSerializer.Serialize(request.Capabilities)
            : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("metadata", request.Metadata is not null
            ? JsonSerializer.Serialize(request.Metadata)
            : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("threshold", request.FailureThreshold ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("updatedBy", request.UpdatedBy ?? "api");

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null)
            return NotFound(new { error = "Conector no encontrado" });

        _logger.LogInformation("Connector updated: id={Id}", id);

        return Ok(new { id, message = "Conector actualizado exitosamente" });
    }

    /// <summary>
    /// Elimina un conector (soft delete: is_active = false)
    /// DELETE /admin/connectors/{id}
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteConnector(long id, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        const string sql = @"
            UPDATE tenant_connectors
            SET is_active = false, updated_utc = now()
            WHERE id = @id
            RETURNING id;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null)
            return NotFound(new { error = "Conector no encontrado" });

        _logger.LogWarning("Connector deactivated: id={Id}", id);

        return Ok(new { id, message = "Conector desactivado" });
    }

    /// <summary>
    /// Health check de un conector
    /// POST /admin/connectors/{id}/health
    /// </summary>
    [HttpPost("{id}/health")]
    public async Task<IActionResult> HealthCheck(long id, [FromQuery] string tenant, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenant))
            return BadRequest(new { error = "Parámetro 'tenant' requerido" });

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        const string sql = @"
            SELECT connector_type
            FROM tenant_connectors
            WHERE id = @id AND tenant_slug = @tenant
            LIMIT 1;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant", tenant);

        var connectorType = (string?)(await cmd.ExecuteScalarAsync(ct));
        if (connectorType is null)
            return NotFound(new { error = "Conector no encontrado" });

        var connector = await _connectorFactory.GetConnectorAsync(tenant, connectorType, ct);
        if (connector is null)
            return StatusCode(503, new { healthy = false, message = "Conector no disponible" });

        var isHealthy = await connector.HealthCheckAsync(ct);

        return Ok(new
        {
            id,
            connectorType,
            healthy = isHealthy,
            checkedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Resetea el circuit breaker de un conector
    /// POST /admin/connectors/{id}/reset
    /// </summary>
    [HttpPost("{id}/reset")]
    public async Task<IActionResult> ResetCircuitBreaker(long id, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        const string sql = "SELECT reset_connector_cooldown(@id);";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Circuit breaker reset: connector={Id}", id);

        return Ok(new { id, message = "Circuit breaker reseteado" });
    }
}

// ==================== DTOs ====================

/// <summary>
/// DTO de creación de conector 
/// </summary>
public sealed record ConnectorCreateRequest
{
    public required string TenantSlug { get; init; }
    public required string ConnectorType { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required string EndpointUrl { get; init; }
    public string AuthType { get; init; } = "bearer";
    public required Dictionary<string, object> AuthConfig { get; init; }
    public bool IsActive { get; init; } = true;
    public int TimeoutMs { get; init; } = 15000;
    public int RetryCount { get; init; } = 3;
    public int RetryDelayMs { get; init; } = 1000;
    public required List<string> Capabilities { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
    public int FailureThreshold { get; init; } = 5;
    public string? CreatedBy { get; init; }
}

public sealed record ConnectorUpdateRequest
{
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? EndpointUrl { get; init; }
    public string? AuthType { get; init; }
    public Dictionary<string, object>? AuthConfig { get; init; }
    public bool? IsActive { get; init; }
    public int? TimeoutMs { get; init; }
    public int? RetryCount { get; init; }
    public int? RetryDelayMs { get; init; }
    public List<string>? Capabilities { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
    public int? FailureThreshold { get; init; }
    public string? UpdatedBy { get; init; }
}