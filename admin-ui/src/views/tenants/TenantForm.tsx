import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  createTenantFromForm,
  getTenant,
  updateTenantFromForm,
  TenantFormModel
} from '../../features/tenants/api'

type Mode = 'create' | 'edit'

export default function TenantForm() {
  const navigate = useNavigate()
  const { slug } = useParams() as { slug?: string }
  const mode: Mode = slug ? 'edit' : 'create'

  const [model, setModel] = useState<TenantFormModel>({
    slug: '',
    displayName: '',
    isActive: true,
    kommoBaseUrl: '',
    kommoMensajeIaFieldId: 0,     // <-- requerido por el backend
    iaProvider: 'OpenAI',
    iaModel: 'gpt-4o-mini',
    monthlyTokenBudget: 500000,
    alertThresholdPct: 75,        // UI en 0..100
    systemPrompt: '',
    businessRulesText: ''
  })
  const [loading, setLoading] = useState(mode === 'edit')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const run = async () => {
      if (mode === 'edit' && slug) {
        try {
          const t = await getTenant(slug)
          // El backend devuelve alertThresholdPct en 0..1 → convertir a 0..100 para la UI
          const pct = typeof t.alertThresholdPct === 'number'
            ? Math.round(t.alertThresholdPct * 100)
            : 0
          setModel({
            slug: t.slug,
            displayName: t.displayName,
            isActive: t.isActive,
            kommoBaseUrl: t.kommoBaseUrl,
            kommoMensajeIaFieldId: 0,  // lo dejamos 0 si no lo sabemos
            iaProvider: t.iaProvider,
            iaModel: t.iaModel,
            monthlyTokenBudget: t.monthlyTokenBudget,
            alertThresholdPct: pct,
            systemPrompt: t.systemPrompt ?? '',
            businessRulesText: t.businessRulesJson ?? '' // lo mostramos como texto
          })
        } catch (e: any) {
          setError(e?.response?.data ?? 'No se pudo cargar el tenant')
        } finally {
          setLoading(false)
        }
      }
    }
    run()
  }, [mode, slug])

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      // Validaciones mínimas
      if (mode === 'create' && !model.slug) {
        throw new Error('Slug requerido (o deja que el backend lo derive)')
      }
      if (!model.displayName) throw new Error('Falta Display Name.')
      if (!model.kommoBaseUrl) throw new Error('Falta Kommo Base URL.')
      if (model.alertThresholdPct < 0 || model.alertThresholdPct > 100) {
        throw new Error('Alert Threshold debe estar entre 0 y 100.')
      }
      if (mode === 'create' && (!model.kommoMensajeIaFieldId || model.kommoMensajeIaFieldId <= 0)) {
        throw new Error('KommoMensajeIaFieldId es requerido y debe ser > 0.')
      }

      if (mode === 'create') {
        await createTenantFromForm(model)
      } else {
        await updateTenantFromForm(slug!, model)
      }
      navigate('/tenants')
    } catch (e: any) {
      const msg = e?.response?.data ?? e?.message ?? 'Error al guardar'
      setError(typeof msg === 'string' ? msg : JSON.stringify(msg))
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <div>Cargando...</div>

  return (
    <div className="max-w-3xl">
      <h1 className="text-xl font-semibold mb-4">
        {mode === 'create' ? 'Crear tenant' : `Editar ${slug}`}
      </h1>

      {error && <div className="mb-3 text-red-400">{error}</div>}

      <form onSubmit={onSubmit} className="space-y-4">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <label className="flex flex-col gap-1">
            <span className="text-sm text-neutral-400">Slug *</span>
            <input
              className="bg-neutral-900 rounded px-2 py-2"
              value={model.slug}
              onChange={(e) => setModel({ ...model, slug: e.target.value })}
              disabled={mode === 'edit'}
              placeholder="p. ej., serticlouddesarrollo"
            />
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-sm text-neutral-400">Display Name *</span>
            <input
              className="bg-neutral-900 rounded px-2 py-2"
              value={model.displayName}
              onChange={(e) => setModel({ ...model, displayName: e.target.value })}
              placeholder="Nombre visible"
            />
          </label>

          <label className="flex items-center gap-2">
            <input
              type="checkbox"
              checked={model.isActive}
              onChange={(e) => setModel({ ...model, isActive: e.target.checked })}
            />
            <span className="text-sm text-neutral-400">Activo</span>
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-sm text-neutral-400">Kommo Base URL *</span>
            <input
              className="bg-neutral-900 rounded px-2 py-2"
              value={model.kommoBaseUrl}
              onChange={(e) => setModel({ ...model, kommoBaseUrl: e.target.value })}
              placeholder="https://tuempresa.amocrm.com"
            />
          </label>

          {/* Requerido por el backend en Create */}
          <label className="flex flex-col gap-1">
            <span className="text-sm text-neutral-400">Kommo Mensaje IA Field Id *</span>
            <input
              type="number"
              min={1}
              className="bg-neutral-900 rounded px-2 py-2"
              value={model.kommoMensajeIaFieldId}
              onChange={(e) =>
                setModel({ ...model, kommoMensajeIaFieldId: Number(e.target.value) })
              }
            />
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-sm text-neutral-400">IA Provider</span>
            <input
              className="bg-neutral-900 rounded px-2 py-2"
              value={model.iaProvider}
              onChange={(e) => setModel({ ...model, iaProvider: e.target.value })}
              placeholder="OpenAI / AzureOpenAI / ..."
            />
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-sm text-neutral-400">IA Model</span>
            <input
              className="bg-neutral-900 rounded px-2 py-2"
              value={model.iaModel}
              onChange={(e) => setModel({ ...model, iaModel: e.target.value })}
              placeholder="gpt-4o-mini"
            />
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-sm text-neutral-400">Monthly Token Budget</span>
            <input
              type="number"
              className="bg-neutral-900 rounded px-2 py-2"
              value={model.monthlyTokenBudget}
              onChange={(e) =>
                setModel({ ...model, monthlyTokenBudget: Number(e.target.value) })
              }
            />
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-sm text-neutral-400">Alert Threshold %</span>
            <input
              type="number"
              min={0}
              max={100}
              step={1}
              className="bg-neutral-900 rounded px-2 py-2"
              value={model.alertThresholdPct}
              onChange={(e) =>
                setModel({ ...model, alertThresholdPct: Number(e.target.value) })
              }
            />
          </label>
        </div>

        <label className="flex flex-col gap-1">
          <span className="text-sm text-neutral-400">System Prompt</span>
          <textarea
            className="bg-neutral-900 rounded px-2 py-2 min-h-[120px]"
            value={model.systemPrompt}
            onChange={(e) => setModel({ ...model, systemPrompt: e.target.value })}
            placeholder="Instrucciones del asistente…"
          />
        </label>

        <label className="flex flex-col gap-1">
          <span className="text-sm text-neutral-400">Business Rules (JSON)</span>
          <textarea
            className="bg-neutral-900 rounded px-2 py-2 min-h-[120px]"
            value={model.businessRulesText ?? ''}
            onChange={(e) => setModel({ ...model, businessRulesText: e.target.value })}
            placeholder='{"reglas": ["..."]}'
          />
        </label>

        <div className="flex gap-2">
          <button
            type="submit"
            disabled={saving}
            className="bg-neutral-800 hover:bg-neutral-700 px-4 py-2 rounded"
          >
            {saving ? 'Guardando…' : (mode === 'create' ? 'Crear' : 'Guardar')}
          </button>
          <button
            type="button"
            className="bg-neutral-900 px-4 py-2 rounded"
            onClick={() => navigate('/tenants')}
          >
            Cancelar
          </button>
        </div>
      </form>
    </div>
  )
}
