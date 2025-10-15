using KommoAIAgent.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace KommoAIAgent.Infrastructure.Services;

/// <summary>
/// Objeto para estimar costos de IA por provider/model, leyendo datos desde tabla ia_costs en PostgreSQL.
/// </summary>
public sealed class PostgresAiCostCatalog : IAiCostCatalog
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _cfg;

    public PostgresAiCostCatalog(NpgsqlDataSource dataSource, IMemoryCache cache, IConfiguration cfg)
    {
        _dataSource = dataSource;
        _cache = cache;
        _cfg = cfg;
    }

    // Fila de costos por provider/model
    private sealed record CostRow(decimal InPer1K, decimal OutPer1K, decimal EmbPer1KTokens);

    /// <summary>
    /// Estima el coste en USD para la operación dada.
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="model"></param>
    /// <param name="inputTokens"></param>
    /// <param name="outputTokens"></param>
    /// <param name="embeddingChars"></param>
    /// <returns></returns>
    public decimal EstimateUsd(string provider, string model, long inputTokens, long outputTokens, long embeddingChars)
    {
        var p = (provider ?? "").ToLowerInvariant();
        var m = (model ?? "").ToLowerInvariant();
        var key = $"aicosts:{p}:{m}";

        var row = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10); // TTL de caché
            return LoadCostRow(p, m);
        });

        // Fallback a config si no hay fila en DB
        if (row is null)
        {
            decimal inPer1k = _cfg.GetValue<decimal?>($"AiCosts:{p}:{m}:InputPer1K") ?? 0m;
            decimal outPer1k = _cfg.GetValue<decimal?>($"AiCosts:{p}:{m}:OutputPer1K") ?? 0m;
            decimal embPer1kT = _cfg.GetValue<decimal?>($"AiCosts:{p}:EmbeddingsPer1KTokens") ?? 0m;
            row = new CostRow(inPer1k, outPer1k, embPer1kT);
        }

        decimal inCost = inputTokens / 1000m * row.InPer1K;
        decimal outCost = outputTokens / 1000m * row.OutPer1K;

        // Heurística: tokens ≈ chars/4 (si luego guardas emb_tokens, cámbialo aquí)
        var approxEmbTokens = embeddingChars / 4m;
        decimal embCost = approxEmbTokens / 1000m * row.EmbPer1KTokens;

        return inCost + outCost + embCost;
    }

    /// <summary>
    /// Carga una fila de costos desde la tabla ia_costs.
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="model"></param>
    /// <returns></returns>
    private CostRow? LoadCostRow(string provider, string model)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = new NpgsqlCommand(@"
            SELECT input_per_1k, output_per_1k, emb_per_1k_tokens
            FROM ia_costs
            WHERE provider=@p AND model=@m;", conn);

        cmd.Parameters.AddWithValue("p", NpgsqlDbType.Text, provider);
        cmd.Parameters.AddWithValue("m", NpgsqlDbType.Text, model);

        using var rd = cmd.ExecuteReader();
        if (rd.Read())
        {
            var in1k = rd.IsDBNull(0) ? 0m : rd.GetDecimal(0);
            var out1k = rd.IsDBNull(1) ? 0m : rd.GetDecimal(1);
            var emb1k = rd.IsDBNull(2) ? 0m : rd.GetDecimal(2);
            return new CostRow(in1k, out1k, emb1k);
        }
        return null;
    }
}
