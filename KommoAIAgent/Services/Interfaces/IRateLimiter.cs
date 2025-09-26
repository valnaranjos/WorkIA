namespace KommoAIAgent.Services.Interfaces
{
    /// <summary>
    /// Limita solicitudes por tenant (y opcionalmente por lead).
    /// </summary>
    public interface IRateLimiter
    {
        /// <summary>
        /// Intenta consumir 1 “turno IA” del bucket del tenant/lead.
        /// Devuelve true si hay cupo; false si se superó el límite.
        /// </summary>
        Task<bool> TryConsumeAsync(string tenant, long leadId, CancellationToken ct = default);
    }
}
