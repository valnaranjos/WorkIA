using Npgsql;
using NpgsqlTypes;
using Pgvector;
using System.Data;
using Pgvector.Npgsql;
using KommoAIAgent.Application.Interfaces;

namespace KommoAIAgent.Infrastructure.Knowledge;

/// <summary>
/// Base de conocimiento KB usando PostgreSQL con extensión pgvector para almacenamiento y búsqueda vectorial.
/// </summary>
public sealed class PgVectorKnowledgeStore : IKnowledgeStore
{
    //Datasource de conexión a PostgreSQL
    private readonly NpgsqlDataSource _dataSource;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmbedder _embedder;

    public PgVectorKnowledgeStore(NpgsqlDataSource dataSource, IServiceScopeFactory scopeFactory, IEmbedder embedder)
    {
        _dataSource = dataSource;
        _scopeFactory = scopeFactory;
        _embedder = embedder;
    }

    /// <summary>
    /// Performa un UPSERT de un documento en la base de conocimiento, identificándolo por (tenant, sourceId).
    /// </summary>
    /// <param name="tenant"></param>
    /// <param name="sourceId"></param>
    /// <param name="title"></param>
    /// <param name="content"></param>
    /// <param name="tags"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<KbDocRef> UpsertDocumentAsync(string tenant, string sourceId, string? title, string content, string[]? tags = null, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);


        // UPSERT por (tenant, source_id)
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO kb_documents (tenant_slug, source_id, title, content, content_tokens, tags)
            VALUES (@t, @s, @ti, @c, GREATEST(length(@c)/4,1), @tags)
            ON CONFLICT (tenant_slug, source_id) DO UPDATE
            SET title = EXCLUDED.title, content = EXCLUDED.content, updated_utc = NOW()
            RETURNING id, COALESCE(title, '');
        ", conn);

        //Define parámetros para el comando SQL anterior
        cmd.Parameters.AddWithValue("t", NpgsqlDbType.Text, tenant);
        cmd.Parameters.AddWithValue("s", NpgsqlDbType.Text, sourceId);
        cmd.Parameters.AddWithValue("ti", title ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("c", NpgsqlDbType.Text, content);
        cmd.Parameters.AddWithValue("tags", NpgsqlDbType.Array | NpgsqlDbType.Text, (object?)tags ?? Array.Empty<string>());

        // Ejecuta y lee resultado
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var id = reader.GetInt64(0);
        var ti = reader.GetString(1);

        //Devuelve referencia al documento
        return new KbDocRef(id, sourceId, ti);
    }


    /// <summary>
    /// Rechunking y embedding de un documento ya existente en la base de conocimiento.
    /// </summary>
    /// <param name="tenant"></param>
    /// <param name="documentId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<int> RechunkAndEmbedAsync(string tenant, long documentId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);


