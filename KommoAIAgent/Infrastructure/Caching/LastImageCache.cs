using KommoAIAgent.Application.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace KommoAIAgent.Infrastructure.Caching
{
    /// <summary>
    /// Caché para la última imagen enviada por usuario, con límite de tamaño.
    /// </summary>
    public sealed class LastImageCache
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<LastImageCache>? _logger;

        // 🔧 FIX: Límite de tamaño por imagen (5MB)
        private const long MaxImageBytes = 5 * 1024 * 1024;

        public LastImageCache(IMemoryCache cache, ILogger<LastImageCache>? logger = null)
        {
            _cache = cache;
            _logger = logger;
        }

        public static string ImgKey(string tenant, long leadId) => $"lastimg:{tenant}:{leadId}";

        public readonly record struct ImageCtx(byte[] Bytes, string Mime);

        /// <summary>
        /// Almacena la última imagen con TTL deslizante y límite de tamaño.
        /// </summary>
        public void SetLastImage(string tenant, long leadId, byte[] bytes, string mime)
        {
            // 🔧 FIX: Validar tamaño antes de cachear
            if (bytes.Length > MaxImageBytes)
            {
                _logger?.LogWarning(
                    "Imagen demasiado grande para caché: {Size}KB > {Max}MB (tenant={Tenant}, lead={Lead})",
                    bytes.Length / 1024, MaxImageBytes / (1024 * 1024), tenant, leadId
                );
                return;
            }

            var options = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(3),
                // 🔧 FIX: Agregar límite de tamaño para evitar OOM
                Size = bytes.Length,
                Priority = CacheItemPriority.Low // Puede ser evictado si falta memoria
            };

            _cache.Set(ImgKey(tenant, leadId), new ImageCtx(bytes, mime), options);
        }

        public bool TryGetLastImage(string tenant, long leadId, out ImageCtx img)
            => _cache.TryGetValue(ImgKey(tenant, leadId), out img);

        public void Remove(string tenant, long leadId)
            => _cache.Remove(ImgKey(tenant, leadId));
    }
}