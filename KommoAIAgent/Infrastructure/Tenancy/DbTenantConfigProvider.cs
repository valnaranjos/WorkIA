using KommoAIAgent.Application.Tenancy;   // ITenantConfigProvider, TenantConfig
using KommoAIAgent.Domain.Tenancy;        // TenantId, Tenant (entity)
using KommoAIAgent.Infrastructure.Persistence; // AppDbContext
using Microsoft.EntityFrameworkCore;

namespace KommoAIAgent.Infraestructure.Tenancy
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
            // Ajusta los nombres si tu TenantConfig difiere.
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
                OpenAI = new OpenAIConfig
                {
                    ApiKey = Environment.GetEnvironmentVariable("OPENAI__API_KEY") ?? string.Empty,
                    Model = string.IsNullOrWhiteSpace(t.IaModel) ? "gpt-4o-mini" : t.IaModel,
                    VisionModel = "gpt-4o"
                },
                // --- Debounce (default estándar) ---
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
                    TokenLimit = t.MonthlyTokenBudget > 0 ? t.MonthlyTokenBudget : 5_000_000,
                    EstimationFactor = 0.85,
                    ExceededMessage = null,
                    BurstPerMinute = t.RatePerMinute > 0 ? t.RatePerMinute : 12,
                     AlertThresholdPct = t.AlertThresholdPct > 0 ? t.AlertThresholdPct : 75,
                    BurstPer5Minutes = t.RatePer5Minutes > 0 ? t.RatePer5Minutes : 60
                },

                // --- Memoria (default estándar) ---
                Memory = new MemoryConfig
                {
                    TTLMinutes = t.MemoryTTLMinutes > 0 ? t.MemoryTTLMinutes : 120,
                    Redis = new RedisConfig
                    {
                        ConnectionString = Environment.GetEnvironmentVariable("REDIS__CS") ?? string.Empty,
                        Prefix = "kommoai"
                    },
                    ImageCacheTTLMinutes = t.ImageCacheTTLMinutes > 0 ? t.ImageCacheTTLMinutes : 3
                }
            };
        }
    }
}
