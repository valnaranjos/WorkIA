using KommoAIAgent.Services.Interfaces;
using Npgsql;
using NpgsqlTypes;
using System.Data.Common;

namespace KommoAIAgent.Services
{
    /// <summary>
    /// UPSERT por día: suma los deltas en la fila (tenant,provider,model,date).
    /// </summary>
    public sealed class PostgresAIUsageTracker : IAIUsageTracker
    {
        private readonly NpgsqlDataSource _ds;
        private readonly ILogger<PostgresAIUsageTracker> _log;

        public PostgresAIUsageTracker(NpgsqlDataSource ds, ILogger<PostgresAIUsageTracker> log)
        {
            _ds = ds; _log = log;
        }

        private static DateTime TodayUtc() => DateTime.UtcNow.Date;

        public Task TrackEmbeddingAsync(string tenant, string provider, string model, int charCount, double? estCostUsd = null, CancellationToken ct = default)
            => UpsertAsync(tenant, provider, model, TodayUtc(), chatIn: 0, chatOut: 0, embChars: charCount, calls: 1, errors: 0, estCostUsd, ct);

        public Task TrackChatAsync(string tenant, string provider, string model, int inTokens, int outTokens, double? estCostUsd = null, CancellationToken ct = default)
            => UpsertAsync(tenant, provider, model, TodayUtc(), chatIn: inTokens, chatOut: outTokens, embChars: 0, calls: 1, errors: 0, estCostUsd, ct);

        public Task TrackErrorAsync(string tenant, string provider, string model, CancellationToken ct = default)
            => UpsertAsync(tenant, provider, model, TodayUtc(), chatIn: 0, chatOut: 0, embChars: 0, calls: 0, errors: 1, estCostUsd: null, ct);

        private async Task UpsertAsync(string tenant, string provider, string model, DateTime date, int chatIn, int chatOut, int embChars, int calls, int errors, double? estCostUsd, CancellationToken ct)
        {
            await using var conn = await _ds.OpenConnectionAsync(ct);

            const string sql = @"
INSERT INTO tenant_usage_daily
  (tenant_slug, provider, model, date, chat_in_tokens, chat_out_tokens, emb_char_count, calls, errors, est_cost_usd)
VALUES
  (@t, @p, @m, @d, @in, @out, @emb, @calls, @errs, @cost)
ON CONFLICT (tenant_slug, provider, model, date)
DO UPDATE SET
  chat_in_tokens  = tenant_usage_daily.chat_in_tokens  + EXCLUDED.chat_in_tokens,
  chat_out_tokens = tenant_usage_daily.chat_out_tokens + EXCLUDED.chat_out_tokens,
  emb_char_count  = tenant_usage_daily.emb_char_count  + EXCLUDED.emb_char_count,
  calls           = tenant_usage_daily.calls           + EXCLUDED.calls,
  errors          = tenant_usage_daily.errors          + EXCLUDED.errors,
  est_cost_usd    = COALESCE(tenant_usage_daily.est_cost_usd, 0) + COALESCE(EXCLUDED.est_cost_usd, 0);
";
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

            _log.LogInformation("usage.upsert tenant={Tenant} provider={Provider} model={Model} date={Date} +emb_chars={Emb} +inTok={In} +outTok={Out} +calls={Calls} +errors={Errs}",
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
SELECT COALESCE(SUM(emb_char_count),0),
       COALESCE(SUM(chat_in_tokens),0),
       COALESCE(SUM(chat_out_tokens),0),
       COALESCE(SUM(calls),0),
       COALESCE(SUM(errors),0)
FROM tenant_usage_daily
WHERE tenant_slug=@t AND date BETWEEN @from AND @to;";

            await using var conn = await _ds.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("t", NpgsqlTypes.NpgsqlDbType.Text, tenant);
            cmd.Parameters.AddWithValue("from", NpgsqlTypes.NpgsqlDbType.Date, from);
            cmd.Parameters.AddWithValue("to", NpgsqlTypes.NpgsqlDbType.Date, to);

            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            if (await rdr.ReadAsync(ct))
                return (rdr.GetInt32(0), rdr.GetInt32(1), rdr.GetInt32(2), rdr.GetInt32(3), rdr.GetInt32(4));

            return (0, 0, 0, 0, 0);
        }
    }
}
