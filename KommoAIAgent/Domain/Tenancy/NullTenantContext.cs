using KommoAIAgent.Application.Interfaces;

namespace KommoAIAgent.Domain.Tenancy;
// Contexto de tenant SOLO para design-time (migraciones).
internal sealed class NullTenantContext : ITenantContext
{
    // Algunos proyectos exponen ambas propiedades; mapea a lo mismo por simplicidad.
    public TenantId CurrentTenantId { get; } = TenantId.From("design");
    public TenantId Id => CurrentTenantId;

    public TenantConfig Config { get; } = new()
    {
        Slug = "design",
        DisplayName = "DesignTime",

        Kommo = new KommoConfig
        {
            BaseUrl = "https://design.kommo.com",
            // required en tu modelo:
            AccessToken = string.Empty,
            FieldIds = new FieldIds { MensajeIA = 0 },
            Chat = new ChatConfig { ScopeId = string.Empty }
        },

        OpenAI = new OpenAIConfig
        {
            ApiKey = string.Empty,        // en runtime vendrá de secrets/env
            Model = "gpt-4o-mini",
            VisionModel = "gpt-4o"
        },

        Debounce = new DebounceConfig
        {
            WindowMs = 800,
            MaxBurstMs = 1800,
            ForceFlush = new ForceFlushConfig { MaxFragments = 1, MaxChars = 1 }
        },

        Budgets = new BudgetConfig
        {
            Period = "Monthly",
            TokenLimit = 5_000_000,
            EstimationFactor = 0.85,
            ExceededMessage = null,
            BurstPerMinute = 12
        },

        Memory = new MemoryConfig
        {
            TTLMinutes = 120,
            Redis = new RedisConfig { ConnectionString = string.Empty, Prefix = "kommoai" }
        }
    };
}
