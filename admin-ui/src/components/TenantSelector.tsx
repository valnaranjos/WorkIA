import { useEffect, useMemo, useState } from 'react'
import api from '../lib/api/axios'

type Tenant = {
  id: string
  slug: string
  displayName: string
  isActive: boolean
}

export function TenantSelector() {
  const [items, setItems] = useState<Tenant[]>([])
  const [loading, setLoading] = useState(true)
  const [value, setValue] = useState('')

  useEffect(() => {
    const current = localStorage.getItem('TENANT_SLUG') || ''
    setValue(current)
    const run = async () => {
      try {
        const res = await api.get('/admin/admintenants')
        setItems(res.data || [])
      } catch (e) {
        console.error('No se pudo cargar tenants', e)
      } finally {
        setLoading(false)
      }
    }
    run()
  }, [])

  const options = useMemo(() => {
    return items
      .filter(t => t.isActive !== false)
      .map(t => ({ slug: t.slug, name: t.displayName || t.slug }))
  }, [items])

  const onChange = (slug: string) => {
    setValue(slug)
    localStorage.setItem('TENANT_SLUG', slug)
    // Notificamos y recargamos MVF
    window.dispatchEvent(new Event('tenant-changed'))
    window.location.reload()
  }

  return (
    <div className="inline-flex items-center gap-2">
      <span className="text-sm text-neutral-400">Tenant:</span>
      <select
        className="bg-neutral-900 rounded px-2 py-1 text-sm"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        disabled={loading}
        aria-label="Selecciona tenant"
      >
        <option value="">{loading ? 'Cargando...' : '— elegir —'}</option>
        {options.map(opt => (
          <option key={opt.slug} value={opt.slug}>{opt.name}</option>
        ))}
      </select>
    </div>
  )
}
