using Microsoft.Extensions.Caching.Memory;
using System;

namespace KommoAIAgent.Infrastructure
{
    /// <summary>
    /// Clase para almacenar en caché la última imagen enviada por un usuario.
    /// </summary>
    public sealed  class LastImageCache
    {
        private readonly IMemoryCache _cache;

        public LastImageCache(IMemoryCache cache) => _cache = cache;

        ///key para almacenar la imagen en caché.
        public static string ImgKey(long leadId) => $"lastimg:{leadId}";

        // Contexto de la imagen: bytes y tipo MIME.
        public readonly record struct ImageCtx(byte[] Bytes, string Mime);

        /// <summary>
        /// Pone en caché la última imagen enviada por el usuario, con expiración deslizante de 3 minutos.
        /// </summary>
        /// <param name="leadId"></param>
        /// <param name="bytes"></param>
        /// <param name="mime"></param>
        public void SetLastImage(long leadId, byte[] bytes, string mime)
        {
            _cache.Set(ImgKey(leadId), new ImageCtx(bytes, mime),
                new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(3) });
        }

        /// <summary>
        /// Intenta obtener la última imagen enviada por el usuario.
        /// </summary>
        /// <param name="leadId"></param>
        /// <param name="ctx"></param>
        /// <returns></returns>
        public bool TryGetLastImage(long leadId, out ImageCtx ctx)
            => _cache.TryGetValue(ImgKey(leadId), out ctx);


        /// <summary>
        /// Borra la imagen en caché para un lead específico.
        /// </summary>
        /// <param name="leadId"></param>
        public void Remove(long leadId) => _cache.Remove(ImgKey(leadId));
    }
}
