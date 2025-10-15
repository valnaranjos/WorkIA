using KommoAIAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using System.Data.Common;

namespace KommoAIAgent.Infrastructure.Services
{
    /// <summary>
    /// UPSERT por día: suma los deltas en la fila (tenant,provider,model,date).
    /// </summary>
    public sealed class PostgresAIUsageTracker : IAIUsageTracker
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<PostgresAIUsageTracker> _logger;

        public PostgresAIUsageTracker(NpgsqlDataSource dataSource, ILogger<PostgresAIUsageTracker> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        private static DateTime TodayUtc() => DateTime.UtcNow.Date;

        public Task TrackEmbeddingAsync(string tenant, string provider, string model, int charCount, double? estCostUsd = null, CancellationToken ct = default)
            => UpsertAsync(tenant, provider, model, TodayUtc(), chatIn: 0, chatOut: 0, embChars: charCount, calls: 1, errors: 0, estCostUsd, ct);

        public Task TrackChatAsync(string tenant, string provider, string model, int inTokens, int outTokens, double? estCostUsd = null, CancellationToken ct = default)
            => UpsertAsync(tenant, provider, model, TodayUtc(), chatIn: inTokens, chatOut: outTokens, embChars: 0, calls: 1, errors: 0, estCostUsd, ct);

        public async Task TrackErrorAsync(string? tenant, string? provider, string? model, CancellationToken ct = default)
        {
            // Suma +1 error y +0 calls en tu tabla diaria (tenant_usage_daily)
            // o en la que estés usando. Ejemplo:
            const string sql = @"
INSERT INTO ia_usage_daily(tenant_slug, provider, model, day, input_tokens, output_tokens, embedding_chars, calls, errors, updated_utc)
VALUES (@tenant, @provider, @model, @day, 0, 0, 0, 0, 1, now())
ON CONFLICT (tenant_slug, provider, model, day)
DO UPDATE SET
  errors = ia_usage_daily.errors + 1,
  updated_utc = now();";

            var day = DateOnly.FromDateTime(DateTime.UtcNow.Date);

            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.Add("tenant", NpgsqlDbType.Text).Value = (object?)tenant ?? DBNull.Value;
            cmd.Parameters.Add("provider", NpgsqlDbType.Text).Value = (object?)provider ?? DBNull.Value;
            cmd.Parameters.Add("model", NpgsqlDbType.Text).Value = (object?)model ?? DBNull.Value;
            cmd.Parameters.Add("day", NpgsqlDbType.Date).Value = day;
            await cmd.ExecuteNonQueryAsync(ct);
        }


        private async Task UpsertAsync(string tenant, string provider, string model, DateTime date, int chatIn, int chatOut, int embChars, int calls, int errors, double? estCostUsd, CancellationToken ct)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            const string sql = @"
INSERT INTO ia_usage_daily
  (tenant_slug, provider, model, day, input_tokens, output_tokens, embedding_chars, calls, errors, updated_utc)
VALUES
  (@t, @p, @m, @d, @in, @out, @emb, @calls, @errs, now())
ON CONFLICT (tenant_slug, provider, model, day)
DO UPDATE SET
  input_tokens   = ia_usage_daily.input_tokens   + EXCLUDED.input_tokens,
  output_tokens  = ia_usage_daily.output_tokens  + EXCLUDED.output_tokens,
  embedding_chars= ia_usage_daily.embedding_chars+ EXCLUDED.embedding_chars,
  calls          = ia_usage_daily.calls          + EXCLUDED.calls,
  errors         = ia_usage_daily.errors         + EXCLUDED.errors,
  updated_utc    = now();";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("t", NpgsqlDbType.Text, tenant);
            cmd.Parameters.AddWithValue("p", NpgsqlDbType.Text, provider);
            cmd.Parameters.AddWithValue("m", NpgsqlDbType.Text, model);
            cmd.Parameters.AddWithValue("d", NpgsqlDbType.Date, date);
            cmd.Parameters.AddWithValue("in", NpgsqlDbType.Integer, chatIn);
            cmd.Parameters.AddWithValue("out", NpgsqlDbType.Integer, chatOut);
            cmd.Parameters.AddWithValue("emb", NpgsqlDbType.Integer, embChars);
            cmd.Parameters.AddWithValue("calls", NpgsqlDbType.Integer, calls);
            cmd.Parameters.AddWithValue("errs", NpgsqlDbType.Integer, errors);
            if (estCostUsd.HasValue)
                cmd.Parameters.AddWithValue("cost", NpgsqlDbType.Numeric, estCostUsd.Value);
            else
                cmd.Parameters.AddWithValue("cost", NpgsqlDbType.Numeric, DBNull.Value);

            _logger.LogInformation("usage.upsert tenant={Tenant} provider={Provider} model={Model} date={Date} +emb_chars={Emb} +inTok={In} +outTok={Out} +calls={Calls} +errors={Errs}",
    tenant, provider, model, date, embChars, chatIn, chatOut, calls, errors);


            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>
        /// Obtiene el total mensual x tenat
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="month"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<(int embChars, int chatIn, int chatOut, int calls, int errors)>
        GetMonthTotalsAsync(string tenant, DateOnly month, CancellationToken ct)
        {
            // Primer y último día del mes
            var from = new DateOnly(month.Year, month.Month, 1);
            var to = from.AddMonths(1).AddDays(-1);

            const string sql = @"
SELECT COALESCE(SUM(embedding_chars),0),
       COALESCE(SUM(input_tokens),0),
       COALESCE(SUM(output_tokens),0),
       COALESCE(SUM(calls),0),
       COALESCE(SUM(errors),0)
FROM ia_usage_daily
WHERE tenant_slug=@t AND day BETWEEN @from AND @to;";

            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("t", NpgsqlDbType.Text, tenant);
            cmd.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
            cmd.Parameters.AddWithValue("to", NpgsqlDbType.Date, to);

            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            if (await rdr.ReadAsync(ct))
                return (rdr.GetInt32(0), rdr.GetInt32(1), rdr.GetInt32(2), rdr.GetInt32(3), rdr.GetInt32(4));

            return (0, 0, 0, 0, 0);
        }
    
   
       
        public async Task LogErrorAsync(string? tenant, string? provider, string? model, string operation, string message, object? raw = null, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO ia_logs(when_utc, tenant_slug, provider, model, operation, message, raw)
VALUES (now(), @tenant, @provider, @model, @op, @msg, @raw::jsonb);
";
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.Add("tenant", NpgsqlDbType.Text).Value = (object?)tenant ?? DBNull.Value;
            cmd.Parameters.Add("provider", NpgsqlDbType.Text).Value = (object?)provider ?? DBNull.Value;
            cmd.Parameters.Add("model", NpgsqlDbType.Text).Value = (object?)model ?? DBNull.Value;
            cmd.Parameters.Add("op", NpgsqlDbType.Text).Value = operation;
            cmd.Parameters.Add("msg", NpgsqlDbType.Text).Value = message;
            cmd.Parameters.Add("raw", NpgsqlDbType.Jsonb).Value = raw is null ? DBNull.Value : System.Text.Json.JsonSerializer.Serialize(raw);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
