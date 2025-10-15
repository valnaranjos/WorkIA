using KommoAIAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KommoAIAgent.Infrastructure.Services;

public interface IRagRetriever
{
    Task<(List<(string Text, string? Title, float Score)> Hits, float TopScore)> RetrieveAsync(
        string tenantSlug, string userText, int topK, CancellationToken ct);
}

public sealed class RagRetriever : IRagRetriever
{
    private readonly IEmbedder _embedder;
    private readonly IKnowledgeStore _kb;
    private readonly ILogger<RagRetriever> _logger;

    public RagRetriever(IEmbedder embedder, IKnowledgeStore kb, ILogger<RagRetriever> logger)
    {
        _embedder = embedder;
        _kb = kb;
        _logger = logger;
    }

    public async Task<(List<(string, string?, float)>, float)> RetrieveAsync(
        string tenantSlug, string userText, int topK, CancellationToken ct)
    {
        var (embedding, embChars) = await _embedder.EmbedAsync(tenantSlug, userText, ct);
        var results = await _kb.SearchAsync(tenantSlug, embedding, topK, ct);
        var list = results.Select(r => (r.Text, r.Title, r.Score)).ToList();
        var top = list.Count > 0 ? list.Max(h => h.Item3) : 0f;

        _logger.LogInformation("RAG hits={Count}, topScore={Top:0.000} tenant={Tenant}",
            list.Count, top, tenantSlug);

        return (list, top);
    }
}
