using KommoAIAgent.Application.Common;
using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Knowledge;
using KommoAIAgent.Knowledge.Sql;
using KommoAIAgent.Services;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Servicio de embedding usando OpenAI con caché en memoria (IMemoryCache).
/// - Reusa OpenAiService para cliente y modelo.
/// IMemoryCache por tenant (como front-cache determinado por el TTL)
/// DB (kb_embedding_cache), persistente por tenant. Para cargas grandes y persistencia.
/// -Clave de caché: emb: { tenant}:{ model}:{ SHA256(texto_normalizado)}
/// - No se guarda texto en caché, solo el vector. 
/// </summary>
public sealed class OpenAIEmbeddingService : IEmbedder
{
    private readonly OpenAiService _openai;
    private readonly ILogger<OpenAIEmbeddingService> _logger;
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenant;
    private readonly IEmbeddingCache _dbCache;
    //TTL del caché, configurable si se desea.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);

    // Dimensiones del embedding y modelo a usar, se puede configurar
    public int Dimensions => 1536;

    //Modelo de embedding a usar, recomendado text-embedding-3-small por coste y prestaciones
    public string Model => "text-embedding-3-small";

    // Por defecto, cacheado por tenant.
    private readonly bool _cacheByTenant = true;

    public OpenAIEmbeddingService(OpenAiService openai,
        IMemoryCache cache,
        ITenantContext tenant,
        IEmbeddingCache dbCache,
        ILogger<OpenAIEmbeddingService> log)
     {
        _openai = openai;
        _cache = cache;
        _tenant = tenant;
        _dbCache = dbCache;
        _logger = log;
    }
    /// <summary>
    /// Embedding para un solo texto con caché (HIT/MISS transparente).
    /// Memoria → DB → OpenAI. Guarda en DB y Memoria tras MISS.
    /// </summary>
    public async Task<float[]> EmbedTextAsync(string text, CancellationToken ct = default)
    {
        var key = CacheKey(Model, text);
        var slug = GetTenantSlug();
        var hash = ComputeTextHash(text);
        var ttl = GetCacheTtl();

        // Memoria
        if (_cache.TryGetValue(key, out float[] cached) && cached is not null)
        {
            _logger.LogDebug("Emb cache HIT (memory, single) tenant={Tenant}", slug);
            return cached;
        }

        // DB
        var dbVec = await _dbCache.TryGetAsync(slug, Model, hash, ct);
        if (dbVec is not null)
        {
            _logger.LogDebug("Emb cache HIT (db, single) tenant={Tenant}", slug);
            _cache.Set(key, dbVec, ttl);
            return dbVec;
        }

        // OpenAI
        var vec = await _openai.GetEmbeddingAsync(Model, text, ct);
        _logger.LogDebug("Emb cache MISS (single) -> OpenAI, tenant={Tenant}", slug);

        // Guardar DB + Memoria
        await _dbCache.PutAsync(slug, Model, hash, vec, ct);
        _cache.Set(key, vec, ttl);

        return vec;
    }

    /// <summary>
    /// Embeddings para lote:
    /// - Resuelve HITs en memoria
    /// - Intenta DB para los misses
    /// - Pide a OpenAI SOLO los que AÚN faltan.
    /// Guarda cada MISS en DB y Memoria manteniendo el orden.
    /// </summary>
    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts is null || texts.Count == 0) return Array.Empty<float[]>();

        var slug = GetTenantSlug();
        var ttl = GetCacheTtl();

        var result = new float[texts.Count][];
        var memKeys = new string[texts.Count];
        var hashes = new string[texts.Count];
        var missIdx = new List<int>(texts.Count);  // faltantes tras memoria
        var missIdx2 = new List<int>(texts.Count);  // faltantes tras DB

        // Memoria
        for (int i = 0; i < texts.Count; i++)
        {
            memKeys[i] = CacheKey(Model, texts[i]);
            if (_cache.TryGetValue(memKeys[i], out float[]? vec) && vec is not null)
            {
                result[i] = vec;
            }
            else
            {
                hashes[i] = ComputeTextHash(texts[i]);
                missIdx.Add(i);
            }
        }

        // DB para los que faltan
        foreach (var i in missIdx)
        {
            var dbVec = await _dbCache.TryGetAsync(slug, Model, hashes[i], ct);
            if (dbVec is not null)
            {
                result[i] = dbVec;
                _cache.Set(memKeys[i], dbVec, ttl);
            }
            else
            {
                missIdx2.Add(i);
            }
        }

        // OpenAI solo para los que aún faltan
        if (missIdx2.Count > 0)
        {
            var inputs = new List<string>(missIdx2.Count);
            foreach (var i in missIdx2) inputs.Add(texts[i]);

            var missVecs = await _openai.GetEmbeddingsAsync(Model, inputs, ct);
            for (int j = 0; j < missIdx2.Count; j++)
            {
                var i = missIdx2[j];
                var vec = missVecs[j];

                result[i] = vec;

                // Persistir y cachear
                await _dbCache.PutAsync(slug, Model, hashes[i], vec, ct);
                _cache.Set(memKeys[i], vec, ttl);
            }
        }

        _logger.LogDebug("Emb batch resuelto: memHits={MemHits} dbHits={DbHits} openaiMisses={Ai}",
            texts.Count - missIdx.Count,
            missIdx.Count - missIdx2.Count,
            missIdx2.Count);

        return result;
    }


    /// <summary>
    /// Obtiene la clave de caché para un texto y modelo dado.
    /// </summary>
    /// <param name="model"></param>
    /// <param name="text"></param>
    /// <returns></returns>
    private string CacheKey(string model, string text)
    {
        var norm = TextUtil.NormalizeForEmbeddingKey(text);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)));

        var slug = GetTenantSlug();

        return $"emb:{slug}:{model}:{hash}";
    }


    /// <summary>
    /// Obtiene el TTL de caché desde configuración de business rules del tenant. como (embeddingCacheTtlHours)
    /// </summary>
    /// <returns></returns>
    private TimeSpan GetCacheTtl()
    {
        try
        {
            var br = _tenant.Config?.BusinessRules;
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

    /// <summary>
    /// Obtiene el slug del tenant o "unknown" si no está disponible, para uso en logs.
    /// </summary>
    /// <returns></returns>
    private string GetTenantSlug()
    {
        var slug = _tenant.Config?.Slug;
        if (string.IsNullOrWhiteSpace(slug)) slug = _tenant.CurrentTenantId.Value;
        return string.IsNullOrWhiteSpace(slug) ? "unknown" : slug;
    }

    private static string ComputeTextHash(string text)
    {
        var canon = TextUtil.NormalizeForEmbeddingKey(text);
        var bytes = Encoding.UTF8.GetBytes(canon);
        return Convert.ToHexString(SHA256.HashData(bytes)); // HEX mayúsculas
    }
}
