using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace KommoAIAgent.Controllers
{
    /// <summary>
    /// Endpoints simples para observar el contexto de tenant y salud del servicio, desde fuera.
    /// </summary>
    [ApiController]
    public partial class DiagnosticsController : ControllerBase
    {
        private readonly ITenantContext _tenant;
        private readonly ITokenBudget _budget;

        public DiagnosticsController(ITenantContext tenant, ITokenBudget budget)
        {
            _tenant = tenant;
            _budget = budget;
        }

        /// <summary>
        /// GET /t/{slug}/__whoami
        /// Muestra el tenant actualmente resuelto por el middleware, útil para probar rutas/headers.
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Healthcheck “lógico” por tenant (p. ej. confirmar que hay token/config).
        /// GET /t/{slug}/__health
        /// </summary>
        /// <returns></returns>
        [HttpGet("/t/{slug}/__health")]
        public IActionResult TenantHealth()
        {
            var ok = !string.IsNullOrWhiteSpace(_tenant.Config.Kommo.AccessToken)
                     && !string.IsNullOrWhiteSpace(_tenant.Config.OpenAI.ApiKey);

            return ok ? Ok(new { status = "OK", tenant = _tenant.CurrentTenantId.Value })
                      : StatusCode(503, new { status = "UNHEALTHY", tenant = _tenant.CurrentTenantId.Value });
        }

       
        /// <summary>
        /// Healthcheck básico de proceso, sin tenants.
        /// GET /__health
        /// </summary>
        /// <returns></returns>
        [HttpGet("/__health")]
        public IActionResult Health() => Ok(new { status = "OK" });


        /// <summary>
        /// GET /t/{slug}/__budget
        /// Devuelve: periodo, límite, usado, restante, factor de estimación y msg de exceso
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
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

    /// <summary>
    /// Diagnosticos o health dentro de la API.
    /// </summary>
    public partial class DiagnosticsController : ControllerBase
    {
        /// <summary>
        /// Liveness: proceso vivo.
        /// </summary>
        /// <returns></returns>
        [HttpGet("~/health/live")]
        [AllowAnonymous]
        public IActionResult Live() => Ok(new { ok = true });

        /// <summary>
        /// Readness:  DB + pgvector + al menos un tenant activo
        /// </summary>
        /// <param name="ds"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("~/health/ready")]
        [AllowAnonymous]
        public async Task<IActionResult> Ready([FromServices] NpgsqlDataSource ds, CancellationToken ct)
        {
            try
            {
                await using var conn = await ds.OpenConnectionAsync(ct);

                // DB ok
                await using (var ping = new NpgsqlCommand("SELECT 1", conn))
                    await ping.ExecuteScalarAsync(ct);

                //pgvector ok
                const string sqlExt = "SELECT 1 FROM pg_extension WHERE extname='vector' LIMIT 1";
                await using (var cmdExt = new NpgsqlCommand(sqlExt, conn))
                    if (await cmdExt.ExecuteScalarAsync(ct) is null)
                        return StatusCode(503, new { ok = false, reason = "pgvector missing" });

                //al menos un tenant activo
                const string sqlTenant = "SELECT 1 FROM tenants WHERE \"IsActive\" = true LIMIT 1";
                await using (var cmdTenant = new NpgsqlCommand(sqlTenant, conn))
                    if (await cmdTenant.ExecuteScalarAsync(ct) is null)
                        return StatusCode(503, new { ok = false, reason = "no active tenants" });

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return StatusCode(503, new { ok = false, reason = ex.GetType().Name });
            }
        }
    }
}
