using KommoAIAgent.Application.Interfaces;
namespace KommoAIAgent.Infrastructure.Services
{
    /// <summary>
    /// Proveedor de embeddings usando OpenAI.
    /// </summary>
    public class OpenAIEmbeddingProvider : IEmbeddingProvider
    {
        private readonly OpenAiService _openai;
        public OpenAIEmbeddingProvider(OpenAiService openai) => _openai = openai;

        public string ProviderId => "openai";
        public string Model => "text-embedding-3-small";
        public int Dimensions => 1536;

        public Task<float[]> EmbedTextAsync(string text, CancellationToken ct = default)
            => _openai.GetEmbeddingAsync(Model, text, ct);

        public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => _openai.GetEmbeddingsAsync(Model, texts, ct);
    }
}
