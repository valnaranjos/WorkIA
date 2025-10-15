using KommoAIAgent.Api.Contracts;
using KommoAIAgent.Api.Security;
using KommoAIAgent.Application.Interfaces;
using KommoAIAgent.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using System.Data;

namespace KommoAIAgent.Api.Controllers;

/// <summary>
/// Controlador para la administración de métricas y reportes.
/// </summary>
[ApiController]
[Route("admin/metrics")]
[AdminApiKey]
public sealed class AdminMetricsController : ControllerBase
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IAiCostCatalog _costs;
    private readonly ITenantContext _tenant;
    private readonly ILogger<AdminMetricsController> _logger;

    public AdminMetricsController(
        NpgsqlDataSource dataSource,
        IAiCostCatalog costs,
        ITenantContext tenant, 
        ILogger<AdminMetricsController> logger)
    {
        _dataSource = dataSource;
        _costs = costs;
        _tenant = tenant;
        _logger = logger;
    }


    /// <summary>
    /// Obtiene el resumen de uso de IA por provider/model en un rango dado.
    /// </summary>
    /// <param name="tenant"></param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] string tenant, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tenant)) return BadRequest(new { error = "tenant requerido" });

        var fromDate = (from ?? DateTime.UtcNow.Date.AddDays(-30)).Date;
        var toDate = (to ?? DateTime.UtcNow.Date).Date;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const string sql = @"
        SELECT
            u.provider,
            u.model,
            COALESCE(SUM(u.embedding_chars),0) AS embedding_chars,
            COALESCE(SUM(u.input_tokens),0)    AS input_tokens,
            COALESCE(SUM(u.output_tokens),0)   AS output_tokens,
            COALESCE(SUM(u.calls),0)           AS calls,
            COALESCE(SUM(u.errors),0)          AS errors
        FROM ia_usage_daily u
        WHERE u.tenant_slug = @t
          AND u.day >= @from
          AND u.day <= @to
        GROUP BY u.provider, u.model
        ORDER BY calls DESC, output_tokens DESC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("t", tenant);
        cmd.Parameters.AddWithValue("from", fromDate);
        cmd.Parameters.AddWithValue("to", toDate);

        var items = new List<object>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new
            {
                provider = rd.GetString(0),
                model = rd.GetString(1),
                embedding_chars = rd.GetInt64(2),
                input_tokens = rd.GetInt64(3),
                output_tokens = rd.GetInt64(4),
                calls = rd.GetInt64(5),
                errors = rd.GetInt64(6)
            });
        }

        return Ok(new { tenant, from = fromDate, to = toDate, items });
    }


    /// <summary>
    /// Obtiene el uso diario de IA para un tenant diario.
    /// GET /admin/metrics/daily?tenant=slug&days=30
    /// </summary>
    /// <param name="tenant"></param>
    /// <param name="days"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpGet("daily")]
    public async Task<IActionResult> Daily([FromQuery] string tenant, [FromQuery] int days = 7, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenant)) return BadRequest(new { error = "tenant requerido" });
        if (days <= 0 || days > 90) days = 14;

        var fromDate = DateTime.UtcNow.Date.AddDays(-days);
        var toDate = DateTime.UtcNow.Date;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const string sql = @"
        SELECT
            u.day,
            u.provider,
            u.model,
            u.embedding_chars,
            u.input_tokens,
            u.output_tokens,
            u.calls,
            u.errors
        FROM ia_usage_daily u
        WHERE u.tenant_slug = @t
          AND u.day >= @from
          AND u.day <= @to
        ORDER BY u.day DESC, u.provider, u.model;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("t", tenant);
        cmd.Parameters.AddWithValue("from", fromDate);
        cmd.Parameters.AddWithValue("to", toDate);

        var items = new List<object>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new
            {
                day = rd.GetDateTime(0).ToString("yyyy-MM-dd"),
                provider = rd.GetString(1),
                model = rd.GetString(2),
                embedding_chars = rd.GetInt64(3),
                input_tokens = rd.GetInt64(4),
                output_tokens = rd.GetInt64(5),
                calls = rd.GetInt64(6),
                errors = rd.GetInt64(7)
            });
        }

        return Ok(new { tenant, from = fromDate, to = toDate, items });
    }



    /// <summary>
    /// GET /admin/metrics/errors?tenant=slug&limit=100
    /// Errores recientes de IA para un tenant (logs de ia_logs).
    /// </summary>
    /// <param name="tenant"></param>
    /// <param name="limit"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpGet("errors")]
    public async Task<IActionResult> Errors([FromQuery] string? tenant, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenant)) return BadRequest(new { error = "tenant requerido" });
        limit = Math.Clamp(limit, 1, 500);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        const string sql = @"
        SELECT
            l.id,
            l.when_utc,
            l.provider,
            l.model,
            l.operation,
            l.message
        FROM ia_logs l
        WHERE l.tenant_slug = @t
          AND (
                l.operation ILIKE '%error%' OR
                l.message   ILIKE '%error%' OR
                l.message   ILIKE '%fail%'   OR
                l.message   ILIKE '%exception%'
              )
        ORDER BY l.when_utc DESC
        LIMIT @limit;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("t", tenant);
        cmd.Parameters.AddWithValue("limit", limit);

        var items = new List<object>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new
            {
                id = rd.GetInt64(0),
                when_utc = rd.GetDateTime(1),
                provider = rd.IsDBNull(2) ? null : rd.GetString(2),
                model = rd.IsDBNull(3) ? null : rd.GetString(3),
                operation = rd.IsDBNull(4) ? null : rd.GetString(4),
                message = rd.IsDBNull(5) ? null : rd.GetString(5)
            });
        }

        return Ok(new { tenant, items });
    }


    // GET /admin/metrics/costs
    /// <summary>
    /// Obtiene todas las filas de costos de IA.
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpGet("costs")]
    public async Task<ActionResult<IEnumerable<CostRowResponse>>> ListCosts(CancellationToken ct)
    {
        var list = new List<CostRowResponse>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
        SELECT provider, model, input_per_1k, output_per_1k, emb_per_1k_tokens, updated_utc
        FROM ia_costs
        ORDER BY provider, model;", conn);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            list.Add(new CostRowResponse(
                rd.GetString(0), rd.GetString(1),
                rd.IsDBNull(2) ? 0 : rd.GetDecimal(2),
                rd.IsDBNull(3) ? 0 : rd.GetDecimal(3),
                rd.IsDBNull(4) ? 0 : rd.GetDecimal(4),
                rd.GetDateTime(5)
            ));
        }
        return Ok(list);
    }

    // PUT /admin/metrics/costs  (upsert)}
    /// <summary>
    /// Efectúa un upsert de una fila de costos de IA.
    /// </summary>
    /// <param name="req"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpPut("costs")]
    public async Task<ActionResult> UpsertCost([FromBody] CostUpsertRequest req, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
        INSERT INTO ia_costs(provider, model, input_per_1k, output_per_1k, emb_per_1k_tokens, updated_utc)
        VALUES(@p, @m, @in1k, @out1k, @emb1k, now())
        ON CONFLICT (provider, model) DO UPDATE SET
            input_per_1k=EXCLUDED.input_per_1k,
            output_per_1k=EXCLUDED.output_per_1k,
            emb_per_1k_tokens=EXCLUDED.emb_per_1k_tokens,
            updated_utc=now();", conn);

        cmd.Parameters.AddWithValue("p", NpgsqlDbType.Text, req.Provider.ToLowerInvariant().Trim());
        cmd.Parameters.AddWithValue("m", NpgsqlDbType.Text, req.Model.ToLowerInvariant().Trim());
        cmd.Parameters.AddWithValue("in1k", NpgsqlDbType.Numeric, req.InputPer1K);
        cmd.Parameters.AddWithValue("out1k", NpgsqlDbType.Numeric, req.OutputPer1K);
        cmd.Parameters.AddWithValue("emb1k", NpgsqlDbType.Numeric, req.EmbPer1KTokens);

        await cmd.ExecuteNonQueryAsync(ct);

        // Nota: el catálogo usa caché 10 min; podrías invalidarla si usas MemoryCache con Remove.
        return NoContent();
    }

    // DELETE /admin/metrics/costs/{provider}/{model}
    /// <summary>
    /// Borra una fila de costos de IA.
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="model"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpDelete("costs/{provider}/{model}")]
    public async Task<ActionResult> DeleteCost(string provider, string model, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
        DELETE FROM ia_costs WHERE provider=@p AND model=@m;", conn);

        cmd.Parameters.AddWithValue("p", NpgsqlDbType.Text, provider.ToLowerInvariant().Trim());
        cmd.Parameters.AddWithValue("m", NpgsqlDbType.Text, model.ToLowerInvariant().Trim());

        var n = await cmd.ExecuteNonQueryAsync(ct);
        return n > 0 ? NoContent() : NotFound();
    }



    /// <summary>
    /// Dto para una fila de uso diario de un tenant.
    /// </summary>
    public sealed class UsageRowDto
    {
        public string Tenant_Slug { get; set; } = default!;
        public string Provider { get; set; } = default!;
        public string Model { get; set; } = default!;
        public DateTime Date { get; set; }
        public int Emb_Char_Count { get; set; }
        public int Chat_In_Tokens { get; set; }
        public int Chat_Out_Tokens { get; set; }
        public int Calls { get; set; }
        public int Errors { get; set; }
    }

    /// <summary>
    /// DTO para upsert de costos de IA.
    /// </summary>
    /// <param name="Provider"></param>
    /// <param name="Model"></param>
    /// <param name="InputPer1K"></param>
    /// <param name="OutputPer1K"></param>
    /// <param name="EmbPer1KTokens"></param>
    public sealed record CostUpsertRequest(
    string Provider,
    string Model,
    decimal InputPer1K,
    decimal OutputPer1K,
    decimal EmbPer1KTokens
);

    /// <summary>
    /// DTO para una fila de costos de IA.
    /// </summary>
    /// <param name="Provider"></param>
    /// <param name="Model"></param>
    /// <param name="InputPer1K"></param>
    /// <param name="OutputPer1K"></param>
    /// <param name="EmbPer1KTokens"></param>
    /// <param name="UpdatedUtc"></param>
    public sealed record CostRowResponse(
        string Provider,
        string Model,
        decimal InputPer1K,
        decimal OutputPer1K,
        decimal EmbPer1KTokens,
        DateTime UpdatedUtc
    );
}
