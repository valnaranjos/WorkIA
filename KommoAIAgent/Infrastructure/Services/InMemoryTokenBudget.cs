using KommoAIAgent.Application.Interfaces;
using KommoAIAgent.Domain.Tenancy;
using Microsoft.Extensions.Caching.Memory;

namespace KommoAIAgent.Infrastructure.Services
{
    /// <summary>
    /// Presupuesto de tokens por tenant según el periodo configurado (Daily/Monthly).
    /// InMemory: simple y suficiente para 1 instancia. Para varias instancias, migrar a Redis.
    /// </summary>
    public sealed class InMemoryPeriodicTokenBudget : ITokenBudget
    {
        private readonly IMemoryCache _cache;

        public InMemoryPeriodicTokenBudget(IMemoryCache cache) => _cache = cache;

        private static (string key, DateTimeOffset expiresAt) KeyAndExpiry(ITenantContext t)
        {
            var period = t.Config.Budgets?.Period?.Trim().ToLowerInvariant() ?? "monthly";
            var slug = t.CurrentTenantId.Value;

            if (period == "daily")
            {
                var now = DateTimeOffset.UtcNow;
                var key = $"budget:{slug}:D:{now:yyyyMMdd}";
                var end = new DateTimeOffset(now.Year, now.Month, now.Day, 23, 59, 59, TimeSpan.Zero);
                return (key, end);
            }
            else // monthly (default)
            {
                var now = DateTimeOffset.UtcNow;
                var key = $"budget:{slug}:M:{now:yyyyMM}";
                // expira al último segundo del mes UTC
                var firstNextMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
                var end = firstNextMonth.AddSeconds(-1);
                return (key, end);
            }
        }

        public Task<int> GetUsedTodayAsync(ITenantContext tenant, CancellationToken ct = default)
        {
            // Semánticamente es “periodo actual”, dejamos nombre por compatibilidad
            var (key, _) = KeyAndExpiry(tenant);
            var used = _cache.Get<int?>(key) ?? 0;
            return Task.FromResult(used);
        }

        public Task<bool> CanConsumeAsync(ITenantContext tenant, int requestedTokens, CancellationToken ct = default)
        {
            var limit = tenant.Config.Budgets?.TokenLimit ?? 0; // 0 = sin límite
            if (limit <= 0) return Task.FromResult(true);

            var (key, _) = KeyAndExpiry(tenant);
            var used = _cache.Get<int?>(key) ?? 0;
            var ok = used + Math.Max(0, requestedTokens) <= limit;
            return Task.FromResult(ok);
        }

        public Task AddUsageAsync(ITenantContext tenant, int usedTokens, CancellationToken ct = default)
        {
            var (key, exp) = KeyAndExpiry(tenant);
            var current = _cache.Get<int?>(key) ?? 0;
            _cache.Set(key, current + Math.Max(0, usedTokens), exp);
            return Task.CompletedTask;
        }
    }
}
