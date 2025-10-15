using KommoAIAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KommoAIAgent.Infrastructure.Services
{
    public interface IRagRetriever
    {
        Task<(IReadOnlyList<KbChunkHit> Hits, float TopScore)>
            RetrieveAsync(string tenantSlug, string userText, int topK, CancellationToken ct);
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
        public async Task<(IReadOnlyList<KbChunkHit> Hits, float TopScore)>
            RetrieveAsync(string tenantSlug, string userText, int topK, CancellationToken ct)
        {
            // Deja que el store haga el embed interno (así funciona tu PgVectorKnowledgeStore)
            var hits = await _kb.SearchAsync(tenantSlug, userText, topK, ct: ct);

            var top = (hits.Count > 0) ? hits[0].Score : 0f; // ya vienen ordenados por Score
            _logger.LogInformation("RAG hits={Count}, topScore={Top:0.000}, tenant={Tenant}",
                hits.Count, top, tenantSlug);

            return (hits, top);
        }
    }
}
