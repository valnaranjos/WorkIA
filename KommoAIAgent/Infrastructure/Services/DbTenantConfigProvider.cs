using KommoAIAgent.Application.Interfaces;
using KommoAIAgent.Domain.Tenancy;        // TenantId, Tenant (entity)
using KommoAIAgent.Infrastructure.Persistence; // AppDbContext
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KommoAIAgent.Infrastructure.Services
{
    /// <summary>
    /// Provider de configuraciones de tenant leyendo directamente desde la BD (tabla tenants).
    /// </summary>
    public sealed class DbTenantConfigProvider : ITenantConfigProvider
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public DbTenantConfigProvider(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public TenantConfig Get(TenantId id)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var t = db.Tenants.AsNoTracking()
                  .FirstOrDefault(x => x.Slug == id.Value && x.IsActive)
                 ?? throw new KeyNotFoundException($"Tenant '{id.Value}' no se encontró");

            return Map(t);
        }

        public bool TryGet(TenantId id, out TenantConfig config)
        {

            //Si el slug es nulo o vacío, no tiene sentido buscar en la BD.
            if (string.IsNullOrWhiteSpace(id.Value))
            {
                config = default!;
                return false;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var t = db.Tenants.AsNoTracking()
                     .FirstOrDefault(x => x.Slug == id.Value && x.IsActive);

            if (t is null)
            {
                config = default!;
                return false;
            }

            config = Map(t);
            return true;
        }

        public TenantConfig GetDefault()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var t = db.Tenants.AsNoTracking().FirstOrDefault(x => x.IsActive)
                    ?? throw new InvalidOperationException("No hay tenants activos.");

            return Map(t);
        }

        private static TenantConfig Map(Tenant t)
        {
            // Mapea la entidad Tenant a TenantConfig.
            return new TenantConfig
            {
                Slug = t.Slug,
                DisplayName = t.DisplayName,

                Kommo = new KommoConfig
                {
                    BaseUrl = t.KommoBaseUrl,
                    AccessToken = t.KommoAccessToken ?? string.Empty,
                    FieldIds = new FieldIds { MensajeIA = t.KommoMensajeIaFieldId ?? 0 },
                    Chat = new ChatConfig { ScopeId = t.KommoScopeId }
                },

                //Configuración de IA (usa variables de entorno si no está en la BD)
                OpenAI = new OpenAIConfig
                {
                    ApiKey = "", //Se resuelve por AiCredentialsProvider
                    Model = string.IsNullOrWhiteSpace(t.IaModel) ? "gpt-4o-mini" : t.IaModel,
                    VisionModel = "gpt-4o",
                    Temperature = t.Temperature,
                    TopP = t.TopP,
                    MaxTokens = t.MaxTokens,
                    SystemPrompt = t.SystemPrompt,
                },

                //Prompting para IA y reglas de negocios x tenant.             
                BusinessRules = t.BusinessRulesJson is null ? null : JsonDocument.Parse(t.BusinessRulesJson),

                // --- Debounce (config estándar) ---
                Debounce = new DebounceConfig
                {
                    WindowMs = t.DebounceMs > 0 ? t.DebounceMs : 800,
                    MaxBurstMs = 1800,
                    ForceFlush = new ForceFlushConfig { MaxFragments = 5, MaxChars = 1200 }
                },

                // --- Presupuesto tokens ---
                Budgets = new BudgetConfig
                {
                    Period = "Monthly",
                    //Limite por defecto 2M tokens si no está configurado en la BD.
                    TokenLimit = t.MonthlyTokenBudget > 0 ? t.MonthlyTokenBudget : 2_000_000,
                    //Factor de estimación para alertas (default estándar)
                    EstimationFactor = 0.85,
                    //Mensaje de alerta (si es nulo, no avisa)
                    ExceededMessage = "Has excedido el 85% del presupuesto mensual.",
                    //Ráfaga máxima por minuto (default estándar)
                    BurstPerMinute = t.RatePerMinute > 0 ? t.RatePerMinute : 12,
                    //Umbral de alerta (default estándar)
                    AlertThresholdPct = t.AlertThresholdPct > 0 ? t.AlertThresholdPct : 75,
                    //Ráfaga máxima por 5 minutos (default estándar)
                    BurstPer5Minutes = t.RatePer5Minutes > 0 ? t.RatePer5Minutes : 60
                },

                // --- Memoria (default estándar) ---
                Memory = new MemoryConfig
                {
                    //Memoria en minutos (default 120 min si no está en la BD) x lead
                    TTLMinutes = t.MemoryTTLMinutes > 0 ? t.MemoryTTLMinutes : 120,
                    //Cache en Redis (usa variable de entorno o cadena vacía)
                    Redis = new RedisConfig
                    {
                        ConnectionString = Environment.GetEnvironmentVariable("REDIS__CS") ?? string.Empty,
                        Prefix = "kommoai"
                    },
                    //Cache de imágenes (default 3 min si no está en la BD)
                    ImageCacheTTLMinutes = t.ImageCacheTTLMinutes > 0 ? t.ImageCacheTTLMinutes : 3
                }
            };
        }
    }
}
