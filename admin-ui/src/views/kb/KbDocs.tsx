import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { listDocs, search, KbDoc } from '../../features/kb/api'

function useDebounced<T>(value: T, delay = 400) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = setTimeout(() => setV(value), delay)
    return () => clearTimeout(id)
  }, [value, delay])
  return v
}

export default function KbDocs() {
  const tenant = localStorage.getItem('TENANT_SLUG') || ''

  // UI state
  const [q, setQ] = useState('')
  const dq = useDebounced(q, 400)
  const [page, setPage] = useState(1)
  const [pageSize] = useState(20)

  // data state
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [items, setItems] = useState<KbDoc[]>([])
  const [total, setTotal] = useState(0)

  const totalPages = useMemo(
    () => Math.max(1, Math.ceil(total / pageSize)),
    [total, pageSize]
  )

  useEffect(() => {
    // si no hay tenant, no hacemos requests
    if (!tenant) return
    const run = async () => {
      setLoading(true); setError(null)
      try {
        if (dq && dq.trim().length > 0) {
          // búsqueda
          const res = await search(tenant, dq, page, pageSize)
          const docs: KbDoc[] = res.items.map((r) => ({
            id: r.id,
            sourceId: r.sourceId,
            title: r.title,
            tags: [],
            chunks: undefined,
          }))
          setItems(docs)
          setTotal(docs.length) // si /kb/search no trae total
        } else {
          // listado normal paginado
          const res = await listDocs(tenant, page, pageSize)
          setItems(res.items)
          setTotal(res.total)
        }
      } catch (e: any) {
        const msg = e?.response?.data ?? e?.message ?? 'Error al cargar documentos'
        setError(typeof msg === 'string' ? msg : JSON.stringify(msg))
      } finally {
        setLoading(false)
      }
    }
    run()
  }, [tenant, dq, page, pageSize])

  if (!tenant) {
    return (
      <div>
        <div className="flex items-center justify-between mb-4">
          <h1 className="text-2xl font-semibold">KB — Documentos</h1>
          <Link
            to="/kb/ingest"
            className="px-3 py-2 rounded bg-neutral-800 text-white hover:bg-neutral-700
                       dark:bg-neutral-800 dark:hover:bg-neutral-700"
          >
            + Ingestar texto
          </Link>
        </div>
        <div className="p-4 rounded border border-amber-300 bg-amber-50 text-amber-800">
          Selecciona un tenant en el header para ver los documentos.
        </div>
      </div>
    )
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-semibold">KB — Documentos</h1>
        <Link
          to="/kb/ingest"
          className="px-3 py-2 rounded bg-neutral-800 text-white hover:bg-neutral-700
                     dark:bg-neutral-800 dark:hover:bg-neutral-700"
        >
          + Ingestar texto
        </Link>
      </div>

      {/* Buscador */}
      <div className="flex gap-2 mb-4">
        <input
          value={q}
          onChange={(e) => { setQ(e.target.value); setPage(1) }}
          placeholder="Buscar por título o tag…"
          className="bg-white border border-neutral-300 rounded px-3 py-2 w-full
                     dark:bg-neutral-900 dark:border-neutral-700"
        />
        <button
          onClick={() => setPage(1)}
          className="px-3 py-2 rounded bg-neutral-200 hover:bg-neutral-300
                     dark:bg-neutral-900 dark:hover:bg-neutral-800"
        >
          Buscar
        </button>
      </div>

      {error && <div className="mb-3 text-red-600">{error}</div>}
      {loading && <div className="mb-3">Cargando…</div>}

      {/* Tabla */}
      <div className="overflow-x-auto border border-neutral-200 rounded
                      dark:border-neutral-800 bg-white dark:bg-neutral-900">
        <table className="min-w-full text-sm">
          <thead>
            <tr className="border-b border-neutral-200 dark:border-neutral-800">
              <th className="text-left p-3">SourceId</th>
              <th className="text-left p-3">Título</th>
              <th className="text-left p-3">Tags</th>
              <th className="text-left p-3">Chunks</th>
              <th className="text-left p-3">Acciones</th>
            </tr>
          </thead>
          <tbody>
            {items.length === 0 ? (
              <tr>
                <td className="p-3 text-neutral-500" colSpan={5}>
                  {loading ? 'Cargando…' : 'Sin resultados.'}
                </td>
              </tr>
            ) : items.map((d) => (
              <tr key={d.sourceId || d.id} className="border-t border-neutral-100 dark:border-neutral-800">
                <td className="p-3">{d.sourceId}</td>
                <td className="p-3">{d.title}</td>
                <td className="p-3">
                  {Array.isArray(d.tags) && d.tags.length > 0 ? d.tags.join(', ') : '—'}
                </td>
                <td className="p-3">{d.chunks ?? '—'}</td>
                <td className="p-3">
                  {d.sourceId ? (
                    <Link
                      to={`/kb/doc/${encodeURIComponent(d.sourceId)}`}
                      className="px-3 py-1 rounded bg-neutral-200 hover:bg-neutral-300
                                 dark:bg-neutral-900 dark:hover:bg-neutral-800"
                    >
                      Ver chunks
                    </Link>
                  ) : (
                    <span className="text-neutral-400">—</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="border-t border-neutral-200 dark:border-neutral-800">
              <td className="p-3 text-sm text-neutral-500" colSpan={5}>
                Total: {total}
              </td>
            </tr>
          </tfoot>
        </table>
      </div>

      {/* Paginación simple */}
      <div className="flex items-center gap-2 mt-3">
        <button
          disabled={page <= 1}
          onClick={() => setPage(p => Math.max(1, p - 1))}
          className="px-3 py-2 rounded disabled:opacity-40
                     bg-neutral-200 hover:bg-neutral-300
                     dark:bg-neutral-900 dark:hover:bg-neutral-800"
        >
          Prev
        </button>
        <span className="text-sm text-neutral-500">
          Página {page} de {totalPages}
        </span>
        <button
          disabled={page >= totalPages}
          onClick={() => setPage(p => Math.min(totalPages, p + 1))}
          className="px-3 py-2 rounded disabled:opacity-40
                     bg-neutral-200 hover:bg-neutral-300
                     dark:bg-neutral-900 dark:hover:bg-neutral-800"
        >
          Next
        </button>
      </div>
    </div>
  )
}
