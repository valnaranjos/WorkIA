using KommoAIAgent.Application.Interfaces;
using KommoAIAgent.Infrastructure.Services;
using KommoAIAgent.Infrastructure.Tenancy;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace KommoAIAgent.Infrastructure.Caching
{
    /// <summary>
    /// Chat memory store usando Redis, con fallback a InMemory si Redis no está disponible.
    /// </summary>
    public sealed class RedisChatMemoryStore : IChatMemoryStore
    {
        private readonly ILogger<RedisChatMemoryStore> _log;
        private readonly string _prefix;
        private readonly TimeSpan _defaultTtl;
        private readonly ConnectionMultiplexer? _redis;  // null => InMemory fallback

        private volatile bool _useFallback; // si true => usar InMemory
        private bool UseFallback => _useFallback || _redis is null;

        // InMemory fallback (thread-safe suficiente para un solo proceso)
        private readonly Dictionary<string, LinkedList<string>> _mem = new();
        private readonly object _memLock = new();

        public RedisChatMemoryStore(IOptions<MultiTenancyOptions> opts, ILogger<RedisChatMemoryStore> log)
        {
            _log = log;

            // Tomamos el primer tenant solo para default de Memory (prefijo/TTL)
            var any = opts.Value.Tenants.FirstOrDefault();
            _prefix = any?.Memory?.Redis?.Prefix ?? "kommoai";
            _defaultTtl = TimeSpan.FromMinutes(any?.Memory?.TTLMinutes ?? 120);

            var cs = any?.Memory?.Redis?.ConnectionString;
            if (!string.IsNullOrWhiteSpace(cs))
            {
                try
                {
                    var options = ConfigurationOptions.Parse(cs, ignoreUnknown: true);
                    options.AbortOnConnectFail = false;
                    options.ClientName = "KommoAIAgent";
                    options.ConnectRetry = Math.Max(1, options.ConnectRetry);
                    options.KeepAlive = options.KeepAlive == 0 ? 30 : options.KeepAlive;

                    _redis = ConnectionMultiplexer.Connect(options);

                    // 🔔 Detectar caídas y restauraciones → activar/desactivar fallback
                    _redis.ConnectionFailed += (s, e) =>
                    {
                        _useFallback = true;
                        _log.LogWarning("Redis ConnectionFailed ({EndPoint}, {FailureType}). Activando fallback InMemory.",
                            e.EndPoint, e.FailureType);
                    };
                    _redis.ConnectionRestored += (s, e) =>
                    {
                        _useFallback = false;
                        _log.LogInformation("Redis ConnectionRestored ({EndPoint}). Volviendo a Redis.", e.EndPoint);
                    };

                    // ✅ Ping inicial: si no responde, activar fallback
                    try
                    {
                        var db = _redis.GetDatabase();
                        _ = db.Ping();
                        _log.LogInformation("RedisChatMemoryStore conectado a {Endpoints}", string.Join(",", options.EndPoints));
                    }
                    catch (Exception pingEx)
                    {
                        _useFallback = true;
                        _log.LogWarning(pingEx, "Redis no respondió al Ping; usando InMemory fallback.");
                    }
                }
                catch (Exception ex)
                {
                    _useFallback = true; // <- asegúrate de activar fallback si conectar falla
                    _log.LogWarning(ex, "No se pudo conectar a Redis; usando InMemory fallback.");
                }

            }
        }

        private string Key(string tenant, long leadId) => $"{_prefix}:{tenant}:lead:{leadId}:chat";

        public async Task AppendAsync(string tenant, long leadId, string role, string content, TimeSpan ttl, CancellationToken ct = default)
        {
            var entry = $"{role}\n{content}";
            var key = Key(tenant, leadId);

            if (UseFallback)
            {
                lock (_memLock)
                {
                    if (!_mem.TryGetValue(key, out var list)) _mem[key] = list = new LinkedList<string>();
                    list.AddLast(entry);
                }
                return;
            }

            try
            {
                var db = _redis!.GetDatabase();
                await db.ListRightPushAsync(key, entry);
                await db.KeyExpireAsync(key, ttl == TimeSpan.Zero ? _defaultTtl : ttl);
            }
            catch (RedisConnectionException ex)
            {
                _useFallback = true;
                _log.LogWarning(ex, "Redis Append falló; activando fallback InMemory.");
                await AppendAsync(tenant, leadId, role, content, ttl, ct); // reintento en InMemory
            }
        }


        public async Task<IReadOnlyList<(string role, string content)>> GetAsync(string tenant, long leadId, int lastN, CancellationToken ct = default)
        {
            var key = Key(tenant, leadId);

            if (UseFallback)
            {
                lock (_memLock)
                {
                    if (!_mem.TryGetValue(key, out var list) || list.Count == 0)
                        return Array.Empty<(string, string)>();

                    IEnumerable<string> take = list;
                    if (lastN > 0 && list.Count > lastN)
                        take = list.Skip(list.Count - lastN);

                    return take.Select(Parse).ToArray();
                }
            }

            try
            {
                var db = _redis!.GetDatabase();
                long len = await db.ListLengthAsync(key);
                if (len <= 0) return Array.Empty<(string, string)>();

                long start = lastN <= 0 ? 0 : Math.Max(0, len - lastN);
                var items = await db.ListRangeAsync(key, start, -1);

                return items.Select(x => Parse((string)x!)).ToArray();
            }
            catch (RedisConnectionException ex)
            {
                _useFallback = true;
                _log.LogWarning(ex, "Redis Get falló; activando fallback InMemory.");
                return await GetAsync(tenant, leadId, lastN, ct); // reintento en InMemory
            }

            static (string role, string content) Parse(string s)
            {
                var idx = s.IndexOf('\n');
                if (idx <= 0) return ("user", s);
                return (s[..idx], s[(idx + 1)..]);
            }
        }


        public async Task ClearAsync(string tenant, long leadId, CancellationToken ct = default)
        {
            var key = Key(tenant, leadId);

            if (UseFallback)
            {
                lock (_memLock) _mem.Remove(key);
                return;
            }

            try
            {
                var db = _redis!.GetDatabase();
                await db.KeyDeleteAsync(key);
            }
            catch (RedisConnectionException ex)
            {
                _useFallback = true;
                _log.LogWarning(ex, "Redis Clear falló; activando fallback InMemory.");
                await ClearAsync(tenant, leadId, ct); // reintento en InMemory
            }
        }

    }
}
