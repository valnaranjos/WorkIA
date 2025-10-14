namespace KommoAIAgent.Services.Interfaces
{
    /// <summary>
    /// Registra contadores diarios por tenant/provider/modelo.
    /// MVP: emb_char_count (solo en MISS de embeddings), chat tokens y errores.
    /// </summary>
    public interface IAIUsageTracker
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="provider"></param>
        /// <param name="model"></param>
        /// <param name="charCount"></param>
        /// <param name="estCostUsd"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task TrackEmbeddingAsync(
            string tenant, string provider, string model,
            int charCount, double? estCostUsd = null,
            CancellationToken ct = default);


        /// <summary>
        /// Guarda 
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="provider"></param>
        /// <param name="model"></param>
        /// <param name="inTokens"></param>
        /// <param name="outTokens"></param>
        /// <param name="estCostUsd"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task TrackChatAsync(
            string tenant, string provider, string model,
            int inTokens, int outTokens, double? estCostUsd = null,
            CancellationToken ct = default);

        /// <summary>
        /// Guarda 
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="provider"></param>
        /// <param name="model"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task TrackErrorAsync(
            string tenant, string provider, string model,
            CancellationToken ct = default);


        /// <summary>
        /// Obtiene el total por mes x tenant
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="month"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<(int embChars, int chatIn, int chatOut, int calls, int errors)>
    GetMonthTotalsAsync(string tenant, DateOnly month, CancellationToken ct);


        /// <summary>
        /// Loguea un error detallado (no solo el contador) para análisis posterior.
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="provider"></param>
        /// <param name="model"></param>
        /// <param name="operation"></param>
        /// <param name="message"></param>
        /// <param name="raw"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task LogErrorAsync(
            string? tenant,
            string? provider,
            string? model,
            string operation,
            string message,
            object? raw = null,
            CancellationToken ct = default);
    }
}
