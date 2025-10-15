using KommoAIAgent.Application.Interfaces;

namespace KommoAIAgent.Domain.Tenancy
{
    /// <summary>
    /// Controla el presupuesto diario de tokens por tenant (y opcionalmente por lead).
    /// </summary>
    public interface ITokenBudget
    {
        /// <summary>
        /// Verifica si el tenant tiene cupo para consumir 'requestedTokens' hoy.
        /// No descuenta todavía; solo verifica.
        /// </summary>
        Task<bool> CanConsumeAsync(ITenantContext tenant, int requestedTokens, CancellationToken ct = default);

        /// <summary>
        /// Registra consumo real de tokens (p. ej., Usage.TotalTokens de OpenAI).
        /// </summary>
        Task AddUsageAsync(ITenantContext tenant, int usedTokens, CancellationToken ct = default);

        /// <summary>
        /// Retorna cuánto lleva consumido hoy ese tenant.
        /// </summary>
        Task<int> GetUsedTodayAsync(ITenantContext tenant, CancellationToken ct = default);
    }
}
