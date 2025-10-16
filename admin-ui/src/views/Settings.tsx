import { useState } from 'react'

export function Settings() {
  const [apiKey, setApiKey] = useState(localStorage.getItem('ADMIN_API_KEY') || import.meta.env.VITE_ADMIN_API_KEY || '')
  const [tenant, setTenant] = useState(localStorage.getItem('TENANT_SLUG') || import.meta.env.VITE_DEFAULT_TENANT || '')

  return (
    <div className="max-w-xl space-y-4">
      <h1 className="text-xl font-semibold">Ajustes</h1>

      <div>
        <label className="block text-sm text-neutral-300 mb-1">X-Admin-Key</label>
        <input
          type="password"
          value={apiKey}
          onChange={(e)=>setApiKey(e.target.value)}
          className="w-full bg-neutral-900 px-3 py-2 rounded"
          placeholder="Tu API key administrativa"
        />
      </div>

      <div>
        <label className="block text-sm text-neutral-300 mb-1">Tenant (slug)</label>
        <input
          value={tenant}
          onChange={(e)=>setTenant(e.target.value)}
          className="w-full bg-neutral-900 px-3 py-2 rounded"
          placeholder="p.ej., serticlouddesarrollo"
        />
      </div>

      <div className="flex gap-2">
        <button
          onClick={()=>{ localStorage.setItem('ADMIN_API_KEY', apiKey); localStorage.setItem('TENANT_SLUG', tenant); alert('Guardado'); }}
          className="px-3 py-2 bg-neutral-800 rounded"
        >
          Guardar
        </button>
        <button
          onClick={()=>{ localStorage.removeItem('ADMIN_API_KEY'); localStorage.removeItem('TENANT_SLUG'); setApiKey(''); setTenant(''); }}
          className="px-3 py-2 bg-neutral-800 rounded"
        >
          Reset
        </button>
      </div>
    </div>
  )
}