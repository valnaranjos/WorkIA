using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Domain.Tenancy;
using System.Text.Json;

namespace KommoAIAgent.Application.Common
{
    /// <summary>
    /// Helpers AI compartidos (DRY): tenant, provider/model y estimación de tokens.
    /// OJO: hoy usamos defaults para provider/model porque tu OpenAIConfig
    /// no define Provider/ChatModel/EmbeddingModel. Cuando los agregues al
    /// TenantConfig, ajustamos aquí para leerlos.
    /// </summary>
    public static class AiUtil
    {
        /// <summary>
        /// Slug seguro del tenant con fallback.
        /// </summary>
        public static string TenantSlug(ITenantContext t)
        {
            var slug = t.Config?.Slug;
            if (!string.IsNullOrWhiteSpace(slug))
                return slug!;

            // TenantId en tu proyecto es no-nullable; NO usar ?.Value
            var id = t.CurrentTenantId.Value;
            return string.IsNullOrWhiteSpace(id) ? "unknown" : id;
        }

        /// <summary>
        /// Id del proveedor IA. Hoy fijo "openai" (no existe Provider en OpenAIConfig).
        /// </summary>
        public static string ProviderId(TenantConfig? _)
            => "openai";

        /// <summary>
        /// Modelo de chat por defecto. Cuando agregues cfg.OpenAI.ChatModel, lo leemos aquí.
        /// </summary>
        public static string ChatModelId(TenantConfig? _)
            => "gpt-4o-mini";

        /// <summary>
        /// Modelo de embeddings por defecto. Cuando agregues cfg.OpenAI.EmbeddingModel, lo leemos aquí.
        /// </summary>
        public static string EmbeddingModelId(TenantConfig? _)
            => "text-embedding-3-small";

        /// <summary>
        /// Estimación rápida de tokens (≈ chars/4). Útil si el SDK no trae Usage.
        /// </summary>
        public static int EstimateTokens(string? text)
            => string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / 4);


        /// <summary>
        /// Obtiene el TTL de caché desde configuración de business rules del tenant. como (embeddingCacheTtlHours)
        /// </summary>
        /// <returns></returns>
        public static TimeSpan GetCacheTtl(TenantConfig? _)
        {
            try
            {
                var br = _?.BusinessRules;
                if (br is not null && br.RootElement.ValueKind == JsonValueKind.Object &&
                    br.RootElement.TryGetProperty("embeddingCacheTtlHours", out var v) &&
                    v.TryGetInt32(out var hours))
                {
                    // clamp de seguridad: 1h–168h (7d)
                    hours = Math.Clamp(hours, 1, 168);
                    return TimeSpan.FromHours(hours);
                }
            }
            catch { /* ignoramos; fallback abajo */ }

            // Default global sensato a 48HRS.
            return TimeSpan.FromHours(48);
        }
    }
}
