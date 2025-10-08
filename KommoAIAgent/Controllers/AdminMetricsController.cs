using KommoAIAgent.Api.Security;
using KommoAIAgent.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using System.Data;

namespace KommoAIAgent.Controllers;

/// <summary>
/// Controlador para la administración de métricas y reportes.
/// </summary>
[ApiController]
[Route("admin/metrics")]
[AdminApiKey]
public sealed class AdminMetricsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AdminMetricsController> _logger;

    public AdminMetricsController(AppDbContext db, ILogger<AdminMetricsController> logger)
    {
        _db = db;
        _logger = logger;
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
    /// Endpoint de reporte de uso de IA por tenant y día.
    /// </summary>
    /// <param name="tenant"></param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage(
        [FromQuery] string tenant,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenant))
            return BadRequest(new { error = "tenant is required" });

        var f = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
        var t = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        var mustClose = conn.State != ConnectionState.Open;
        if (mustClose) await conn.OpenAsync(ct);

        try
        {
            //Consulta SQL directa para evitar overhead de EF Core en lectura masiva
            const string sql = @"
            SELECT tenant_slug, provider, model, date, emb_char_count, chat_in_tokens, chat_out_tokens, calls, errors
            FROM tenant_usage_daily
            WHERE tenant_slug = @t AND date BETWEEN @f AND @to
            ORDER BY date, provider, model;";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("t", tenant);
            cmd.Parameters.AddWithValue("f", NpgsqlDbType.Date, new DateTime(f.Year, f.Month, f.Day));
            cmd.Parameters.AddWithValue("to", NpgsqlDbType.Date, new DateTime(t.Year, t.Month, t.Day));

            var rows = new List<UsageRowDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new UsageRowDto
                {
                    Tenant_Slug = reader.GetString(0),
                    Provider = reader.GetString(1),
                    Model = reader.GetString(2),
                    Date = reader.GetDateTime(3).Date,
                    Emb_Char_Count = reader.GetInt32(4),
                    Chat_In_Tokens = reader.GetInt32(5),
                    Chat_Out_Tokens = reader.GetInt32(6),
                    Calls = reader.GetInt32(7),
                    Errors = reader.GetInt32(8),
                });
            }

            return Ok(new { count = rows.Count, rows });
        }
        finally
        {
            if (mustClose) await conn.CloseAsync();
        }
    }
}
