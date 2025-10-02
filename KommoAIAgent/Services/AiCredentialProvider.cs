using KommoAIAgent.Domain.Tenancy;
using KommoAIAgent.Services.Interfaces;

namespace KommoAIAgent.Services
{
    /// <summary>
    /// Esta clase permite configurar la apiKey de la IA estándar (la toma de secrets) o por tenat, y el proveedor.
    /// </summary>
    public sealed class AiCredentialProvider : IAiCredentialProvider
    {
        private readonly IConfiguration _cfg;
        public AiCredentialProvider(IConfiguration cfg) => _cfg = cfg;

        public string GetApiKey(TenantConfig tc)
        {
            // Override por tenant (si se habilita después en BD)
            if (!string.IsNullOrWhiteSpace(tc.OpenAI?.ApiKey)) return tc.OpenAI!.ApiKey!;

            // Global desde secrets
            var k = _cfg["OPENAI:API_KEY"] ?? _cfg["OPENAI__API_KEY"];
            return k ?? string.Empty;
        }
    }
}