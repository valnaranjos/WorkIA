using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KommoAIAgent.Api.Middleware
{
    /// <summary>
    /// Valida la cabecera X-Admin-Key (o query ?adminKey=) contra Admin:ApiKey del configuration.
    /// Bloquea con 401 si no coincide. Si Admin:ApiKey no está configurado, 503.
    /// </summary>
    public sealed class AdminApiKeyFilter : IAsyncActionFilter
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AdminApiKeyFilter> _logger;

        public AdminApiKeyFilter(IConfiguration config, ILogger<AdminApiKeyFilter> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var expected = _config["Admin:ApiKey"];
            if (string.IsNullOrWhiteSpace(expected))
            {
                _logger.LogWarning("Admin:ApiKey no está configurado; se bloquea acceso admin.");
                context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
                return;
            }

            var req = context.HttpContext.Request;
            var provided =
                (req.Headers.TryGetValue("X-Admin-Key", out var hdr) ? hdr.FirstOrDefault() : null)
                ?? req.Query["adminKey"].FirstOrDefault();

            if (string.IsNullOrEmpty(provided) || !string.Equals(provided, expected, StringComparison.Ordinal))
            {
                context.Result = new UnauthorizedObjectResult(new { error = "invalid admin key" });
                return;
            }

            await next();
        }
    }
}
