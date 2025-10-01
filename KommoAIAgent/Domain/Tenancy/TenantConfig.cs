namespace KommoAIAgent.Domain.Tenancy
{ /// <summary>
  /// Contexto del tenant actual (scoped a la request).
  /// Usar siempre vía DI para acceder al TenantId y su configuración
  /// .
  /// </summary>
    public sealed class TenantConfig
    {
        // Identificador del tenant, p.ej. "serticlouddesarrollo"
        public required string Slug { get; init; }

        // Nombre legible del tenant
        public string DisplayName { get; init; } = string.Empty;

        // Configuración de Kommo (BaseUrl/Token/CFs)
        public required KommoConfig Kommo { get; init; }

        // Configuración de OpenAI (API Key y modelos)
        public required OpenAIConfig OpenAI { get; init; }

        // Debounce de mensajes entrantes
        public DebounceConfig Debounce { get; init; } = new();

        // Límites de uso y antispam por tenant
        public BudgetConfig Budgets { get; init; } = new();

        // Memoria conversacional (TTL + Redis)
        public MemoryConfig Memory { get; init; } = new();
    }


    /// <summary>
    /// Configuración específica para Kommo por tenant.
    /// </summary>
    public sealed class KommoConfig
    {
        public required string BaseUrl { get; init; }
        public required string AccessToken { get; init; }

        //Campos personalizados 
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
        // Ventana base en ms para agrupar mensajes
        public int WindowMs { get; init; } = 1000;

        // Extensión máxima de la ventana si sigue llegando tráfico
        public int MaxBurstMs { get; init; } = 2500;

        // Regla para forzar flush si se acumula demasiado
        public ForceFlushConfig ForceFlush { get; init; } = new();
    }


    // Configuración para forzar el envío de mensajes acumulados.
    public sealed class ForceFlushConfig { public int MaxFragments { get; init; } = 5; public int MaxChars { get; init; } = 1200; }

    /// Configuración para límites de uso y presupuesto.
    /// <summary>
    /// Límites de uso por tenant (presupuesto de tokens y bursts por minuto).
    /// </summary>
    public sealed class BudgetConfig
    {
        // Periodo del presupuesto: "Daily" o "Monthly".
        public string Period { get; init; } = "Monthly";

        // Límite de tokens del periodo (0 = sin límite)
        public int TokenLimit { get; init; } = 200_000;

        // Estimación cuando el SDK no expone Usage (1.0 = exacto; 0.8 = menos conservador)
        public double EstimationFactor { get; init; } = 0.85;

        // Mensaje de cortesía cuando se supera el presupuesto
        public string? ExceededMessage { get; init; }

        // 🔙 Límite de ráfagas por minuto (0 = sin límite)
        public int BurstPerMinute { get; init; } = 12;
        public int AlertThresholdPct { get; set; } = 75;
        public int BurstPer5Minutes { get; set; } = 60;
    }

    /// <summary>
    /// Configuración de memoria conversacional por tenant.
    /// </summary>
    public sealed class MemoryConfig
    {
        // Tiempo de vida del historial por lead (minutos)
        public int TTLMinutes { get; init; } = 120;

        // Configuración de Redis (si no hay ConnectionString, se usa InMemory fallback)
        public RedisConfig Redis { get; init; } = new();

        public int ImageCacheTTLMinutes { get; set; } = 3;
    }

    /// <summary>
    /// Conexión a Redis (ElastiCache). Si está vacío, el store usará memoria local.
    /// </summary>
    public sealed class RedisConfig
    {
        // Connection string de StackExchange.Redis. Ej:
        // "my-redis.cache.amazonaws.com:6379,ssl=true,password=***,abortConnect=false"
        public string? ConnectionString { get; init; }

        // Prefijo de claves para evitar colisiones entre proyectos
        public string Prefix { get; init; } = "kommoai";
    }

    

}
