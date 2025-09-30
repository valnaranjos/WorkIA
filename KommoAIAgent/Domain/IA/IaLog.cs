using KommoAIAgent.Domain.Tenancy;
namespace KommoAIAgent.Domain.Ia;

public class IaLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TenantId TenantId { get; set; }

    public string Model { get; set; } = null!;
    public string Input { get; set; } = null!;
    public string Output { get; set; } = null!;
    public string? Meta { get; set; } // JSON con temperatura, etc.

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
