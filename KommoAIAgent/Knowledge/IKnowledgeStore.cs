namespace KommoAIAgent.Knowledge
{
    // Referencia a un documento en la base de conocimiento
    public record KbDocRef(long DocumentId, string SourceId, string Title);

    // Resultado de una búsqueda de chunks relevantes
    public record KbChunkHit(long ChunkId, long DocumentId, string Text, float Score, string? Title);

    /// <summary>
    /// Interfaz de almacenamiento de conocimiento para la gestión de documentos y chunks para RAG y pasarlo a LLMs.
    /// </summary>
    public interface IKnowledgeStore
    {
        // Ingesta/actualización
        Task<KbDocRef> UpsertDocumentAsync(string tenant, string sourceId, string? title, string content, string[]? tags = null, CancellationToken ct = default);

        // Rechunking y embedding
        Task<int> RechunkAndEmbedAsync(string tenant, long documentId, CancellationToken ct = default);

        // Borrado de documento
        Task<bool> DeleteDocumentAsync(string tenant, string sourceId, CancellationToken ct = default);

        // Búsqueda de chunks relevantes
        Task<IReadOnlyList<KbChunkHit>> SearchAsync(string tenant, string query, int topK = 6, string[]? mustTags = null, CancellationToken ct = default);
    }
}