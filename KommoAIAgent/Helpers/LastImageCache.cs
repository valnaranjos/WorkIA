using Microsoft.Extensions.Caching.Memory;
using System;

namespace KommoAIAgent.Helpers
{
     public sealed  class LastImageCache
    {
        private readonly IMemoryCache _cache;

        public LastImageCache(IMemoryCache cache) => _cache = cache;

        public static string ImgKey(long leadId) => $"lastimg:{leadId}";
        public readonly record struct ImageCtx(byte[] Bytes, string Mime);

        public void SetLastImage(long leadId, byte[] bytes, string mime)
        {
            _cache.Set(ImgKey(leadId), new ImageCtx(bytes, mime),
                new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(3) });
        }

        public bool TryGetLastImage(long leadId, out ImageCtx ctx)
            => _cache.TryGetValue(ImgKey(leadId), out ctx);
    }
}
