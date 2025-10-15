namespace KommoAIAgent.Application.Interfaces
{
    /// <summary>
    /// Interfaz para proveedores de embeddings (OpenAI, AzureOpenAI, Anthropic, etc.)
    /// Si se llega a usar otro proveedor, se creará como OpenAIEmbeddingProvider con base en esta interfaz.
    /// </summary>
    public interface IEmbeddingProvider
    {
        string ProviderId { get; }   
        string Model { get; }
        int Dimensions { get; }// p.ej. 1536

        //Embed un solo texto
        Task<float[]> EmbedTextAsync(string text, CancellationToken ct = default);

        //Embed batch de textos
        Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
    }
}
