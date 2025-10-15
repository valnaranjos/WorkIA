using Microsoft.Extensions.Configuration;

namespace KommoAIAgent.Application.Interfaces;

public interface IAiCostCatalog
{
    decimal EstimateUsd(string provider, string model, long inputTokens, long outputTokens, long embeddingChars);
}

/// <summary>
/// Clase para estimar costos de IA por provider/model, según configuración desde appsettings, se puede ajustar sin re-despliegue.
/// </summary>
public sealed class AiCostCatalog : IAiCostCatalog
{
    private readonly IConfiguration _cfg;

    public AiCostCatalog(IConfiguration cfg) => _cfg = cfg;

    // Nota: si no se configuras costos, devuelve 0.
    public decimal EstimateUsd(string provider, string model, long inputTokens, long outputTokens, long embeddingChars)
    {
        // Rutas de config:
        // AiCosts:{provider}:{model}:InputPer1K
        // AiCosts:{provider}:{model}:OutputPer1K
        // AiCosts:{provider}:EmbeddingsPer1KTokens
        //
        // Como almacenamos emb_chars (no tokens), aproximamos: tokens ≈ chars/4
        // Puedes cambiar esta heurística si prefieres emb_tokens directo.
        var p = (provider ?? "").ToLowerInvariant();
        var m = (model ?? "").ToLowerInvariant();

        decimal inPer1k = _cfg.GetValue<decimal?>($"AiCosts:{p}:{m}:InputPer1K") ?? 0m;
        decimal outPer1k = _cfg.GetValue<decimal?>($"AiCosts:{p}:{m}:OutputPer1K") ?? 0m;
        decimal embPer1kT = _cfg.GetValue<decimal?>($"AiCosts:{p}:EmbeddingsPer1KTokens") ?? 0m;

        decimal inCost = inputTokens / 1000m * inPer1k;
        decimal outCost = outputTokens / 1000m * outPer1k;

        var approxEmbTokens = embeddingChars / 4m; // heurística
        decimal embCost = approxEmbTokens / 1000m * embPer1kT;

        return inCost + outCost + embCost;
    }
}
