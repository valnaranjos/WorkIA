using KommoAIAgent.Domain.Tenancy;

namespace KommoAIAgent.Application.Interfaces
{
    /// <summary>
    /// Contexto del tenant actual, disponible vía DI.
    /// </summary>
    public interface ITenantContext
    {
        /// <summary>
        /// Identificador único del tenant actual.
        /// Nunca debe ser nulo, se setea en el middleware de resolución.
        /// </summary>
        TenantId CurrentTenantId { get; }

        /// <summary>
        /// Configuración completa del tenant (Kommo, OpenAI, etc.).
        /// </summary>
        TenantConfig Config { get; }
    }


    /// <summary>
    /// Accesor mutable para el contexto del tenant.
    /// Permite al middleware setear el tenant actual.
    /// </summary>
    public interface ITenantContextAccessor
    {
        /// <summary>
        /// Devuelve el contexto actual del tenant ya resuelto.
        /// </summary>
        ITenantContext Current { get; }

        /// <summary>
        /// Setea el tenant actual de forma atómica.
        /// Llamado por el TenantResolutionMiddleware.
        /// </summary>
        void SetCurrent(TenantId id, TenantConfig config);
    }


    /// <summary>
    /// Resolver de tenant a partir de la petición HTTP.
    /// Origen en orden: 
    /// 1) ruta /t/{slug}, 
    /// 2) header X-Tenant-Slug, 
    /// 3) payload Kommo (account_id / scope_id),
    /// 4) subdominio mapeado,
    /// 5) fallback al default.
    /// </summary>
    public interface ITenantResolver
    {
        TenantId Resolve(HttpContext http);
    }

    /// <summary>
    /// Proveedor de configuraciones de tenants.
    /// Normalmente backed por appsettings.json + secrets.
    /// </summary>
    public interface ITenantConfigProvider
    {
        /// <summary>
        /// Obtiene configuración de un tenant por Id. 
        /// Lanza excepción si no existe.
        /// </summary>
        TenantConfig Get(TenantId id);

        /// <summary>
        /// Obtiene la configuración por defecto.
        /// </summary>
        TenantConfig GetDefault();

        /// <summary>
        /// Intenta obtener configuración sin lanzar excepción.
        /// </summary>
        bool TryGet(TenantId id, out TenantConfig config);
    }
}
