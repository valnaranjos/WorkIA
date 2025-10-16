import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { getDocChunks } from '../../features/kb/api'

type Chunk = { idx: number; text: string } | string

export default function KbDocDetail() {
  const { id } = useParams() as { id?: string }
  const navigate = useNavigate()
  const tenant = localStorage.getItem('TENANT_SLUG') || ''
  const [chunks, setChunks] = useState<Chunk[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const run = async () => {
      if (!tenant || !id) return
      try {
        const data = await getDocChunks(tenant, id)

        // Normalizamos: el backend puede devolver [] ó {chunks: []} ó {items: []}
        let arr: any = []
        if (Array.isArray(data)) arr = data
        else if (data && Array.isArray(data.chunks)) arr = data.chunks
        else if (data && Array.isArray(data.items)) arr = data.items

        // Algunos backends devuelven strings directamente por chunk
        // Aseguramos {idx, text}
        const norm: Chunk[] = arr.map((c: any, i: number) => {
          if (typeof c === 'string') return { idx: i, text: c }
          if (typeof c?.text === 'string') return { idx: c.idx ?? i, text: c.text }
          return { idx: i, text: String(c ?? '') }
        })

        setChunks(norm)
      } catch (e: any) {
        const msg = e?.response?.data ?? e?.message ?? 'Error al cargar chunks'
        setError(typeof msg === 'string' ? msg : JSON.stringify(msg))
      }
    }
    run()
  }, [id, tenant])

  if (!tenant) {
    return (
      <div className="p-4 rounded border border-amber-300 bg-amber-50 text-amber-800">
        Selecciona un tenant en el header.
      </div>
    )
  }

  if (!id) {
    return (
      <div className="p-4 rounded border border-red-200 bg-red-50 text-red-700">
        Falta el identificador del documento (sourceId).
      </div>
    )
  }

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">KB — Chunks del documento</h1>
        <button
          className="px-3 py-2 rounded bg-neutral-200 hover:bg-neutral-300
                     dark:bg-neutral-900 dark:hover:bg-neutral-800"
          onClick={() => navigate('/kb/docs')}
        >
          Volver
        </button>
      </div>

      {error && <div className="mb-3 text-red-600">{error}</div>}

      {!chunks ? (
        <div>Cargando…</div>
      ) : chunks.length === 0 ? (
        <div className="text-neutral-500">Sin chunks.</div>
      ) : (
        <div className="space-y-3">
          {chunks.map((c: any) => (
            <div key={c.idx} className="p-3 rounded border border-neutral-200 bg-white
                                       dark:bg-neutral-900 dark:border-neutral-800">
              <div className="text-xs text-neutral-500 mb-1">Chunk #{c.idx}</div>
              <div className="whitespace-pre-wrap">{c.text}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
