using Npgsql;
using NpgsqlTypes;
using Pgvector;            // Vector
using Pgvector.Npgsql;     // mapeo
using KommoAIAgent.Knowledge;
using KommoAIAgent.Knowledge.Sql;

public sealed class PostgresEmbeddingCache : IEmbeddingCache
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresEmbeddingCache> _logger;

    public PostgresEmbeddingCache(NpgsqlDataSource dataSource, ILogger<PostgresEmbeddingCache> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<float[]?> TryGetAsync(string tenantSlug, string model, string textHash, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        const string sql = @"
            SELECT embedding
            FROM kb_embedding_cache
            WHERE tenant_slug = @t AND model = @m AND text_hash = @h
            LIMIT 1;
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("t", NpgsqlDbType.Text, tenantSlug);
        cmd.Parameters.AddWithValue("m", NpgsqlDbType.Text, model);
        cmd.Parameters.AddWithValue("h", NpgsqlDbType.Text, textHash);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct))
        {
            // pgvector mapea a Pgvector.Vector
            var v = r.GetFieldValue<Vector>(0);
            return v.ToArray();
        }
        return null;
    }


    public async Task PutAsync(string tenantSlug, string model, string textHash, float[] vector, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        const string upsert = @"
            INSERT INTO kb_embedding_cache (tenant_slug, model, text_hash, embedding)
            VALUES (@t, @m, @h, @e)
            ON CONFLICT (tenant_slug, model, text_hash) DO NOTHING;
        ";

        await using var cmd = new NpgsqlCommand(upsert, conn);
        cmd.Parameters.AddWithValue("t", NpgsqlDbType.Text, tenantSlug);
        cmd.Parameters.AddWithValue("m", NpgsqlDbType.Text, model);
        cmd.Parameters.AddWithValue("h", NpgsqlDbType.Text, textHash);
        cmd.Parameters.AddWithValue("e", new Vector(vector));

        await cmd.ExecuteNonQueryAsync(ct);
    }
}

