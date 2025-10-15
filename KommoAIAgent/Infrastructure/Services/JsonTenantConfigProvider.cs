using KommoAIAgent.Application.Interfaces;
using KommoAIAgent.Domain.Tenancy;
using Microsoft.Extensions.Options;

namespace KommoAIAgent.Infrastructure.Tenancy
{
    /// <summary>
    /// Carga la configuración de múltiples tenants desde un archivo JSON. Útil para desarrollo y pruebas.
    /// </summary>
    public sealed class JsonTenantConfigProvider : ITenantConfigProvider
    {
        private readonly Dictionary<string, TenantConfig> _map;
        private readonly string _defaultSlug;


        public JsonTenantConfigProvider(IOptions<MultiTenancyOptions> opt)
        {
            _defaultSlug = opt.Value.DefaultTenant?.Trim().ToLowerInvariant() ?? string.Empty;
            _map = (opt.Value.Tenants ?? new()).ToDictionary(t => t.Slug.Trim().ToLowerInvariant(), t => t);
        }


        public TenantConfig Get(TenantId id) => _map.TryGetValue(id.Value, out var cfg)
        ? cfg
        : throw new KeyNotFoundException($"Tenant '{id.Value}' not found");


        public bool TryGet(TenantId id, out TenantConfig cfg) => _map.TryGetValue(id.Value, out cfg!);


        public TenantConfig GetDefault()
        => string.IsNullOrEmpty(_defaultSlug) ? _map.Values.First() : _map[_defaultSlug];
    }


    /// <summary>
    /// MultiTenancyOptions representa la configuración de múltiples tenants.
    /// </summary>
    public sealed class MultiTenancyOptions
    {
        public string? DefaultTenant { get; init; }
        public List<TenantConfig> Tenants { get; init; } = new();
    }
}
