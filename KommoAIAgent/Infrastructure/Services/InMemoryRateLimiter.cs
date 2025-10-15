using KommoAIAgent.Application.Interfaces;
using KommoAIAgent.Domain.Tenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace KommoAIAgent.Infrastructure.Services
{
    /// <summary>
    /// Rate limiter simple en memoria: N turnos por minuto por tenant (y lead).
    /// - Usa IMemoryCache: clave {tenant}:{lead}:minuteBucket
    /// - Respeta Budgets.BurstPerMinute en TenantConfig; si es 0, no limita.
    /// </summary>
    public sealed class InMemoryRateLimiter : IRateLimiter
    {
        private readonly IMemoryCache _cache;
        private readonly IOptions<MultiTenancyOptions> _opts;

        public InMemoryRateLimiter(IMemoryCache cache, IOptions<MultiTenancyOptions> opts)
        {
            _cache = cache;
            _opts = opts;
        }

        public Task<bool> TryConsumeAsync(string tenant, long leadId, CancellationToken ct = default)
        {
            // Busca el budget del tenant; si no lo encuentra, no limita.
            var cfg = _opts.Value.Tenants.FirstOrDefault(t => t.Slug == tenant);
            var limit = cfg?.Budgets?.BurstPerMinute ?? 0;
            if (limit <= 0) return Task.FromResult(true);

            var bucket = $"{tenant}:{leadId}:{DateTimeOffset.UtcNow:yyyyMMddHHmm}";
            var count = _cache.GetOrCreate(bucket, e =>
            {
                // Expira al final del minuto en curso.
                var now = DateTimeOffset.UtcNow;
                var endOfMinute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 59, now.Offset);
                e.AbsoluteExpiration = endOfMinute.AddSeconds(1);
                return 0;
            });

            if (count >= limit) return Task.FromResult(false);

            _cache.Set(bucket, count + 1);
            return Task.FromResult(true);
        }
    }
}
