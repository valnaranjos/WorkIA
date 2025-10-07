using KommoAIAgent.Application.Common;
using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Knowledge;
using KommoAIAgent.Knowledge.Sql;
using KommoAIAgent.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KommoAIAgent.Services;
/// <summary>
/// Servicio de embedding con doble caché:
/// IMemoryCache por tenant (como front-cache determinado por el TTL)
/// DB (kb_embedding_cache), persistente por tenant. Para cargas grandes y persistencia.
/// -Clave de caché: emb: { tenant}:{ model}:{ SHA256(texto_normalizado)}
/// - No se guarda texto en caché, solo el vector. 
/// /// Usa un <see cref="IEmbeddingProvider"/> para resolver el proveedor real (hoy: OpenAI).
/// </summary>
public sealed class OpenAIEmbeddingService : IEmbedder
{
    //Listo para escalar a más proveedores si se desea.
    private readonly IEmbeddingProvider _provider;

    private readonly ILogger<OpenAIEmbeddingService> _logger;
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenant;
    private readonly IEmbeddingCache _dbCache;
    private readonly IAIUsageTracker _usage;


    // Dimensiones del embedding y modelo a usar, traídos del proveedor.
    public int Dimensions => _provider.Dimensions;

    //Modelo de embedding a usar, traído del proveedor.
    public string Model => _provider.Model;

    public OpenAIEmbeddingService(
        IEmbeddingProvider provider,
        IMemoryCache cache,
        ITenantContext tenant,
        IEmbeddingCache dbCache,
        IAIUsageTracker usage,
        ILogger<OpenAIEmbeddingService> log)
     {
        _provider = provider;
        _cache = cache;
        _tenant = tenant;
        _dbCache = dbCache;
        _logger = log;
        _usage = usage;
    }
    /// <summary>
    /// Embedding para un solo texto con caché (HIT/MISS transparente).
    /// Memoria → DB → Proveedor. Guarda en DB y Memoria tras MISS.
    /// </summary>
    public async Task<float[]> EmbedTextAsync(string text, CancellationToken ct = default)
    {
        
        var provider = AiUtil.ProviderId(_tenant.Config);
        var key = CacheKey(provider, Model, text);
        var slug = AiUtil.TenantSlug(_tenant);
        var hash = ComputeTextHash(text);
        var ttl = AiUtil.GetCacheTtl(_tenant.Config);

        // Memoria
        if (_cache.TryGetValue(key, out float[] cached) && cached is not null)
        {
            _logger.LogDebug("Emb cache HIT (memory, single) tenant={Tenant}", slug);
            return cached;
        }

        // DB
        var dbVec = await _dbCache.TryGetAsync(slug, provider, Model, hash, ct);
        if (dbVec is not null)
        {
            _logger.LogDebug("Emb cache HIT (db, single) tenant={Tenant}", slug);
            _cache.Set(key, dbVec, ttl);
            return dbVec;
        }

        // Proveedor
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15)); // ← TIMEOUT DEFENSIVO

        float[] vec;
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            _logger.LogInformation("Embeddings -> provider (single) tenant={Tenant}", slug);
            vec = await _provider.EmbedTextAsync(text, linkedCts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Embedding cancelado por request (tenant={Tenant})", slug);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timeout al pedir embedding (tenant={Tenant})", slug);
            throw new TimeoutException($"Embedding timeout después de 15s (tenant={slug})");
        }

        // Guardar DB + Memoria
        await _dbCache.PutAsync(slug, provider, Model, hash, vec, ct);
        _cache.Set(key, vec, ttl);

        // MÉTRICA: solo MISS real
        await _usage.TrackEmbeddingAsync(
            tenant: slug,
            provider: provider,
            model: Model,
            charCount: text?.Length ?? 0,
            estCostUsd: null,
            ct);

        return vec;
    }

    /// <summary>
    /// Embeddings para lote:
    /// - Resuelve HITs en memoria
    /// - Intenta DB para los misses
    /// - Pide al proveedor SOLO los que AÚN faltan.
    /// Guarda cada MISS en DB y Memoria manteniendo el orden.
    /// </summary>
    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts is null || texts.Count == 0) return Array.Empty<float[]>();

        var slug = AiUtil.TenantSlug(_tenant);
        var provider = AiUtil.ProviderId(_tenant.Config);
        var ttl = AiUtil.GetCacheTtl(_tenant.Config);


        var result = new float[texts.Count][];
        var memKeys = new string[texts.Count];
        var hashes = new string[texts.Count];

        var missIdx = new List<int>(texts.Count);  // faltantes tras memoria
        var missIdx2 = new List<int>(texts.Count);  // faltantes tras DB


        // Memoria
        for (int i = 0; i < texts.Count; i++)
        {
            memKeys[i] = CacheKey(provider, Model, texts[i]);
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
            var dbVec = await _dbCache.TryGetAsync(slug, provider, Model, hashes[i], ct);
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

        // Proveedor solo para los que aún faltan
        if (missIdx2.Count > 0)
        {
            var inputs = new List<string>(missIdx2.Count);
            foreach (var i in missIdx2) inputs.Add(texts[i]);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20)); // timeout defensivo

            float[][] missVecs;
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                _logger.LogInformation("Embeddings -> provider (batch) tenant={Tenant} misses={Count}",
                    slug, missIdx2.Count);
                missVecs = await _provider.EmbedBatchAsync(inputs, linkedCts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Batch embedding cancelado (tenant={Tenant})", slug);
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Timeout en batch embedding (tenant={Tenant}, count={Count})",
                    slug, missIdx2.Count);
                throw new TimeoutException($"Batch embedding timeout (tenant={slug}, {missIdx2.Count} items)");
            }
            // ==============================
            //Guardar y cachear

            for (int j = 0; j < missIdx2.Count; j++)
            {
                var i = missIdx2[j];
                var vec = missVecs[j];

                result[i] = vec;

                // Persistir y cachear
                await _dbCache.PutAsync(slug, provider, Model, hashes[i], vec, ct);
                _cache.Set(memKeys[i], vec, ttl);
            }

            // Métrica: SOLO de los MISS
            var chars = 0;
            foreach (var s in inputs) chars += s?.Length ?? 0;

            await _usage.TrackEmbeddingAsync(slug, provider, Model, chars, null, ct);
        }

            return result;
        }


    /// <summary>
    /// Obtiene la clave de caché para un texto y modelo dado.
    /// </summary>
    /// <param name="provider">Proveedor de IA</param>
    /// <param name="model">Modelo</param>
    /// <param name="text"></param>
    /// <returns></returns>
    private string CacheKey(string provider, string model, string text)
    {
        var slug = AiUtil.TenantSlug(_tenant);
        var norm = TextUtil.NormalizeForEmbeddingKey(text);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)));
                
        return $"emb:{slug}:{provider}:{model}:{hash}";
    }

   
    /// <summary>
    /// Computa el hash SHA256 en HEX (mayúsculas) del texto normalizado para uso en caché y DB.
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    private static string ComputeTextHash(string text)
    {
        var canon = TextUtil.NormalizeForEmbeddingKey(text);
        var bytes = Encoding.UTF8.GetBytes(canon);
        return Convert.ToHexString(SHA256.HashData(bytes)); // HEX mayúsculas
    }

}
