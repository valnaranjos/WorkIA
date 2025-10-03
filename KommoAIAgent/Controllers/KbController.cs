using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Knowledge;
using Microsoft.AspNetCore.Mvc;

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

    [HttpPost("ingest/text")]
    public async Task<IActionResult> IngestText([FromBody] KbIngestTextDto dto, CancellationToken ct)
    {
        var doc = await _store.UpsertDocumentAsync(_tenant.CurrentTenantId.Value, dto.SourceId, dto.Title, dto.Content, dto.Tags, ct);
        var chunks = await _store.RechunkAndEmbedAsync(_tenant.CurrentTenantId.Value, doc.DocumentId, ct);
        return Ok(new { doc.DocumentId, chunks });
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int k = 5, CancellationToken ct = default)
    {
        var hits = await _store.SearchAsync(_tenant.CurrentTenantId.Value, q, k, null, ct);
        return Ok(new { count = hits.Count, hits = hits.Select(h => new { h.Score, h.Title, text = h.Text }) });
    }
}

public sealed class KbIngestTextDto
{
    public string SourceId { get; set; } = default!;
    public string? Title { get; set; }
    public string Content { get; set; } = default!;
    public string[]? Tags { get; set; }
}
