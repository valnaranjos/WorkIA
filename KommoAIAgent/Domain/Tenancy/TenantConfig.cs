namespace KommoAIAgent.Domain.Tenancy
{
    /// <summary>
    /// Configuración específica por tenant.
    /// </summary>
    public sealed class TenantConfig
    {
        public required string Slug { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public required KommoConfig Kommo { get; init; }
        public required OpenAIConfig OpenAI { get; init; }
        public DebounceConfig Debounce { get; init; } = new();
        public BudgetConfig Budgets { get; init; } = new();
    }


    /// <summary>
    /// Configuración específica para Kommo por tenant.
    /// </summary>
    public sealed class KommoConfig
    {
        public required string BaseUrl { get; init; }
        public required string AccessToken { get; init; }
        public FieldIds FieldIds { get; init; } = new();
        public ChatConfig Chat { get; init; } = new();
    }


    public sealed class FieldIds { public long MensajeIA { get; init; } }
    public sealed class ChatConfig { public string? ScopeId { get; init; } }


    public sealed class OpenAIConfig { public required string ApiKey { get; init; } public string Model { get; init; } = "gpt-4o-mini"; public string VisionModel { get; init; } = "gpt-4o"; }


    /// <summary>
    /// Configuración para el debounce de mensajes entrantes.
    /// </summary>
    public sealed class DebounceConfig
    {
        public int WindowMs { get; init; } = 1000;
        public int MaxBurstMs { get; init; } = 2500;
        public ForceFlushConfig ForceFlush { get; init; } = new();
    }


    // Configuración para forzar el envío de mensajes acumulados.
    public sealed class ForceFlushConfig { public int MaxFragments { get; init; } = 5; public int MaxChars { get; init; } = 1200; }

    /// Configuración para límites de uso y presupuesto.
    public sealed class BudgetConfig { public int DailyTokenLimit { get; init; } = 500000; }
}
