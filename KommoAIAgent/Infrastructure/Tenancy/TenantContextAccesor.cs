using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Domain.Tenancy;

namespace KommoAIAgent.Infrastructure.Tenancy
{
    /// <summary>
    /// Accesor para el contexto del tenant actual, usando AsyncLocal para mantener el contexto por petición.
    /// </summary>
    public sealed class TenantContextAccessor : ITenantContextAccessor, ITenantContext
    {
        private static readonly AsyncLocal<TenantSnapshot?> _current = new();

        // Implementación EXPÍCITA
        ITenantContext ITenantContextAccessor.Current => this;

        public TenantId CurrentTenantId => _current.Value?.Id ?? TenantId.From(string.Empty);
        public TenantConfig Config => _current.Value?.Config
            ?? throw new InvalidOperationException("TenantConfig not set");
        public void SetCurrent(TenantId id, TenantConfig cfg)
            => _current.Value = new TenantSnapshot(id, cfg);

        // Estructura inmutable para almacenar el estado del tenant actual.
        private readonly record struct TenantSnapshot(TenantId Id, TenantConfig Config);
    }
}
