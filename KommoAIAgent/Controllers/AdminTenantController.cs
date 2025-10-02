using KommoAIAgent.Api.Contracts;
using KommoAIAgent.Application.Common;
using KommoAIAgent.Domain.Tenancy;
using KommoAIAgent.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KommoAIAgent.Controllers;

/// <summary>
/// Controlador para la administración de tenants
/// </summary>
[ApiController]
[Route("admin/[controller]")]
public class TenantsController : ControllerBase
{
    private readonly AppDbContext _db;
    private static readonly Regex SlugRx = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    public TenantsController(AppDbContext db) => _db = db;

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
    /// Obtiene un tenant por su subdominio, devuelve 404 si no existe,
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpGet("by-slug/{slug}")]
    public async Task<ActionResult<TenantResponse>> GetBySlug(string slug)
    {
        if (!SlugRx.IsMatch(slug)) return BadRequest("Slug inválido (usa a-z, 0-9 y guiones).");

        var t = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug);
        return t is null ? NotFound() : ToResponse(t);
    }

    /// <summary>
    /// Crea un nuevo tenant con los datos provistos.
    /// </summary>
    /// <param name="req"></param>
    /// <returns></returns>
    [HttpPost]
    public async Task<ActionResult<TenantResponse>> Create(TenantRequest req)
    {
        var slug = string.IsNullOrWhiteSpace(req.Slug)
            ? SubdomainParser.DeriveSlug(req.KommoBaseUrl)
            : req.Slug.Trim().ToLowerInvariant();

        if (await _db.Tenants.AnyAsync(x => x.Slug == slug))
            return Conflict($"Subdominio '{slug}' ya existe.");

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            DisplayName = req.DisplayName.Trim(),
            KommoBaseUrl = req.KommoBaseUrl.Trim(),
            IaProvider = req.IaProvider ?? "OpenAI",
            IaModel = req.IaModel ?? "gpt-4o-mini",
            Temperature = req.Temperature,
            TopP = req.TopP,
            MaxTokens = req.MaxTokens,
            MonthlyTokenBudget = req.MonthlyTokenBudget ?? 5_000_000,
            AlertThresholdPct = req.AlertThresholdPct ?? 75,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetBySlug), new { slug = tenant.Slug }, ToResponse(tenant));
    }

    /// <summary>
    /// Permite actualizar los datos de un tenant existente a apartir de su subdominio.
    /// </summary>
    /// <param name="slug">Subdominio único por tenant</param>
    /// <param name="req"></param>
    /// <returns></returns>
    [HttpPut("by-slug/{slug}")]
    public async Task<ActionResult<TenantResponse>> UpdateBySlug(string slug, TenantRequest req)
    {
        if (!SlugRx.IsMatch(slug)) return BadRequest("Slug inválido (usa a-z, 0-9 y guiones).");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Slug == slug);
        if (tenant is null) return NotFound();

        tenant.DisplayName = req.DisplayName ?? tenant.DisplayName;
        tenant.KommoBaseUrl = req.KommoBaseUrl ?? tenant.KommoBaseUrl;
        tenant.IaProvider = req.IaProvider ?? tenant.IaProvider;
        tenant.IaModel = req.IaModel ?? tenant.IaModel;
        tenant.Temperature = req.Temperature ?? tenant.Temperature;
        tenant.TopP = req.TopP ?? tenant.TopP;
        tenant.MaxTokens = req.MaxTokens ?? tenant.MaxTokens;
        tenant.MonthlyTokenBudget = req.MonthlyTokenBudget ?? tenant.MonthlyTokenBudget;
        tenant.AlertThresholdPct = req.AlertThresholdPct ?? tenant.AlertThresholdPct;
        tenant.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return ToResponse(tenant);
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
        tenant.UpdatedAt = DateTime.UtcNow;
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
        t.UpdatedAt = DateTime.UtcNow;
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
        t.UpdatedAt = DateTime.UtcNow;
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
    private static TenantResponse ToResponse(Tenant t) =>
        new(t.Id, t.Slug, t.DisplayName, t.IsActive, t.KommoBaseUrl, t.IaProvider, t.IaModel,
            t.MonthlyTokenBudget, t.AlertThresholdPct, t.CreatedAt, t.UpdatedAt);
}
