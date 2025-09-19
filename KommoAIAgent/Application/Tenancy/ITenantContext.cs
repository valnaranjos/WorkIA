using KommoAIAgent.Domain.Tenancy;

namespace KommoAIAgent.Application.Tenancy
{
    /// <summary>
    /// Contexto del tenant actual, disponible vía DI.
    /// </summary>
    public interface ITenantContext
    {
        TenantId CurrentTenantId { get; }
        TenantConfig Config { get; }
    }


    /// <summary>
    /// Accesor para el contexto del tenant actual.
    /// </summary>
    public interface ITenantContextAccessor
    {
        ITenantContext Current { get; }
        void SetCurrent(TenantId id, TenantConfig cfg);
    }


    /// <summary>
    /// Resuelve el tenant actual desde la petición HTTP.
    /// </summary>
    public interface ITenantResolver
    {
        /// Intenta resolver desde (1) ruta /t/{slug}, (2) header X-Tenant-Slug, (3) Kommo: account_id/scope_id, (4) subdominio mapeado.
        TenantId Resolve(HttpContext http);
    }

    /// <summary>
    /// Provee la configuración de los tenants.
    /// </summary>
    public interface ITenantConfigProvider
    {
        TenantConfig Get(TenantId id);
        TenantConfig GetDefault();
        bool TryGet(TenantId id, out TenantConfig cfg);
    }
}
