import { useQuery } from '@tanstack/react-query'
import api from '../lib/api/axios'
import { useNavigate } from 'react-router-dom'

export function Home() {
  const navigate = useNavigate()
  const tenant = localStorage.getItem('TENANT_SLUG') || ''

  const health = useQuery({
    queryKey: ['health'],
    queryFn: async () => (await api.get('/health')).data
  })
  const ready = useQuery({
    queryKey: ['ready'],
    queryFn: async () => (await api.get('/health/ready')).data
  })
  const tenants = useQuery({
    queryKey: ['admintenants'],
    queryFn: async () => (await api.get('/admin/admintenants')).data as any[]
  })

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold">Inicio</h1>

      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
        <div className="bg-white dark:bg-neutral-900 border border-neutral-200 dark:border-neutral-800 p-4 rounded">
          <div className="text-xs text-neutral-400">Health</div>
          <pre className="text-sm">{JSON.stringify(health.data ?? {}, null, 2)}</pre>
        </div>
        <div className="bg-white dark:bg-neutral-900 border border-neutral-200 dark:border-neutral-800 p-4 rounded">
          <div className="text-xs text-neutral-400">Ready</div>
          <pre className="text-sm">{JSON.stringify(ready.data ?? {}, null, 2)}</pre>
        </div>
        <div className="bg-white dark:bg-neutral-900 border border-neutral-200 dark:border-neutral-800 p-4 rounded">
          <div className="text-xs text-neutral-400"># Tenants</div>
          <div className="text-3xl font-semibold">{Array.isArray(tenants.data) ? tenants.data.length : 0}</div>
        </div>
        <div className="bg-white dark:bg-neutral-900 border border-neutral-200 dark:border-neutral-800 p-4 rounded p-4 rounded flex flex-col justify-between">
          <div className="text-xs text-neutral-400">Tenant actual</div>
          <div className="text-lg">{tenant || '— ninguno —'}</div>
          <button
            className={`mt-3 px-3 py-2 rounded ${tenant ? 'b' : 'bg-neutral-800/50 cursor-not-allowed'}`}
            disabled={!tenant}
            onClick={() => navigate('/metrics')}
          >
            Ir a Métricas del tenant
          </button>
        </div>
      </div>
    </div>
  )
}