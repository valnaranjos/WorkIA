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
            TenantConfig cfg;
            if (string.IsNullOrEmpty(id.Value)) cfg = _cfgProvider.GetDefault();
            else if (!_cfgProvider.TryGet(id, out cfg)) cfg = _cfgProvider.GetDefault();

            // Establecemos el contexto del tenant para esta petición.
            _ctx.SetCurrent(TenantId.From(cfg.Slug), cfg);
            await _next(http);
        }
    }
}
