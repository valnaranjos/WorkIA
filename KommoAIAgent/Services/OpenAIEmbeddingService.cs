using KommoAIAgent.Knowledge;
using KommoAIAgent.Services;

/// <summary>
/// Servicio de embedding usando OpenAI.
/// </summary>
public sealed class OpenAIEmbeddingService : IEmbedder
{
    // Inyectar el servicio de OpenAI
    private readonly OpenAiService _openai;

    // Dimensiones del embedding y modelo a usar, se puede configurar
    public int Dimensions => 1536;

    //Modelo de embedding a usar, recomendado text-embedding-3-small por coste y prestaciones
    public string Model => "text-embedding-3-small";

   
    public OpenAIEmbeddingService(OpenAiService openai) => _openai = openai;

    // Obtener el embedding de un texto
    public Task<float[]> EmbedTextAsync(string text, CancellationToken ct = default)
        => _openai.GetEmbeddingAsync(Model, text, ct);

    // Obtener los embeddings de un lote de textos
    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        => _openai.GetEmbeddingsAsync(Model, texts, ct);
}
