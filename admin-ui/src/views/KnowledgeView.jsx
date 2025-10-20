import React, { useEffect, useMemo, useState, useCallback } from "react";
import api from "../lib/api";
import { toast } from "../components/Toaster";
import Modal from "../components/Modal";

/**
 * Knowledge Base View (CORREGIDO)
 * - Selector de tenant (global)
 * - Lista de documentos con búsqueda, paginado y acciones
 * - Ver chunks (modal) por sourceId con búsqueda/paginado
 * - Ingesta de texto (1 doc)
 * - Batch (CSV/JSONL) que llama a ingestText varias veces
 * - Clases de Tailwind corregidas
 */

export default function KnowledgeView() {
  // ===== Tenants / filtros =====
  const [tenants, setTenants] = useState([]);
  const [slug, setSlug] = useState("");
  const [query, setQuery] = useState("");
  const [pageSize, setPageSize] = useState(20);
  const [page, setPage] = useState(1);

  // ===== Datos =====
  const [loading, setLoading] = useState(false);
  const [docs, setDocs] = useState([]);
  const [total, setTotal] = useState(0);

  // ===== Modal: Ver chunks =====
  const [chunksOpen, setChunksOpen] = useState(false);
  const [chunksTitle, setChunksTitle] = useState("");
  const [chunksSourceId, setChunksSourceId] = useState("");
  const [chunksItems, setChunksItems] = useState([]);
  const [chunksTotal, setChunksTotal] = useState(0);
  const [chunksSize, setChunksSize] = useState(20);
  const [chunksLoading, setChunksLoading] = useState(false);

  // ===== Modal: Ingesta 1 =====
  const [ingOpen, setIngOpen] = useState(false);
  const [ingTenant, setIngTenant] = useState("");
  const [ingTitle, setIngTitle] = useState("");
  const [ingTags, setIngTags] = useState("");
  const [ingText, setIngText] = useState("");
  const [ingSaving, setIngSaving] = useState(false);

  // ===== Modal: Batch =====
  const [batchOpen, setBatchOpen] = useState(false);
  const [batchTenant, setBatchTenant] = useState("");
  const [batchRaw, setBatchRaw] = useState("");
  const [batchParsingErr, setBatchParsingErr] = useState("");
  const [batchRows, setBatchRows] = useState([]);
  const [batchRunning, setBatchRunning] = useState(false);
  const [batchDone, setBatchDone] = useState(0);

  // ===== Tenants =====
  useEffect(() => {
    let mounted = true;
    (async () => {
      try {
        const list = await api.getTenants();
        if (!mounted) return;
        setTenants(list || []);
        if ((list || []).length && !slug) setSlug(list[0].slug);
      } catch (e) {
        toast("error", "No se pudo cargar tenants: " + (e?.message || e));
      }
    })();
    return () => { mounted = false; };
  }, [slug]);

  // ===== Cargar documentos (CORREGIDO: parámetros correctos) =====
  const load = useCallback(async () => {
    if (!slug) return;
    setLoading(true);
    try {
      const res = await api.getKbDocs({ tenant: slug, q: query, page, pageSize });
      setDocs(res?.items || []);
      setTotal(res?.total || 0);
    } catch (e) {
      const errMsg = e?.message || String(e);
      // Si el endpoint no existe, muestra mensaje más claro
      if (errMsg.includes('not defined') || errMsg.includes('404')) {
        toast("error", "⚠️ El endpoint /admin/kb/docs no existe en el backend. Verifica la API.");
      } else {
        toast("error", "No se pudo cargar KB: " + errMsg);
      }
      console.error("Error cargando KB:", e);
    } finally {
      setLoading(false);
    }
  }, [slug, query, page, pageSize]);

  useEffect(() => { load(); }, [load]);

  // ===== Abrir / cargar chunks (CORREGIDO: endpoint real sin paginación) =====
  const openChunksBySourceId = async (sourceId, title) => {
    setChunksTitle(title || "");
    setChunksSourceId(sourceId);
    setChunksOpen(true);
    await loadChunks({ sourceId, take: chunksSize });
  };

  const loadChunks = useCallback(async ({ sourceId, take }) => {
    if (!slug || !sourceId) return;
    setChunksLoading(true);
    try {
      // Backend solo tiene "take", no paginación completa
      const res = await api.getKbChunks({
        tenant: slug,
        sourceId,
        take: take || 20,
      });
      setChunksItems(res?.items || []);
      setChunksTotal(res?.count || 0);
    } catch (e) {
      toast("error", "No se pudieron cargar los chunks: " + (e?.message || e));
    } finally {
      setChunksLoading(false);
    }
  }, [slug]);

  // ===== Eliminar doc (CORREGIDO: usa sourceId) =====
  const removeDoc = async (sourceId) => {
    if (!sourceId) return;
    if (!window.confirm("¿Eliminar este documento?")) return;
    try {
      await api.kbDelete({ tenant: slug, sourceId });
      toast("success", "✅ Documento eliminado");
      await load();
    } catch (e) {
      toast("error", "❌No se pudo eliminar: " + (e?.message || e));
    }
  };

  // ===== Nuevo (1 doc) =====
  const openNewDoc = () => {
    setIngTenant(slug);
    setIngTitle("");
    setIngTags("");
    setIngText("");
    setIngOpen(true);
  };

  const submitIngestText = async () => {
    const targetTenant = ingTenant || slug;
    if (!targetTenant) return toast("error", "Selecciona un tenant");
    if (!ingText.trim()) return toast("error", "Escribe algún texto");
    setIngSaving(true);
    try {
      const tagsArray = ingTags
        ? ingTags.split(",").map(s => s.trim()).filter(Boolean)
        : [];
      await api.ingestText(targetTenant, {
        title: ingTitle || undefined,
        text: ingText,
        tags: tagsArray
      });
      toast("success", "Documento ingresado");
      setIngOpen(false);
      await load();
    } catch (e) {
      toast("error", "No se pudo ingresar: " + (e?.message || e));
    } finally {
      setIngSaving(false);
    }
  };

  // ===== Batch =====
  const openBatch = () => {
    setBatchTenant(slug);
    setBatchRaw("");
    setBatchParsingErr("");
    setBatchRows([]);
    setBatchDone(0);
    setBatchOpen(true);
  };

  const parseBatch = useCallback((raw) => {
    const src = (raw || "").trim();
    if (!src) return { rows: [] };
    const maybeJsonl = src.split("\n").slice(0, 3).every(line => {
      const t = line.trim();
      return !t || t.startsWith("{") || t.startsWith("[") || t.startsWith("\"");
    });
    if (maybeJsonl) {
      const rows = [];
      const lines = src.split("\n");
      for (let i = 0; i < lines.length; i++) {
        const t = lines[i].trim();
        if (!t) continue;
        try {
          const obj = JSON.parse(t);
          if (!obj.text) throw new Error("Falta 'text'");
          rows.push({
            title: obj.title || "",
            tags: Array.isArray(obj.tags) ? obj.tags.join(",") : (obj.tags || ""),
            text: String(obj.text || "")
          });
        } catch (e) {
          return { error: `JSONL línea ${i + 1}: ${e.message}` };
        }
      }
      return { rows };
    }
    // CSV
    const lines = src.split("\n").filter(l => l.trim());
    if (!lines.length) return { rows: [] };
    const header = lines[0].split(",").map(h => h.trim().toLowerCase());
    const ti = header.indexOf("title");
    const tg = header.indexOf("tags");
    const tx = header.indexOf("text");
    if (ti === -1 || tg === -1 || tx === -1) {
      return { error: "CSV inválido. Encabezado esperado: title,tags,text" };
    }
    const rows = [];
    for (let i = 1; i < lines.length; i++) {
      const cols = splitCsvLine(lines[i]);
      rows.push({
        title: (cols[ti] || "").trim(),
        tags: (cols[tg] || "").trim(),
        text: (cols[tx] || "").trim(),
      });
    }
    return { rows };
  }, []);

  function splitCsvLine(line) {
    const out = [];
    let cur = "";
    let inQ = false;
    for (let i = 0; i < line.length; i++) {
      const ch = line[i];
      if (ch === '"') {
        if (inQ && line[i + 1] === '"') { cur += '"'; i++; }
        else inQ = !inQ;
      } else if (ch === "," && !inQ) {
        out.push(cur); cur = "";
      } else {
        cur += ch;
      }
    }
    out.push(cur);
    return out;
  }

  useEffect(() => {
    if (!batchOpen) return;
    const r = parseBatch(batchRaw);
    if (r.error) {
      setBatchParsingErr(r.error);
      setBatchRows([]);
    } else {
      setBatchParsingErr("");
      setBatchRows(r.rows || []);
    }
  }, [batchRaw, batchOpen, parseBatch]);

  const runBatch = async () => {
    if (!batchTenant) return toast("error", "Selecciona un tenant");
    if (!batchRows.length) return toast("error", "No hay filas válidas");
    setBatchRunning(true);
    setBatchDone(0);
    try {
      const items = batchRows.map(r => ({ title: r.title, tags: r.tags, text: r.text }));
      const res = await api.ingestBatchText(items, {
        slug: batchTenant,
        concurrency: 3,
        onProgress: (d) => setBatchDone(d),
      });
      const errsByIdx = new Map(res.errors.map(e => [e.idx, e.error]));
      setBatchRows(prev => prev.map((r, i) => ({ ...r, status: errsByIdx.has(i) ? `error: ${errsByIdx.get(i)}` : "ok" })));
      toast("success", `Batch: ${res.inserted}/${res.total} insertados`);
      await load();
    } catch (e) {
      toast("error", "Fallo el batch: " + (e?.message || e));
    } finally {
      setBatchRunning(false);
    }
  };

  // ===== Render =====
  return (
    <div className="p-4 space-y-4">
      {/* Filtros */}
      <div className="flex flex-wrap items-center gap-3">
        <select
          value={slug}
          onChange={e => { setSlug(e.target.value); setPage(1); }}
          className="border rounded px-3 py-2"
          aria-label="Seleccionar tenant"
        >
          {tenants.map(t => (
            <option key={t.slug} value={t.slug}>{t.slug} — {t.displayName}</option>
          ))}
        </select>

        <input
          value={query}
          onChange={e => { setQuery(e.target.value); setPage(1); }}
          placeholder="Buscar por título, url, tags…"
          className="border rounded px-3 py-2 w-80"
        />

        <select
          value={pageSize}
          onChange={e => { setPageSize(Number(e.target.value)); setPage(1); }}
          className="border rounded px-3 py-2"
          aria-label="Por página"
        >
          {[10, 20, 50].map(n => <option key={n} value={n}>{n} / página</option>)}
        </select>

        <button type="button" onClick={load} className="px-3 py-2 rounded bg-blue-600 text-white hover:bg-blue-700">
          Buscar
        </button>

        <div className="ml-auto flex gap-2">
          <button type="button" onClick={openBatch} className="px-3 py-2 rounded bg-emerald-600 text-white hover:bg-emerald-700">
            + Batch
          </button>
          <button type="button" onClick={openNewDoc} className="px-3 py-2 rounded bg-blue-600 text-white hover:bg-blue-700">
            + Nuevo documento
          </button>
        </div>
      </div>

      {/* Tabla */}
      <div className="border rounded overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-gray-50">
            <tr>
              <Th>Fuente</Th>
              <Th>Título / URL</Th>
              <Th>Tags</Th>
              <Th>Fecha</Th>
              <Th align="right">Acciones</Th>
            </tr>
          </thead>
          <tbody>
            {!loading && docs.length === 0 && (
              <tr><td className="p-4 text-gray-500" colSpan={5}>Sin resultados.</td></tr>
            )}
            {docs.map(d => (
              <tr key={d.id || d.sourceId} className="border-t">
                <Td>{d.source || "doc"}</Td>
                <Td>{d.title || d.url || "-"}</Td>
                <Td>{(d.tags || []).join(", ")}</Td>
                <Td>{d.createdAt?.slice?.(0, 19) || ""}</Td>
                <Td align="right">
                  <button className="text-blue-600 hover:underline mr-3" onClick={() => openChunksBySourceId(d.sourceId, d.title)}>
                    Ver chunks
                  </button>
                  <button className="text-red-600 hover:underline" onClick={() => removeDoc(d.sourceId)}>
                    Eliminar
                  </button>
                </Td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Paginación */}
      <div className="flex items-center justify-between text-sm">
        <div>Total: {total}</div>
        <div className="flex gap-2">
          <button className="px-3 py-1 border rounded disabled:opacity-50" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}>«</button>
          <div className="px-2 py-1">Página {page}</div>
          <button className="px-3 py-1 border rounded disabled:opacity-50" onClick={() => setPage(p => p + 1)} disabled={docs.length < pageSize}>»</button>
        </div>
      </div>

      {/* Modal Chunks (SIMPLIFICADO: sin búsqueda/paginación, solo "take") */}
      <Modal title={`Chunks — ${chunksTitle || ""}`} open={chunksOpen} onClose={() => setChunksOpen(false)}>
        <div className="space-y-3">
          <div className="flex items-center gap-2">
            <select
              value={chunksSize}
              onChange={e => { 
                const s = Number(e.target.value); 
                setChunksSize(s); 
                loadChunks({ sourceId: chunksSourceId, take: s }); 
              }}
              className="border rounded px-3 py-2"
              aria-label="Cantidad de chunks"
            >
              {[10, 20, 50, 100, 200].map(n => <option key={n} value={n}>Mostrar {n} chunks</option>)}
            </select>
            <button
              className="px-3 py-2 rounded bg-blue-600 text-white hover:bg-blue-700"
              onClick={() => loadChunks({ sourceId: chunksSourceId, take: chunksSize })}
            >
              Actualizar
            </button>
          </div>

          <div className="border rounded max-h-[60vh] overflow-auto">
            <table className="w-full text-sm">
              <thead className="bg-gray-50">
                <tr><Th>#</Th><Th>Texto</Th><Th>Creado</Th></tr>
              </thead>
              <tbody>
                {!chunksLoading && chunksItems.length === 0 && (
                  <tr><td className="p-3 text-gray-500" colSpan={3}>Sin datos.</td></tr>
                )}
                {chunksItems.map((c, i) => (
                  <tr key={`ck-${c.chunkId || i}`} className="border-t">
                    <Td>{i + 1}</Td>
                    <Td className="max-w-lg break-words">{c.text || ""}</Td>
                    <Td>{c.createdAt ? new Date(c.createdAt).toLocaleString() : "-"}</Td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="flex items-center justify-between text-sm">
            <div>Total mostrado: {chunksItems.length}</div>
            <div className="text-gray-500">
              {chunksTotal > chunksSize && `(mostrando ${chunksSize} de ${chunksTotal})`}
            </div>
          </div>
        </div>
      </Modal>

      {/* Modal Nuevo */}
      <Modal title="Nuevo documento (KB)" open={ingOpen} onClose={() => setIngOpen(false)}>
        <div className="space-y-3">
          <select value={ingTenant} onChange={e => setIngTenant(e.target.value)} className="w-full border rounded px-3 py-2">
            {tenants.map(t => <option key={t.slug} value={t.slug}>{t.slug} — {t.displayName}</option>)}
          </select>
          <input value={ingTitle} onChange={e => setIngTitle(e.target.value)} className="w-full border rounded px-3 py-2" placeholder="Título (opcional)" />
          <input value={ingTags} onChange={e => setIngTags(e.target.value)} className="w-full border rounded px-3 py-2" placeholder="Tags (coma separadas)" />
          <textarea value={ingText} onChange={e => setIngText(e.target.value)} className="w-full border rounded px-3 py-2 h-60" placeholder="Pega el texto…" />
          <div className="flex justify-end gap-2 pt-2">
            <button className="px-4 py-2 border rounded" onClick={() => setIngOpen(false)}>Cancelar</button>
            <button className="px-4 py-2 rounded bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50" onClick={submitIngestText} disabled={ingSaving}>
              {ingSaving ? "Guardando…" : "Ingresar a KB"}
            </button>
          </div>
        </div>
      </Modal>

      {/* Modal Batch */}
      <Modal title="Batch de documentos (CSV o JSONL)" open={batchOpen} onClose={() => setBatchOpen(false)}>
        <div className="space-y-3">
          <select value={batchTenant} onChange={e => setBatchTenant(e.target.value)} className="w-full border rounded px-3 py-2">
            {tenants.map(t => <option key={t.slug} value={t.slug}>{t.slug} — {t.displayName}</option>)}
          </select>
          <div className="text-xs text-gray-600">
            <p className="mb-1">Formatos:</p>
            <ul className="list-disc ml-5">
              <li><b>CSV</b>: encabezado <code>title,tags,text</code></li>
              <li><b>JSONL</b>: una línea por JSON <code>{'{ "title":"...", "tags":"a,b", "text":"..." }'}</code></li>
            </ul>
          </div>
          <textarea
            value={batchRaw}
            onChange={e => setBatchRaw(e.target.value)}
            className="w-full border rounded px-3 py-2 h-60"
            placeholder={`CSV:
title,tags,text
"Mi título","tag1,tag2","Contenido..."

JSONL:
{"title":"Mi doc","tags":"tag1,tag2","text":"..."}`}
          />
          {batchParsingErr && <div className="text-red-600 text-sm">{batchParsingErr}</div>}
          {!batchParsingErr && !!batchRows.length && (
            <div className="text-xs text-gray-600">Filas válidas: {batchRows.length}. {batchRunning ? `Progreso ${batchDone}/${batchRows.length}` : ""}</div>
          )}
          {!batchParsingErr && !!batchRows.length && (
            <div className="border rounded max-h-40 overflow-auto">
              <table className="w-full text-xs">
                <thead className="bg-gray-50"><tr><Th>#</Th><Th>Título</Th><Th>Tags</Th><Th>Texto (inicio)</Th><Th>Estado</Th></tr></thead>
                <tbody>
                  {batchRows.map((r, i) => (
                    <tr key={`br-${i}`} className="border-t">
                      <Td>{i + 1}</Td>
                      <Td>{r.title || "-"}</Td>
                      <Td>{r.tags || "-"}</Td>
                      <Td>{(r.text || "").slice(0, 60)}{(r.text || "").length > 60 ? "…" : ""}</Td>
                      <Td>{r.status || "-"}</Td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          <div className="flex justify-end gap-2 pt-2">
            <button className="px-4 py-2 border rounded" onClick={() => setBatchOpen(false)} disabled={batchRunning}>Cancelar</button>
            <button className="px-4 py-2 rounded bg-emerald-600 text-white hover:bg-emerald-700 disabled:opacity-50" onClick={runBatch} disabled={batchRunning || !!batchParsingErr || !batchRows.length}>
              {batchRunning ? "Procesando…" : "Cargar batch"}
            </button>
          </div>
        </div>
      </Modal>
    </div>
  );
}

/* ---------- helpers UI (CORREGIDO: clases completas) ---------- */
function Th({ children, align = "left" }) {
  const alignClass = align === "right" ? "text-right" : align === "center" ? "text-center" : "text-left";
  return <th className={`p-3 ${alignClass}`}><span className="font-medium text-gray-700">{children}</span></th>;
}

function Td({ children, align = "left" }) {
  const alignClass = align === "right" ? "text-right" : align === "center" ? "text-center" : "text-left";
  return <td className={`p-3 ${alignClass}`}>{children}</td>;
}