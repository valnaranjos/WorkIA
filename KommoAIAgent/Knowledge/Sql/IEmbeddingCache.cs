namespace KommoAIAgent.Knowledge.Sql
{
    /// <summary>
    /// Interfaz para el caché de embeddings en DB SQL.
    /// </summary>
    public interface IEmbeddingCache
    {
       /// <summary>
       /// 
       /// </summary>
       /// <param name="tenantSlug">Slug del tenant</param>
       /// <param name="provider">Proveedor de IA, listo para escalar en futuras versiones, configurado por tenant</param>
       /// <param name="model"></param>
       /// <param name="textHash"></param>
       /// <param name="ct"></param>
       /// <returns></returns>
        Task<float[]?> TryGetAsync(string tenantSlug, string provider, string model, string textHash, CancellationToken ct = default);


        /// <summary>
        /// Agrega o actualiza un embedding en el caché.
        /// </summary>
        /// <param name="tenantSlug">Slug del tenant</param>
        /// <param name="provider">Proveedor de IA, listo para escalar en futuras versiones, configurado por tenant</param>
        /// <param name="model"></param>
        /// <param name="textHash"></param>
        /// <param name="vector"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task PutAsync(string tenantSlug, string provider, string model, string textHash, float[] vector, CancellationToken ct = default);
    }
}