        // lee contenido
        string? content;
        await using (var cmd = new NpgsqlCommand(@"SELECT content FROM kb_documents WHERE tenant_slug=@t AND id=@id", conn))
        {
            cmd.Parameters.AddWithValue("t", NpgsqlDbType.Text, tenant);
            cmd.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, documentId);
            content = (string?)await cmd.ExecuteScalarAsync(ct);
        }
        if (string.IsNullOrWhiteSpace(content)) return 0;

        //  rechunk
        var parts = content.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(p => p.Trim())
                           .ToArray();
        var chunks = new List<string>();
        var buf = new List<string>();
        int accTokens = 0, maxTokens = 350;

        foreach (var p in parts)
        {
            var tks = Math.Max(p.Length / 4, 1);
            if (accTokens + tks > maxTokens && buf.Count > 0)
            {
                chunks.Add(string.Join("\n\n", buf));
                buf.Clear(); accTokens = 0;
            }
            buf.Add(p); accTokens += tks;
        }
        if (buf.Count > 0) chunks.Add(string.Join("\n\n", buf));

        // embeddings (resuelve IEmbedder por scope)
        float[][] vectors;
        using (var scope = _scopeFactory.CreateScope())
        {
            var embedder = scope.ServiceProvider.GetRequiredService<IEmbedder>();
            vectors = await embedder.EmbedBatchAsync(chunks, ct);
        }

        // borra chunks previos
        await using (var del = new NpgsqlCommand(@"DELETE FROM kb_chunks WHERE tenant_slug=@t AND document_id=@d", conn))
        {
            del.Parameters.AddWithValue("t", NpgsqlDbType.Text, tenant);
            del.Parameters.AddWithValue("d", NpgsqlDbType.Bigint, documentId);
            await del.ExecuteNonQueryAsync(ct);
        }

        // bulk insert (COPY) con vector
        using (var writer = conn.BeginBinaryImport(@"COPY kb_chunks (tenant_slug, document_id, chunk_index, text, embedding, token_count) FROM STDIN (FORMAT BINARY)"))
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                writer.StartRow();
                writer.Write(tenant, NpgsqlDbType.Text);
                writer.Write(documentId, NpgsqlDbType.Bigint);
                writer.Write(i, NpgsqlDbType.Integer);
                writer.Write(chunks[i], NpgsqlDbType.Text);
                writer.Write(new Vector(vectors[i]));
                writer.Write(Math.Max(chunks[i].Length / 4, 1), NpgsqlDbType.Integer);
            }
            writer.Complete();
        }

        // Devuelve número de chunks creados
        return chunks.Count;
    }

    /// <summary>
    /// Borrado de un documento y sus chunks asociados.
    /// </summary>
    /// <param name="tenant"></param>
    /// <param name="sourceId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<int> DeleteDocumentAsync(string tenant, string sourceId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1) localizar ids por tenant+source_id
        const string sqlFind = @"
        SELECT id
        FROM kb_documents
        WHERE tenant_slug = @tenant AND source_id = @sourceId
        FOR UPDATE";
        var docIds = new List<long>();
        await using (var cmd = new NpgsqlCommand(sqlFind, conn, tx))
        {
            cmd.Parameters.AddWithValue("@tenant", tenant);
            cmd.Parameters.AddWithValue("@sourceId", sourceId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) docIds.Add(r.GetInt64(0));
        }
        if (docIds.Count == 0) { await tx.RollbackAsync(ct); return 0; }

        // 2) borrar chunks primero (si no tienes ON DELETE CASCADE)
        const string sqlDelChunks = @"DELETE FROM kb_chunks WHERE document_id = ANY(@docIds)";
        await using (var cmd = new NpgsqlCommand(sqlDelChunks, conn, tx))
        {
            cmd.Parameters.AddWithValue("@docIds", docIds);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 3) borrar documentos
        const string sqlDelDocs = @"DELETE FROM kb_documents WHERE id = ANY(@docIds)";
        int affected;
        await using (var cmd = new NpgsqlCommand(sqlDelDocs, conn, tx))
        {
            cmd.Parameters.AddWithValue("@docIds", docIds);
            affected = await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return affected;
    }

    /// <summary>
    /// Búsqueda de chunks relevantes en la base de conocimiento, usando búsqueda vectorial (cosine similarity).
    /// </summary>
    /// <param name="tenant"></param>
    /// <param name="query"></param>
    /// <param name="topK"></param>
    /// <param name="mustTags"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<IReadOnlyList<KbChunkHit>> SearchAsync(
     string tenant,
     string query,
     int topK,
     string[]? mustTags = null,
     string tagMatch = "any",
     CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Embed de la query (usa tu IEmbedder inyectado)
        var qvec = await _embedder.EmbedTextAsync(query, ct);

        // WHERE opcional para tags

        const string baseWhere = "WHERE d.tenant_slug = @t";
        string tagsWhere = "";

        if (mustTags is { Length: > 0 })
        {
            // "all" => requiere TODAS las tags (d.tags @> @tags)
            // "any" => basta intersección (d.tags && @tags)
            bool requireAll = string.Equals(tagMatch, "all", StringComparison.OrdinalIgnoreCase);
            tagsWhere = requireAll
                ? " AND d.tags @> @tags"
                : " AND d.tags && @tags";
        }

        // 3) Consulta: ordenamos por distancia (embedding <=> @q); score = 1 - distancia
        var sql = $@"
SELECT
    c.id AS chunk_id,
    d.id AS document_id,
    c.text,
    1 - (c.embedding <=> @q) AS score,
    d.title
FROM kb_chunks c
JOIN kb_documents d ON d.id = c.document_id
{baseWhere}{tagsWhere}
ORDER BY c.embedding <=> @q
LIMIT @k;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("t", NpgsqlDbType.Text, tenant);
        cmd.Parameters.AddWithValue("k", NpgsqlDbType.Integer, topK);
        cmd.Parameters.AddWithValue("q", new Vector(qvec)); // pgvector

        if (mustTags is { Length: > 0 })
            cmd.Parameters.AddWithValue("tags", NpgsqlDbType.Array | NpgsqlDbType.Text, mustTags);

        var hits = new List<KbChunkHit>(topK);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var chunkId = r.GetFieldValue<long>(0);
            var documentId = r.GetFieldValue<long>(1);
            var text = r.GetString(2);
            var score = r.GetFloat(3);
            var title = r.IsDBNull(4) ? null : r.GetString(4);

            hits.Add(new KbChunkHit(chunkId, documentId, text, score, title));
        }

        return hits;
    }
}
