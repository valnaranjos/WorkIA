using KommoAIAgent.Api.Contracts;
using KommoAIAgent.Api.Security;
using KommoAIAgent.Application.Common;
using KommoAIAgent.Domain.Tenancy;
using KommoAIAgent.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KommoAIAgent.Api.Controllers;

/// <summary>
/// Controlador para la administración de tenants
/// Agregar, editar y eliminar (soft-delete) tenants.
/// Agregar o actualizar prompts y reglas de negocio.
/// </summary>
[ApiController]
[Route("admin/[controller]")]
[AdminApiKey]
public class AdminTenantsController : ControllerBase
{
    private readonly AppDbContext _db;
    private static readonly Regex SlugRx = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    public AdminTenantsController(AppDbContext db) => _db = db;

    /// <summary>
    /// Obtiene la lista completa de tenants.
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TenantResponse>>> GetAll()
    {
        var tenants = await _db.Tenants.AsNoTracking().ToListAsync();
        return tenants.Select(ToResponse).ToList();
    }

    /// <summary>
    /// Obtiene un tenant por su subdominio, devuelve 404 si no existe
    /// GET /admin/admintenants/by-slug/{slug}
    /// </summary>
    /// <param name="slug"></param>
    /// <returns></returns>
    [HttpGet("by-slug/{slug}")]
    public async Task<ActionResult<TenantDetailResponse>> GetBySlug(string slug)
    {
        if (!SlugRx.IsMatch(slug)) return BadRequest("Slug inválido (usa a-z, 0-9 y guiones).");

        var t = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug);
        return t is null ? NotFound() : ToDetailResponse(t);
    }

    /// <summary>
    /// Crea un nuevo tenant con los datos provistos.
    /// </summary>
    /// <param name="req"></param>
    /// <returns></returns>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<TenantResponse>> Create([FromBody] TenantCreateRequest req)
    {
        // Slug: provisto o derivado del subdominio Kommo
        var slug = string.IsNullOrWhiteSpace(req.Slug)
            ? SubdomainParser.DeriveSlug(req.KommoBaseUrl)
            : req.Slug.Trim().ToLowerInvariant();

        if (!SlugRx.IsMatch(slug))
            return BadRequest("Slug inválido (usa a-z, 0-9 y guiones).");

        if (await _db.Tenants.AnyAsync(x => x.Slug == slug))
            return Conflict($"Subdominio '{slug}' ya existe.");

        // Validaciones mínimas adicionales (además de DataAnnotations)
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return BadRequest("DisplayName es requerido.");
        if (req.KommoMensajeIaFieldId < 0)
            return BadRequest("KommoMensajeIaFieldId es requerido y debe ser > 0.");

        // Reglas de negocio a string JSON (si vinieron)
        string rulesRaw = "{}";
        if (req.BusinessRules is JsonElement el)
            rulesRaw = el.GetRawText()?.Trim() ?? "{}";

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        var t = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            DisplayName = req.DisplayName.Trim(),
            IsActive = req.IsActive,

            // --- Kommo ---
            KommoBaseUrl = req.KommoBaseUrl.Trim(),
            KommoAccessToken = req.KommoAccessToken ?? string.Empty,
            KommoScopeId = (req.KommoScopeId ?? string.Empty).Trim(),          // <- era int→string
            KommoMensajeIaFieldId = req.KommoMensajeIaFieldId,                  // deja el tipo que tengas (int/long)

            // --- IA ---
            IaProvider = req.IaProvider,
            IaModel = req.IaModel,
            Temperature = (float)req.Temperature,   // <- double→float
            TopP = (float)req.TopP,          // <- double→float
            MaxTokens = req.MaxTokens,
            SystemPrompt = (req.SystemPrompt ?? string.Empty).Trim(),
            BusinessRulesJson = rulesRaw,           // tu string RAW del JSON

            // --- Operación / Límites ---
            DebounceMs = req.DebounceMs,
            RatePerMinute = req.RatePerMinute,
            RatePer5Minutes = req.RatePer5Minutes,
            MemoryTTLMinutes = req.MemoryTTLMinutes,
            ImageCacheTTLMinutes = req.ImageCacheTTLMinutes,
            MonthlyTokenBudget = req.MonthlyTokenBudget,
            AlertThresholdPct = req.AlertThresholdPct,  // <- double→float (si guardas 0..1)
                                                        // Si guardas 0..100, usa: (float)(req.AlertThresholdPct / 100.0)

            // --- Auditoría ---
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Tenants.Add(t);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetBySlug), new { slug = t.Slug }, ToResponse(t));
    }

    /// <summary>
    /// Permite actualizar los datos de un tenant existente a apartir de su subdominio.
    /// </summary>
    /// <param name="slug">Subdominio único por tenant</param>
    /// <param name="req"></param>
    /// <returns></returns>
    [HttpPut("by-slug/{slug}")]
    [Consumes("application/json")]
    public async Task<ActionResult<TenantResponse>> UpdateBySlug(string slug, [FromBody] TenantUpdateRequest req)
    {
        if (!SlugRx.IsMatch(slug)) return BadRequest("Slug inválido (usa a-z, 0-9 y guiones).");

        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Slug == slug);
        if (t is null) return NotFound();

        // Identidad
        if (!string.IsNullOrWhiteSpace(req.DisplayName)) t.DisplayName = req.DisplayName.Trim();
        if (req.IsActive.HasValue) t.IsActive = req.IsActive.Value;

        // Kommo
        if (!string.IsNullOrWhiteSpace(req.KommoBaseUrl)) t.KommoBaseUrl = req.KommoBaseUrl.Trim();
        if (!string.IsNullOrWhiteSpace(req.KommoAccessToken)) t.KommoAccessToken = req.KommoAccessToken;
        if (!string.IsNullOrWhiteSpace(req.KommoScopeId)) t.KommoScopeId = req.KommoScopeId.Trim();
        if (req.KommoMensajeIaFieldId.HasValue && req.KommoMensajeIaFieldId.Value > 0)
            t.KommoMensajeIaFieldId = req.KommoMensajeIaFieldId.Value;

        // IA
        if (!string.IsNullOrWhiteSpace(req.IaProvider)) t.IaProvider = req.IaProvider;
        if (!string.IsNullOrWhiteSpace(req.IaModel)) t.IaModel = req.IaModel;
        if (req.Temperature.HasValue) t.Temperature = req.Temperature.Value;
        if (req.TopP.HasValue) t.TopP = req.TopP.Value;
        if (req.MaxTokens.HasValue) t.MaxTokens = req.MaxTokens.Value;
        if (req.SystemPrompt is not null) t.SystemPrompt = (req.SystemPrompt ?? string.Empty).Trim();


        if (req.IaApiKey is not null) t.IaApiKey = req.IaApiKey;
        if (req.FallbackProvider is not null) t.FallbackProvider = req.FallbackProvider;
        if (req.FallbackApiKey is not null) t.FallbackApiKey = req.FallbackApiKey;
        if (req.EnableImageOCR.HasValue) t.EnableImageOCR = req.EnableImageOCR.Value;
        if (req.EnableAutoConnectorInvocation.HasValue) t.EnableAutoConnectorInvocation = req.EnableAutoConnectorInvocation.Value;


        if (req.BusinessRules is JsonElement el)
            t.BusinessRulesJson = el.GetRawText()?.Trim() ?? t.BusinessRulesJson;

        // Operación / límites
        if (req.DebounceMs.HasValue) t.DebounceMs = req.DebounceMs.Value;
        if (req.RatePerMinute.HasValue) t.RatePerMinute = req.RatePerMinute.Value;
        if (req.RatePer5Minutes.HasValue) t.RatePer5Minutes = req.RatePer5Minutes.Value;
        if (req.MemoryTTLMinutes.HasValue) t.MemoryTTLMinutes = req.MemoryTTLMinutes.Value;
        if (req.ImageCacheTTLMinutes.HasValue) t.ImageCacheTTLMinutes = req.ImageCacheTTLMinutes.Value;

        // Presupuesto
        if (req.MonthlyTokenBudget.HasValue) t.MonthlyTokenBudget = req.MonthlyTokenBudget.Value;
        if (req.AlertThresholdPct.HasValue) t.AlertThresholdPct = req.AlertThresholdPct.Value;

        t.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        await _db.SaveChangesAsync();
        return ToResponse(t);
    }


    /// <summary>
    /// Permite desactivar un tenant (soft delete).
    /// </summary>
    /// <param name="slug"></param>
    /// <returns></returns>
    [HttpDelete("by-slug/{slug}")]
    public async Task<IActionResult> DeleteBySlug(string slug)
    {
        if (!SlugRx.IsMatch(slug)) return BadRequest("Slug inválido (usa a-z, 0-9 y guiones).");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Slug == slug);
        if (tenant is null) return NotFound();

        tenant.IsActive = false;
        tenant.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await _db.SaveChangesAsync();
        return NoContent();
    }


    /// <summary>
    /// Permite actualizar o agg el prompt del sistema de un tenant.
    /// </summary>
    /// <param name="slug">Id para tenant</param>
    /// <param name="req">Formato según el UpdatePromptRequest</param>
    /// <returns></returns>
    [HttpPut("{slug}/prompt")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPrompt([FromRoute] string slug, [FromBody] UpdatePromptRequest req)
    {
        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Slug == slug && x.IsActive);
        if (t is null) return NotFound(new { error = "Tenant no encontrado o inactivo" });

        t.SystemPrompt = (req.SystemPrompt ?? string.Empty).Trim();
        t.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Permite obtener el prompt del sistema de un tenant.
    /// </summary>
    /// <param name="slug"></param>
    /// <returns></returns>
    [HttpGet("{slug}/prompt")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPrompt([FromRoute] string slug)
    {
        var t = await _db.Tenants
            .Where(x => x.Slug == slug && x.IsActive)
            .Select(x => new { x.Slug, x.DisplayName, x.SystemPrompt })
            .FirstOrDefaultAsync();
        if (t is null) return NotFound(new { error = "Tenant no encontrado o inactivo" });
        return Ok(t);
    }


    /// <summary>
    /// Permite actualizar o agg las reglas de negocio (JSON) de un tenant.
    /// </summary>
    /// <param name="slug"></param>
    /// <param name="req"></param>
    /// <returns></returns>
    [HttpPut("{slug}/rules")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetRules([FromRoute] string slug, [FromBody] UpdateRulesRequest req)
    {
        // Validar que sea JSON “de verdad”
        string raw;
        try { raw = req.Rules.GetRawText(); }
        catch { return BadRequest(new { error = "Rules no es un JSON válido" }); }

        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Slug == slug && x.IsActive);
        if (t is null) return NotFound(new { error = "Tenant no encontrado o inactivo" });

        t.BusinessRulesJson = raw.Trim();
        t.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await _db.SaveChangesAsync();
        return NoContent();
    }


    /// <summary>
    /// Permite obtener las reglas de negocio (JSON) de un tenant.
    /// </summary>
    /// <param name="slug"></param>
    /// <returns></returns>
    [HttpGet("{slug}/rules")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRules([FromRoute] string slug)
    {
        var t = await _db.Tenants
            .Where(x => x.Slug == slug && x.IsActive)
            .Select(x => new { x.Slug, x.DisplayName, Rules = x.BusinessRulesJson })
            .FirstOrDefaultAsync();
        if (t is null) return NotFound(new { error = "Tenant no encontrado o inactivo" });
        return Ok(t);
    }

    /// <summary>
    /// Lee un Tenant y lo convierte a DTO de respuesta.
    /// </summary>
    /// <param name="t"></param>
    /// <returns></returns>
    private static TenantResponse ToResponse(Tenant t)
    {
        // Normaliza alerta: si en DB está en 0..100, conviértelo a 0..1; si ya está en 0..1 lo deja igual.
        double alertFrac = t.AlertThresholdPct > 1 ? t.AlertThresholdPct / 100.0 : t.AlertThresholdPct;
        var updated = t.UpdatedAt ?? t.CreatedAt;
        return new TenantResponse(
            t.Id,
            t.Slug,
            t.DisplayName,
            t.IsActive,
            t.KommoBaseUrl,
            t.IaProvider,
            t.IaModel,
            t.MonthlyTokenBudget,
            alertFrac,
            t.CreatedAt,
            updated,
            t.SystemPrompt ?? string.Empty,
            t.BusinessRulesJson // lo devolvemos RAW; el front decide si parsea
        );
    }



    /// <summary>
    /// Forma detallada de un tenant.
    /// </summary>
    /// <param name="t"></param>
    /// <returns></returns>
    private static TenantDetailResponse ToDetailResponse(Tenant t)
    {
        // Si guardas AlertThreshold como porcentaje (80), lo mapeas igual;
        // si internamente es fracción (0.8), multiplícalo por 100.
        var updated = t.UpdatedAt ?? t.CreatedAt;

        return new TenantDetailResponse(
            t.Id,
            t.Slug,
            t.DisplayName,
            t.IsActive,
            t.KommoBaseUrl,

            // Kommo
            t.KommoAccessToken,
            t.KommoMensajeIaFieldId,
            t.KommoScopeId,

            // IA
            t.IaProvider,
            t.IaModel,
            t.Temperature,
            t.TopP,
            t.MaxTokens,

            //IA Provider
            t.IaApiKey,
            t.FallbackProvider,
            t.FallbackApiKey,
            t.EnableImageOCR,
            t.EnableAutoConnectorInvocation,


            // Presupuestos / alertas
            t.MonthlyTokenBudget,
            t.AlertThresholdPct,
            t.CreatedAt,
            updated,

            // Texto / reglas
            t.SystemPrompt ?? string.Empty,
            t.BusinessRulesJson
        );
    }
}
