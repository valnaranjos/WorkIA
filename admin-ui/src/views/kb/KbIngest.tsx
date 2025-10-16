import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ingestText } from '../../features/kb/api'

export default function KbIngest() {
  const n = useNavigate()
  const tenant = localStorage.getItem('TENANT_SLUG') || ''
  const [sourceId, setSourceId] = useState('')
  const [title, setTitle] = useState('')
  const [tags, setTags] = useState('')
  const [text, setText] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (!tenant) {
    return (
      <div>
        <h1 className="text-2xl font-semibold mb-4">KB — Ingestar texto</h1>
        <div className="p-4 rounded border border-amber-300 bg-amber-50 text-amber-800">
          Selecciona un tenant en el header para poder ingresar texto.
        </div>
      </div>
    )
  }

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSaving(true); setError(null)
    try {
      if (!sourceId) throw new Error('Falta SourceId')
      if (!title) throw new Error('Falta Título')
      if (!text || text.trim().length < 10) throw new Error('El texto es muy corto')
      await ingestText(tenant, {
        sourceId,
        title,
        tags: tags ? tags.split(',').map(t => t.trim()).filter(Boolean) : undefined,
        text
      })
      n('/kb/docs')
    } catch (e: any) {
      const msg = e?.response?.data ?? e?.message ?? 'Error al ingresar texto'
      setError(typeof msg === 'string' ? msg : JSON.stringify(msg))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="max-w-3xl">
      <h1 className="text-2xl font-semibold mb-4">KB — Ingestar texto</h1>
      {error && <div className="mb-3 text-red-600">{error}</div>}

      <form onSubmit={onSubmit} className="space-y-4">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <label className="flex flex-col gap-1">
            <span className="text-sm text-neutral-600 dark:text-neutral-300">SourceId *</span>
            <input className="bg-white border border-neutral-300 rounded px-3 py-2 dark:bg-neutral-900 dark:border-neutral-700"
              value={sourceId} onChange={(e) => setSourceId(e.target.value)} />
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-sm text-neutral-600 dark:text-neutral-300">Título *</span>
            <input className="bg-white border border-neutral-300 rounded px-3 py-2 dark:bg-neutral-900 dark:border-neutral-700"
              value={title} onChange={(e) => setTitle(e.target.value)} />
          </label>

          <label className="flex flex-col gap-1 md:col-span-2">
            <span className="text-sm text-neutral-600 dark:text-neutral-300">Tags (coma)</span>
            <input className="bg-white border border-neutral-300 rounded px-3 py-2 dark:bg-neutral-900 dark:border-neutral-700"
              value={tags} onChange={(e) => setTags(e.target.value)} placeholder="empresa, contacto, servicios" />
          </label>
        </div>

        <label className="flex flex-col gap-1">
          <span className="text-sm text-neutral-600 dark:text-neutral-300">Texto *</span>
          <textarea
            className="bg-white border border-neutral-300 rounded px-3 py-2 min-h-[220px]
                       dark:bg-neutral-900 dark:border-neutral-700"
            value={text} onChange={(e) => setText(e.target.value)}
            placeholder="Pega aquí el contenido…"
          />
        </label>

        <div className="flex gap-2">
          <button type="submit" disabled={saving}
                  className="px-4 py-2 rounded bg-neutral-800 text-white hover:bg-neutral-700
                             dark:bg-neutral-800 dark:hover:bg-neutral-700">
            {saving ? 'Guardando…' : 'Ingestar'}
          </button>
          <button type="button" className="px-4 py-2 rounded bg-neutral-200 hover:bg-neutral-300
                                           dark:bg-neutral-900 dark:hover:bg-neutral-800"
                  onClick={() => n('/kb/docs')}>
            Cancelar
          </button>
        </div>
      </form>
    </div>
  )
}
