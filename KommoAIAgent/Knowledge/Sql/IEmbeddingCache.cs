namespace KommoAIAgent.Knowledge.Sql
{
    /// <summary>
    /// Interfaz para el caché de embeddings en DB SQL.
    /// </summary>
    public interface IEmbeddingCache
    {
        /// <summary>
        /// Obtiene un embedding del caché si existe.
        /// </summary>
        /// <param name="tenantSlug"></param>
        /// <param name="model"></param>
        /// <param name="textHash"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<float[]?> TryGetAsync(string tenantSlug, string model, string textHash, CancellationToken ct = default);


        /// <summary>
        /// Agrega un embedding al caché.
        /// </summary>
        /// <param name="tenantSlug"></param>
        /// <param name="model"></param>
        /// <param name="textHash"></param>
        /// <param name="vector"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task PutAsync(string tenantSlug, string model, string textHash, float[] vector, CancellationToken ct = default);
    }
}
