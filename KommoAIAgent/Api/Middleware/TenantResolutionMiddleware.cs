using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Domain.Tenancy;

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


        public TenantResolutionMiddleware(RequestDelegate next, ITenantResolver resolver, ITenantConfigProvider cfgProvider, ITenantContextAccessor ctx)
        { _next = next; _resolver = resolver; _cfgProvider = cfgProvider; _ctx = ctx; }

        /// <summary>
        /// Intercepta la petición HTTP, resuelve el tenant y establece el contexto.
        /// </summary>
        /// <param name="http"></param>
        /// <returns></returns>
        public async Task InvokeAsync(HttpContext http)
        {
            var id = _resolver.Resolve(http);

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
