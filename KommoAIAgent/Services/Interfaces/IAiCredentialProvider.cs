using KommoAIAgent.Domain.Tenancy;

namespace KommoAIAgent.Services.Interfaces
{
    /// <summary>
    /// Contrato para configurar la apiKey del proveedor de IA.
    /// </summary>
    public interface IAiCredentialProvider
    {
        string GetApiKey(TenantConfig cfg);
    }
}
