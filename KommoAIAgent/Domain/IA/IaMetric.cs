using KommoAIAgent.Domain.Tenancy;

namespace KommoAIAgent.Domain.Ia;

/// <summary>
/// Clase para registrar métricas de uso de IA por tenant.
/// </summary>
public class IaMetric
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TenantId TenantId { get; set; }

    public string Model { get; set; } = null!;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }

    // Tokens totales (suma de prompt + completion)
    public int TotalTokens => PromptTokens + CompletionTokens;
    public DateTime At { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
