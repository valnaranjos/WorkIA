using KommoAIAgent.Domain.Tenancy;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace KommoAIAgent.Api.Contracts
{

    /// <summary>
    /// Request para CREAR un tenant nuevo (Admin).
    /// Mantiene nombres alineados con tu tabla 'tenants'.
    /// </summary>
    public sealed class TenantCreateRequest
    {
        // ---------- Identidad ----------
        /// <summary>Slug único del tenant. Si no hay, el controller puede derivarlo del KommoBaseUrl.</summary>
        [RegularExpression("^[a-z0-9-]{3,50}$", ErrorMessage = "Slug inválido (usa a-z, 0-9 y guiones).")]
        public string? Slug { get; set; }

        [Required, MinLength(2)]
        public string DisplayName { get; set; } = string.Empty;

        // ---------- Kommo ----------
        [Required, Url]
        public string KommoBaseUrl { get; set; } = string.Empty;

        /// <summary>Token OAuth para llamadas a Kommo (opcional si prefieres setearlo por PUT dedicado).</summary>
        public string? KommoAccessToken { get; set; }

        /// <summary>Id del campo largo “Mensaje IA” en Kommo: REQUERIDO para poder publicar la respuesta.</summary>
        [Required]
        public long KommoMensajeIaFieldId { get; set; }

        /// <summary>Scope/pipe/etc. Si no aplica, 0 o null.</summary>
        public string? KommoScopeId { get; set; } = string.Empty;

        // ---------- IA ----------
        /// <summary>Proveedor de IA (por ahora 'OpenAI').</summary>
        public string IaProvider { get; set; } = "OpenAI";

        /// <summary>Modelo por defecto para chat.</summary>
        public string IaModel { get; set; } = "gpt-4o-mini";

        [Range(1, 8192)]
        // Máximo tokens de respuesta (el prompt puede ser más grande).
        public int MaxTokens { get; set; } = 500;

        [Range(0, 2)]
        // Diversidad/creatividad de la respuesta.
        public float Temperature { get; set; } = 0.8f;

        [Range(0, 1)]
        public float TopP { get; set; } = 1.0f;

        /// <summary>System prompt base (tono). Puede ir vacío y lo completas luego.</summary>
        public string? SystemPrompt { get; set; }

        /// <summary>Reglas de negocio (JSON). Ej: {"horario":"L-V 8-6", ...}</summary>
        public JsonElement? BusinessRules { get; set; }

        // ---------- Operación / límites ----------
        [Range(0, 60000)]
        public int DebounceMs { get; set; } = 1500;

        [Range(0, 1000)]
        public int RatePerMinute { get; set; } = 12;

        [Range(0, 5000)]
        public int RatePer5Minutes { get; set; } = 50;

        [Range(0, 100000)]
        public int MemoryTTLMinutes { get; set; } = 120;

        [Range(0, 100000)]
        public int ImageCacheTTLMinutes { get; set; } = 60;

        // ---------- Presupuesto (legado en DB) ----------
        /// <summary>Límite mensual de tokens (chat in+out). 0 = sin límite.</summary>
        [Range(0, int.MaxValue)]
        public int MonthlyTokenBudget { get; set; } = 200_000;

        /// <summary>Umbral de alerta (porcentaje 0..100). Solo si planeas alertas más adelante.</summary>
        [Range(0, 100)]
        public int AlertThresholdPct { get; set; } = 80;

        // ---------- Estado ----------
        /// <summary> true al crear.</summary>
        public bool IsActive { get; set; } = true;
    }


    /// <summary>
    /// Request para ACTUALIZAR un tenant (Admin).
    /// Todos los campos son opcionales; solo se actualiza lo que envías.
    /// </summary>
    public sealed class TenantUpdateRequest
    {
        // Identidad
        public string? DisplayName { get; set; }

        // Kommo
        [Url]
        public string? KommoBaseUrl { get; set; }
        public string? KommoAccessToken { get; set; }
        public long? KommoMensajeIaFieldId { get; set; }
        public string? KommoScopeId { get; set; }

        // IA
        public string? IaProvider { get; set; }
        public string? IaModel { get; set; }
        [Range(1, 8192)]
        public int? MaxTokens { get; set; }
        [Range(0, 2)]
        public float? Temperature { get; set; }
        [Range(0, 1)]
        public float? TopP { get; set; }
        public string? SystemPrompt { get; set; }
        public JsonElement? BusinessRules { get; set; }

        // Operación / límites
        [Range(0, 60000)]
        public int? DebounceMs { get; set; }
        [Range(0, 1000)]
        public int? RatePerMinute { get; set; }
        [Range(0, 5000)]
        public int? RatePer5Minutes { get; set; }
        [Range(0, 100000)]
        public int? MemoryTTLMinutes { get; set; }
        [Range(0, 100000)]
        public int? ImageCacheTTLMinutes { get; set; }

        // Presupuesto (legado)
        [Range(0, int.MaxValue)]
        public int? MonthlyTokenBudget { get; set; }
        [Range(0, 100)]
        public int? AlertThresholdPct { get; set; }

        // Estado
        public bool? IsActive { get; set; }
    }

    /// <summary>
    /// DTO para actualizar el prompt del sistema de un tenant.
    /// </summary>
    public sealed class UpdatePromptRequest
    {
        public string SystemPrompt { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO para actualizar las reglas de negocio (JSON) de un tenant.
    /// </summary>
    public sealed class UpdateRulesRequest
    {
        [Required]
        public JsonElement Rules { get; set; }
    }

    // ========= RESPONSE =========
    public sealed record TenantResponse(
        Guid Id,
        string Slug,
        string DisplayName,
        bool IsActive,
        string KommoBaseUrl,
        string IaProvider,
        string IaModel,
        int MonthlyTokenBudget,
        double AlertThresholdPct,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        string SystemPrompt,
        string? BusinessRulesJson
    );
}
