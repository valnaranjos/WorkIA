using KommoAIAgent.Api.Middleware;
using KommoAIAgent.Knowledge;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;

namespace KommoAIAgent.Controllers;

/// <summary>
/// Controlador para administrar la base de conocimiento (KB).
/// </summary>
[ApiController]
[Route("admin/kb")]
[ServiceFilter(typeof(AdminApiKeyFilter))] // ← seguridad por X-Admin-Key
public sealed class AdminKbController : ControllerBase
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IKnowledgeStore _store;
    private readonly ILogger<AdminKbController> _logger;

    public AdminKbController(
        NpgsqlDataSource dataSource,
        IKnowledgeStore store,
        ILogger<AdminKbController> logger)
    {
        _dataSource = dataSource;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Endpoint para listar documentos en la KB, con filtros y paginación.
    /// GET /admin/kb/docs?tenant={slug}&q=texto&tags=faq,envios&page=1&pageSize=20
    /// </summary>
    /// <param name="tenant"></param>
    /// <param name="q"></param>
    /// <param name="tags"></param>
    /// <param name="page"></param>
    /// <param name="pageSize"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpGet("docs")]
    public async Task<IActionResult> ListDocs(
        [FromQuery] string tenant,
        [FromQuery] string? q,
        [FromQuery] string? tags,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenant))
            return BadRequest(new { error = "Parámetro 'tenant' es requerido" });

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (page - 1) * pageSize;

        // Parseo de tags (opcional)
        string[]? tagArray = null;
        if (!string.IsNullOrWhiteSpace(tags))
            tagArray = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Consulta: documentos + conteo de chunks, con filtros
        const string sql = @"
WITH counts AS (
  SELECT document_id, COUNT(*) AS cnt
  FROM kb_chunks
  GROUP BY document_id
),
rows AS (
  SELECT d.id, d.tenant_slug, d.source_id, d.title, d.tags,
         d.created_utc, d.updated_utc,
         COALESCE(c.cnt,0) AS chunk_count
  FROM kb_documents d
  LEFT JOIN counts c ON c.document_id = d.id
  WHERE d.tenant_slug = @tenant
    AND (@q IS NULL OR d.title ILIKE '%' || @q || '%' OR array_to_string(d.tags, ',') ILIKE '%' || @q || '%')
    AND (@tags IS NULL OR d.tags && @tags)
)
SELECT (SELECT COUNT(*) FROM rows) AS total,
       COALESCE(jsonb_agg(
         jsonb_build_object(
           'documentId', id,
           'sourceId',   source_id,
           'title',      title,
           'tags',       tags,
           'createdAt',  created_utc,
           'updatedAt',  updated_utc,
           'chunkCount', chunk_count
         )
       ), '[]'::jsonb)
FROM (
  SELECT * FROM rows
  ORDER BY updated_utc DESC
  LIMIT @limit OFFSET @offset
) t;
";
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);

        // @tenant SIEMPRE tipado
        cmd.Parameters.AddWithValue("tenant", NpgsqlTypes.NpgsqlDbType.Text, tenant);

        // @q debe estar tipado AUNQUE sea null (por el ILIKE)
        var pQ = new NpgsqlParameter("q", NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = string.IsNullOrWhiteSpace(q) ? DBNull.Value : q
        };
        cmd.Parameters.Add(pQ);

        // @tags debe estar tipado AUNQUE sea null (por el operador &&)
        // Si no hay tags, manda un NULL tipado a TEXT[]
        if (tagArray is null)
        {
            var pTags = new NpgsqlParameter("tags", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text)
            {
                Value = DBNull.Value
            };
            cmd.Parameters.Add(pTags);
        }
        else
        {
            cmd.Parameters.AddWithValue("tags", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text, tagArray);
        }

        cmd.Parameters.AddWithValue("limit", NpgsqlTypes.NpgsqlDbType.Integer, pageSize);
        cmd.Parameters.AddWithValue("offset", NpgsqlTypes.NpgsqlDbType.Integer, offset);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct))
            return Ok(new { total = 0, page, pageSize, items = Array.Empty<object>() });

        var total = rdr.IsDBNull(0) ? 0 : rdr.GetInt64(0);
        var json = rdr.IsDBNull(1) ? "[]" : rdr.GetFieldValue<string>(1);

        return Content($"{{\"total\":{total},\"page\":{page},\"pageSize\":{pageSize},\"items\":{json}}}", "application/json");
    }

    /// <summary>
    /// Endpoint para eliminar un documento de la KB por su sourceId y tenant.
    /// DELETE /admin/kb/doc/{sourceId}?tenant={slug}
    /// </summary>
    /// <param name="sourceId"></param>
    /// <param name="tenant"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpDelete("doc/{sourceId}")]
    public async Task<IActionResult> Delete(
        [FromRoute] string sourceId,
        [FromQuery] string tenant,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenant))
            return BadRequest(new { error = "Parámetro 'tenant' es requerido" });

        if (string.IsNullOrWhiteSpace(sourceId))
            return BadRequest(new { error = "Parámetro 'sourceId' es requerido" });

        var ok = await _store.DeleteDocumentAsync(tenant, sourceId, ct); // <- tu firma actual devuelve bool
        _logger.LogInformation("Admin KB delete tenant={Tenant} sourceId={Source} ok={Ok}", tenant, sourceId, ok);

        var affected = await _store.DeleteDocumentAsync(tenant, sourceId, ct);
        if (affected > 0)
            return Ok(new { deleted = affected });
        else
            return NotFound(new { error = "not found" });
    }

    
    /// <summary>
    /// Obtiene los primeros N chunks de un documento en la KB, para inspección.
    /// GET /admin/kb/doc/{sourceId}/chunks?tenant=slug&take=20
    /// </summary>
    /// <param name="sourceId"></param>
    /// <param name="tenant"></param>
    /// <param name="take"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpGet("doc/{sourceId}/chunks")]
    public async Task<IActionResult> PeekChunks(
        [FromRoute] string sourceId,
        [FromQuery] string tenant,
        [FromQuery] int take = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(sourceId))
            return BadRequest(new { error = "tenant y sourceId son requeridos" });

        take = Math.Clamp(take, 1, 200);

        const string sql = @"
SELECT c.id, c.text, c.created_utc
FROM kb_chunks c
JOIN kb_documents d ON d.id = c.document_id
WHERE d.tenant_slug=@tenant AND d.source_id=@source
ORDER BY c.id ASC
LIMIT @take;";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant", NpgsqlTypes.NpgsqlDbType.Text, tenant);
        cmd.Parameters.AddWithValue("source", NpgsqlTypes.NpgsqlDbType.Text, sourceId);
        cmd.Parameters.AddWithValue("take", NpgsqlTypes.NpgsqlDbType.Integer, take);

        var items = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var created = rdr.GetFieldValue<DateTime>(2); // timestamptz
            items.Add(new
            {
                chunkId = rdr.GetInt64(0),
                text = rdr.GetString(1),
                createdAt = created
            });
        }

        return Ok(new { count = items.Count, items });
    }
}
