using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace KommoAIAgent.Controllers;

[ApiController]
[Route("kb")]
public sealed class KbController : ControllerBase
{
    private readonly IKnowledgeStore _store;
    private readonly ITenantContext _tenant;

    public KbController(IKnowledgeStore store, ITenantContext tenant)
    {
        _store = store;
        _tenant = tenant;
    }


    /// <summary>
    /// Agrega o actualiza un documento de texto en la base de conocimiento KB. 
    /// </summary>
    /// <param name="dto"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpPost("ingest/text")]
    public async Task<IActionResult> IngestText([FromBody] KbIngestTextDto dto, CancellationToken ct)
    {
        var doc = await _store.UpsertDocumentAsync(_tenant.CurrentTenantId.Value, dto.SourceId, dto.Title, dto.Content, dto.Tags, ct);
        var chunks = await _store.RechunkAndEmbedAsync(_tenant.CurrentTenantId.Value, doc.DocumentId, ct);
        return Ok(new { doc.DocumentId, chunks });
    }


    /// <summary>
    /// Busca en la base de conocimiento KB los k documentos más relevantes para la consulta q.
    /// </summary>
    /// <param name="q"></param>
    /// <param name="k"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
     [FromQuery] string q,
     [FromQuery] int k = 5,
     [FromQuery] string? tags = null,
     [FromQuery] string match = "any",
     CancellationToken ct = default)
    {
        var mustTags = string.IsNullOrWhiteSpace(tags)
            ? null
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var hits = await _store.SearchAsync(_tenant.Config.Slug, q, k, mustTags, match, ct);

        return Ok(new
        {
            count = hits.Count,
            hits = hits.Select(h => new { h.Score, h.Title, text = h.Text })
        });
    }


    /// <summary>
    /// Elimina un documento y sus chunks asociados.
    /// </summary>
    /// <param name="sourceId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpDelete("doc/{sourceId}")]
    public async Task<IActionResult> DeleteDoc(string sourceId, CancellationToken ct = default)
    {
        var rows = await _store.DeleteDocumentAsync(_tenant.Config.Slug, sourceId, ct);
        return rows > 0 ? NoContent() : NotFound(new { error = "document not found" });
    }
}

/// <summary>
/// DTO para ingestión de texto en la KB.
/// </summary>
public sealed class KbIngestTextDto
{
    //SourceId es un identificador único para el documento, puede ser un nombre de archivo, URL, etc.
    public string SourceId { get; set; } = default!;

    //Título opcional del documento.
    public string? Title { get; set; }
    //Contenido del documento.
    public string Content { get; set; } = default!;
    //Tags opcionales para categorizar el documento, sirven para búsqueda.
    public string[]? Tags { get; set; }
}

