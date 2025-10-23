using System.Text.Json;

namespace KommoAIAgent.Domain.Tenancy
{ /// <summary>
  /// Contexto del tenant actual (scoped a la request).
  /// Usar siempre vía DI para acceder al TenantId y su configuración
  /// </summary>
    public sealed class TenantConfig
    {
        internal int MonthlyTokenBudget;

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

        // Reglas de negocio en JSON (por tenant)
        public JsonDocument? BusinessRules { get; set; }

        //Configuración genérica de IA (multi-provider).
        public AIProviderConfig AI { get; init; } = new();

        /// <summary>
        /// >Reglas de negocio, como texto plano (se guarda en DB como JSON o raw).
        /// </summary>
        /// <param name="json"></param>
        public void SetBusinessRules(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                BusinessRules = null;
                return;
            }

            using var doc = JsonDocument.Parse(json);
            // Clonar para no depender del using local
            BusinessRules = JsonDocument.Parse(doc.RootElement.GetRawText());
        }
    }


    /// <summary>
    /// Configuración específica para Kommo por tenant.
    /// </summary>
    public sealed class KommoConfig
    {
        //URL base de la API de Kommo (p.ej. https://yourdomain.amocrm.com)
        public required string BaseUrl { get; init; }

        //Token de acceso API (Token de larga duración por ahora)
        public required string AccessToken { get; init; }

        //Campos personalizados 
        public FieldIds FieldIds { get; init; } = new();

        // Configuración de chat (se usa módulo de chat en Kommo)
        public ChatConfig Chat { get; init; } = new();
    }


    // IDs de campos personalizados en Kommo (por ahora, solo uno, donde devuelve el mensaje la IA)
    public sealed class FieldIds { public long MensajeIA { get; init; } }

    // Configuración del módulo de chat en Kommo
    public sealed class ChatConfig { public string? ScopeId { get; init; } }


    /// <summary>
    /// Configuración específica para OpenAI por tenant. Próximo a cambio para soportar otros proveedores.
    /// </summary>
    public sealed class OpenAIConfig {
        //API Key privada del tenant (usada server-side, no exponer nunca)
        public required string ApiKey { get; init; }

        // Modelos a usar (si el tenant no especifica otro)
        public string Model { get; init; } = "gpt-4o-mini";

        // Modelo para análisis de imágenes (si el tenant no especifica otro)
        public string VisionModel { get; init; } = "gpt-4o";

        // Parámetros por defecto para las llamadas a OpenAI (si el tenant no especifica otro)
        public float? Temperature { get; init; }

        // Control de diversidad en la generación (si el tenant no especifica otro)
        public float? TopP { get; init; }
        // Máximo de tokens a generar (si el tenant no especifica otro)
        public int? MaxTokens { get; init; }

        // Prompt del sistema (por tenant)
        public string? SystemPrompt { get; set; }


    }

    /// <summary>
    /// Configuración genérica de IA (extensible a múltiples providers).
    /// </summary>
    public sealed class AIProviderConfig
    {
        /// <summary>
        /// Proveedor de IA: "openai", "anthropic", "azure", etc.
        /// </summary>
        public string Provider { get; init; } = "openai";

        /// <summary>
        /// API Key específica del tenant (null = usa la global).
        /// </summary>
        public string? ApiKey { get; init; }

        /// <summary>
        /// Modelo a usar (ej: "gpt-4o-mini", "claude-3-5-sonnet").
        /// </summary>
        public string? Model { get; init; }

        /// <summary>
        /// Temperatura (0.0 - 2.0).
        /// </summary>
        public float Temperature { get; init; } = 0.7f;

        /// <summary>
        /// Máximo de tokens a generar.
        /// </summary>
        public int MaxTokens { get; init; } = 400;

        /// <summary>
        /// Proveedor de respaldo si el principal falla.
        /// </summary>
        public string? FallbackProvider { get; init; }

        /// <summary>
        /// API Key del proveedor de respaldo.
        /// </summary>
        public string? FallbackApiKey { get; init; }

        /// <summary>
        /// Habilitar extracción automática de imágenes (OCR).
        /// </summary>
        public bool EnableImageOCR { get; init; } = true;

        /// <summary>
        /// Habilitar invocación automática de conectores desde imágenes.
        /// </summary>
        public bool EnableAutoConnectorInvocation { get; init; } = true;
    }



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

        // Alerta cuando se supera este % del presupuesto
        public int AlertThresholdPct { get; set; } = 75;

        // Límite de ráfagas por 5 minutos (0 = sin límite)
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

        // Tiempo de vida del caché de imágenes (minutos)
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
