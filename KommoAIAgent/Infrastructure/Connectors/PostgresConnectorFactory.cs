using KommoAIAgent.Application.Connectors;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace KommoAIAgent.Infrastructure.Connectors;

/// <summary>
/// Factory que crea conectores desde la base de datos PostgreSQL.
/// </summary>
public sealed class PostgresConnectorFactory : IConnectorFactory
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PostgresConnectorFactory> _logger;

    private const string CacheKeyPrefix = "connector:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public PostgresConnectorFactory(
        NpgsqlDataSource dataSource,
        IHttpClientFactory httpFactory,
        IMemoryCache cache,
        ILogger<PostgresConnectorFactory> logger)
    {
        _dataSource = dataSource;
        _httpFactory = httpFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IExternalConnector?> GetConnectorAsync(
        string tenantSlug,
        string connectorType,
        CancellationToken ct = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{tenantSlug}:{connectorType}";

        if (_cache.TryGetValue(cacheKey, out IExternalConnector? cached))
        {
            _logger.LogDebug("Cache HIT: connector {Type} for tenant {Tenant}", connectorType, tenantSlug);
            return cached;
        }

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const string sql = @"
            SELECT 
                id,
                connector_type,
                display_name,
                endpoint_url,
                auth_type,
                auth_config,
                capabilities,
                timeout_ms,
                retry_count,
                cooldown_until
            FROM tenant_connectors
            WHERE tenant_slug = @slug 
              AND connector_type = @type 
              AND is_active = true
            LIMIT 1;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("slug", NpgsqlDbType.Text, tenantSlug);
        cmd.Parameters.AddWithValue("type", NpgsqlDbType.Text, connectorType);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            _logger.LogWarning(
                "Connector {Type} not found for tenant {Tenant}",
                connectorType, tenantSlug
            );
            return null;
        }

        // Circuit breaker: verificar cooldown
        var cooldownUntil = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9);
        if (cooldownUntil.HasValue && cooldownUntil.Value > DateTime.UtcNow)
        {
            _logger.LogWarning(
                "Connector {Type} in cooldown until {Until} (tenant {Tenant})",
                connectorType, cooldownUntil.Value, tenantSlug
            );
            return null;
        }

        var connector = CreateConnectorFromReader(reader, tenantSlug);

        // Cachear
        _cache.Set(cacheKey, connector, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            Size = 1 // 🔧 FIX: Especificar tamaño para el SizeLimit
        });

        _logger.LogInformation(
            "Connector {Type} loaded for tenant {Tenant}",
            connectorType, tenantSlug
        );

        return connector;
    }

    public async Task<IReadOnlyList<IExternalConnector>> GetAllConnectorsAsync(
        string tenantSlug,
        CancellationToken ct = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{tenantSlug}:all";

        if (_cache.TryGetValue(cacheKey, out List<IExternalConnector>? cached))
        {
            _logger.LogDebug("Cache HIT: all connectors for tenant {Tenant}", tenantSlug);
            return cached!;
        }

        var connectors = new List<IExternalConnector>();

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const string sql = @"
            SELECT 
                id,
                connector_type,
                display_name,
                endpoint_url,
                auth_type,
                auth_config,
                capabilities,
                timeout_ms,
                retry_count,
                cooldown_until
            FROM tenant_connectors
            WHERE tenant_slug = @slug 
              AND is_active = true
            ORDER BY display_name;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("slug", NpgsqlDbType.Text, tenantSlug);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            // Circuit breaker check
            var cooldownUntil = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9);
            if (cooldownUntil.HasValue && cooldownUntil.Value > DateTime.UtcNow)
            {
                _logger.LogDebug(
                    "Skipping connector {Type} (in cooldown)",
                    reader.GetString(1)
                );
                continue;
            }

            connectors.Add(CreateConnectorFromReader(reader, tenantSlug));
        }

        // Cachear
        _cache.Set(cacheKey, connectors, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            Size = connectors.Count // 🔧 FIX: Tamaño basado en cantidad de conectores
        });

        _logger.LogInformation(
            "Loaded {Count} connectors for tenant {Tenant}",
            connectors.Count, tenantSlug
        );

        return connectors;
    }

    public async Task<IExternalConnector?> FindConnectorByCapabilityAsync(
        string tenantSlug,
        string capability,
        CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const string sql = @"
            SELECT 
                id,
                connector_type,
                display_name,
                endpoint_url,
                auth_type,
                auth_config,
                capabilities,
                timeout_ms,
                retry_count,
                cooldown_until
            FROM tenant_connectors
            WHERE tenant_slug = @slug 
              AND is_active = true
              AND capabilities @> @capability::jsonb
              AND (cooldown_until IS NULL OR cooldown_until < now())
            ORDER BY id
            LIMIT 1;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("slug", NpgsqlDbType.Text, tenantSlug);
        cmd.Parameters.AddWithValue("capability", NpgsqlDbType.Jsonb, $"[\"{capability}\"]");

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            _logger.LogDebug(
                "No connector found with capability {Capability} for tenant {Tenant}",
                capability, tenantSlug
            );
            return null;
        }

        var connector = CreateConnectorFromReader(reader, tenantSlug);

        _logger.LogInformation(
            "Found connector {Type} for capability {Capability} (tenant {Tenant})",
            connector.ConnectorType, capability, tenantSlug
        );

        return connector;
    }

    private IExternalConnector CreateConnectorFromReader(NpgsqlDataReader reader, string tenantSlug)
    {
        var connectorType = reader.GetString(1);
        var displayName = reader.GetString(2);
        var endpointUrl = reader.GetString(3);
        var authType = reader.GetString(4);
        var authConfigJson = reader.GetString(5);
        var capabilitiesJson = reader.GetString(6);
        var timeoutMs = reader.GetInt32(7);
        var retryCount = reader.GetInt32(8);

        var authConfig = JsonDocument.Parse(authConfigJson);
        var capabilitiesDoc = JsonDocument.Parse(capabilitiesJson);
        var capabilities = capabilitiesDoc.RootElement
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        var httpClient = _httpFactory.CreateClient(connectorType);
        var logger = NullLogger<HttpWebhookConnector>.Instance;


        return new HttpWebhookConnector(
            httpClient,
        connectorType,
        displayName,
        endpointUrl,
        authType,
        authConfig,
        capabilities,
        timeoutMs,
        retryCount,
        logger,
        tenantSlug
        );
    }
}