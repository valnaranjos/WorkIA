using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace KommoAIAgent.Controllers
{
    /// <summary>
    /// Endpoints simples para observar el contexto de tenant y salud del servicio.
    /// </summary>
    [ApiController]
    public sealed class DiagnosticsController : ControllerBase
    {
        private readonly ITenantContext _tenant;
        private readonly ITokenBudget _budget;

        public DiagnosticsController(ITenantContext tenant, ITokenBudget budget)
        {
            _tenant = tenant;
            _budget = budget;
        }


        // GET /t/{slug}/__whoami
        // Muestra el tenant actualmente resuelto por el middleware, útil para probar rutas/headers.
        [HttpGet("/t/{slug}/__whoami")]
        public IActionResult WhoAmI()
        {
            return Ok(new
            {
                Tenant = _tenant.CurrentTenantId.Value,
                KommoBase = _tenant.Config.Kommo.BaseUrl,
                CF_MensajeIA = _tenant.Config.Kommo.FieldIds.MensajeIA,
                OpenAIModel = _tenant.Config.OpenAI.Model
            });
        }

        // GET /t/{slug}/__health
        // Healthcheck “lógico” por tenant (p. ej. confirmar que hay token/config).
        [HttpGet("/t/{slug}/__health")]
        public IActionResult TenantHealth()
        {
            var ok = !string.IsNullOrWhiteSpace(_tenant.Config.Kommo.AccessToken)
                     && !string.IsNullOrWhiteSpace(_tenant.Config.OpenAI.ApiKey);

            return ok ? Ok(new { status = "OK", tenant = _tenant.CurrentTenantId.Value })
                      : StatusCode(503, new { status = "UNHEALTHY", tenant = _tenant.CurrentTenantId.Value });
        }

        // GET /__health
        // Healthcheck básico de proceso (sin tenant).
        [HttpGet("/__health")]
        public IActionResult Health() => Ok(new { status = "OK" });


        // GET /t/{slug}/__budget
        // Devuelve: periodo, límite, usado, restante, factor de estimación y msg de exceso
        [HttpGet("/t/{slug}/__budget")]
        public async Task<IActionResult> GetBudgetAsync(CancellationToken ct)
        {
            var cfg = _tenant.Config.Budgets;
            var period = cfg?.Period ?? "Monthly";
            var limit = cfg?.TokenLimit ?? 0; // 0 => sin límite
            var used = await _budget.GetUsedTodayAsync(_tenant, ct);
            var remaining = limit > 0 ? Math.Max(0, limit - used) : int.MaxValue;

            return Ok(new
            {
                Tenant = _tenant.CurrentTenantId.Value,
                Period = period,
                TokenLimit = limit,
                Used = used,
                Remaining = limit > 0 ? remaining : (int?)null, // null si no hay límite
                EstimationFactor = cfg?.EstimationFactor ?? 0.85,
                ExceededMessage = cfg?.ExceededMessage
            });
        }
    }
}
