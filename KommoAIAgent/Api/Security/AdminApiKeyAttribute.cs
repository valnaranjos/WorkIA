using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KommoAIAgent.Api.Security;

/// <summary>
/// Protege acciones admin con una API key en cabecera X-Admin-Key (o query adminKey).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminApiKeyAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var cfg = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expected = cfg["Admin:ApiKey"];

        if (string.IsNullOrWhiteSpace(expected))
        {
            // Si no hay clave configurada, bloqueamos en ambientes no-dev
            var env = ctx.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>();
            if (!env.IsDevelopment())
            {
                ctx.Result = new UnauthorizedObjectResult(new { error = "Admin API key not configured" });
                return;
            }
        }

        var req = ctx.HttpContext.Request;
        req.Headers.TryGetValue("X-Admin-Key", out var keyFromHeader);
        var key = (string?)keyFromHeader!;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = req.Query["adminKey"];
        }

        if (!string.IsNullOrWhiteSpace(expected) && !string.Equals(key, expected, StringComparison.Ordinal))
        {
            ctx.Result = new UnauthorizedObjectResult(new { error = "Invalid admin key" });
            return;
        }

        await next();
    }
}
