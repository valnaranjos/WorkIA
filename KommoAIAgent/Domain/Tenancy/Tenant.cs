namespace KommoAIAgent.Domain.Tenancy;

/// <summary>
/// Tenant/cliente de la aplicación con su configuración específica.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Identidad y ruteo
    public string Slug { get; set; } = null!;        // UK, minúsculas a-z0-9-, <=100
    public string DisplayName { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public string? SystemPrompt { get; set; }   // instrucciones base en texto y contexto factual.
    public string? BusinessRulesJson { get; set; } // reglas estructuradas en JSON crudo


    // Kommo
    public string KommoBaseUrl { get; set; } = null!; // https://{sub}.kommo.com
    public string? KommoAccessToken { get; set; }
    public long? KommoMensajeIaFieldId { get; set; }
    public string? KommoScopeId { get; set; }

    // IA
    public string IaProvider { get; set; } = "OpenAI";    // enum-string simple para no crear otra tabla, hacerlo dsps.
    public string IaModel { get; set; } = "gpt-4o-mini";
    public float? Temperature { get; set; }               // opcional
    public float? TopP { get; set; }                      // opcional
    public int? MaxTokens { get; set; }                   // opcional

    // Budget & guardrails
    public int MonthlyTokenBudget { get; set; } = 5_000_000;
    public int AlertThresholdPct { get; set; } = 80;

    // Runtime (defaults globales; override por tenant si quieres)
    public int MemoryTTLMinutes { get; set; } = 120;
    public int ImageCacheTTLMinutes { get; set; } = 15;
    public int DebounceMs { get; set; } = 800;
    public int RatePerMinute { get; set; } = 15;
    public int RatePer5Minutes { get; set; } = 60;


    // Auditoría
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
