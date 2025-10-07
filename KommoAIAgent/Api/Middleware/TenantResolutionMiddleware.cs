using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Domain.Tenancy;
using System.IO;

namespace KommoAIAgent.Api.Middleware
{
    /// <summary>
    /// Middleware para resolver y establecer el contexto del tenant en cada petición.
    /// </summary>
    public sealed class TenantResolutionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITenantResolver _resolver;
        private readonly ITenantConfigProvider _cfgProvider;
        private readonly ITenantContextAccessor _ctx;
        private readonly ILogger<TenantResolutionMiddleware> _logger;



        public TenantResolutionMiddleware(
            RequestDelegate next, 
            ITenantResolver resolver, 
            ITenantConfigProvider cfgProvider, 
            ITenantContextAccessor ctx, 
            ILogger<TenantResolutionMiddleware> logger)
        {
            _next = next; 
            _resolver = resolver; 
            _cfgProvider = cfgProvider; 
            _ctx = ctx;
            _logger = logger;
        }

        /// <summary>
        /// Intercepta la petición HTTP, resuelve el tenant y establece el contexto.
        /// </summary>
        /// <param name="http"></param>
        /// <returns></returns>
        public async Task InvokeAsync(HttpContext http)
        {
            //Para poder usar Swagger en dev sin tenant
            //Usar los endpoints /swagger, /health o que incluya /admin sin resolver tenant
            if (http.Request.Path.StartsWithSegments("/swagger") ||
            http.Request.Path.StartsWithSegments("/health") || 
            http.Request.Path.StartsWithSegments("/admin"))
            {
                await _next(http);
                return;
            }

            //Llama al resolvedor
            var id = _resolver.Resolve(http);
            _logger.LogInformation("[TenantMiddleware] resolved='{Slug}' path={Path}", id.Value, http.Request.Path);

            //Si no se ha podido resolver, 400
            if (string.IsNullOrWhiteSpace(id.Value))
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                await http.Response.WriteAsJsonAsync(new { error = "Tenant requerido. Usa header X-Tenant-Slug o ?tenant=" });
                return;
            }

            // Si no existe el tenant, 404
            if (!_cfgProvider.TryGet(id, out var cfg))
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                await http.Response.WriteAsJsonAsync(new { error = "Tenant desconocido" });
                return;
            }

            _ctx.SetCurrent(TenantId.From(cfg.Slug), cfg);
            await _next(http);
        }
    }
}
