using KommoAIAgent.Application.Common;
using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Domain.Tenancy;
using KommoAIAgent.Knowledge;
using KommoAIAgent.Services;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Servicio de embedding usando OpenAI con caché en memoria (IMemoryCache).
/// - Reusa OpenAiService para cliente y modelo.
/// -Clave de caché: emb: { tenant}:{ model}:{ SHA256(texto_normalizado)}
/// - No se guarda texto en caché, solo el vector. 
/// </summary>
public sealed class OpenAIEmbeddingService : IEmbedder
{
    private readonly OpenAiService _openai;
    private readonly ILogger<OpenAIEmbeddingService> _logger;
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenant;
    //TTL del caché, configurable si se desea.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);

    // Dimensiones del embedding y modelo a usar, se puede configurar
    public int Dimensions => 1536;

    //Modelo de embedding a usar, recomendado text-embedding-3-small por coste y prestaciones
    public string Model => "text-embedding-3-small";

    // Por defecto, cacheado por tenant.
    private readonly bool _cacheByTenant = true;

    public OpenAIEmbeddingService(OpenAiService openai, IMemoryCache cache, ITenantContext tenant, ILogger<OpenAIEmbeddingService> log)

        {
        _openai = openai;
        _cache = cache;
        _tenant = tenant;
        _logger = log;
    }
    /// <summary>
    /// Embedding para un solo texto con caché (HIT/MISS transparente).
    /// </summary>
    public async Task<float[]> EmbedTextAsync(string text, CancellationToken ct = default)
    {
        var key = CacheKey(Model, text);

        if (_cache.TryGetValue(key, out float[] cached) && cached is not null)
            return cached;

        // Llamada real a OpenAI a través de tu servicio existente (no cambiamos tu pipeline)
        var vec = await _openai.GetEmbeddingAsync(Model, text, ct);

        _cache.Set(key, vec, CacheTtl);
        return vec;
    }

    /// <summary>
    /// Embeddings para lote:
    /// - Resuelve HITs desde caché
    /// - Pide a OpenAI SOLO los MISS (manteniendo orden)
    /// </summary>
    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts is null || texts.Count == 0) return Array.Empty<float[]>();

        var result = new float[texts.Count][];
        var missIdx = new List<int>(texts.Count);
        var missInp = new List<string>(texts.Count);

        // 1) Resolver hits y recolectar misses
        for (int i = 0; i < texts.Count; i++)
        {
            var key = CacheKey(Model, texts[i]);
            if (_cache.TryGetValue(key, out float[]? vec) && vec is not null)
            {
                result[i] = vec; // HIT
            }
            else
            {
                missIdx.Add(i);
                missInp.Add(texts[i]);
            }
        }

        // 2) Si no hay misses, devolvemos
        if (missInp.Count == 0) return result;

        // 3) Llamada a OpenAI SOLO con los misses (tu servicio ya expone batch)
        var missVecs = await _openai.GetEmbeddingsAsync(Model, missInp, ct);

        // 4) Mapear a posiciones originales y guardar en caché
        for (int j = 0; j < missIdx.Count; j++)
        {
            var i = missIdx[j];
            var vec = missVecs[j];

            result[i] = vec;

            var key = CacheKey(Model, texts[i]);
            _cache.Set(key, vec, GetCacheTtl());
        }

        return result;
    }
    // Clave de caché AISLADA POR TENANT
    private string CacheKey(string model, string text)
    {
        var norm = TextUtil.NormalizeForEmbeddingKey(text);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)));

        // slug preferido; si no hay, usamos el CurrentTenantId.Value como fallback
        string slug = _tenant.Config?.Slug;
        if (string.IsNullOrWhiteSpace(slug))
            slug = _tenant.CurrentTenantId.Value;
        if (string.IsNullOrWhiteSpace(slug))
            slug = "unknown";

        return $"emb:{slug}:{model}:{hash}";
    }

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

}
