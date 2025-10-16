import { useQuery } from '@tanstack/react-query'
import api from '../../lib/api/axios'
import { useMemo } from 'react'
import { LineChart, Line, CartesianGrid, XAxis, YAxis, Tooltip, ResponsiveContainer } from 'recharts'

export function Metrics() {
  const tenant = localStorage.getItem('TENANT_SLUG') || import.meta.env.VITE_DEFAULT_TENANT

  const summary = useQuery({
    queryKey: ['metrics-summary', tenant],
    queryFn: async () => (await api.get('/admin/metrics/summary', { params: { tenant } })).data
  })

  const daily = useQuery({
    queryKey: ['metrics-daily', tenant],
    queryFn: async () => (await api.get('/admin/metrics/daily', { params: { tenant, days: 30 } })).data
  })

  const data = useMemo(() => {
    const items = (daily.data?.items ?? []) as any[]
    // aggregate tokens per day
    const map = new Map<string, number>()
    for (const row of items) {
      const v = (map.get(row.day) || 0) + (row.output_tokens || 0)
      map.set(row.day, v)
    }
    return Array.from(map.entries()).map(([day, value]) => ({ day, value }))
  }, [daily.data])

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold">Métricas</h1>

      <div className="grid grid-cols-3 gap-4">
        <div className="bg-neutral-900 p-4 rounded">
          <div className="text-xs text-neutral-400">Modelos</div>
          <div className="text-2xl font-semibold">{summary.data?.items?.length ?? 0}</div>
        </div>
        <div className="bg-neutral-900 p-4 rounded">
          <div className="text-xs text-neutral-400">Días (serie)</div>
          <div className="text-2xl font-semibold">{daily.data?.items?.length ?? 0}</div>
        </div>
        <div className="bg-neutral-900 p-4 rounded">
          <div className="text-xs text-neutral-400">Tenant</div>
          <div className="text-lg">{tenant}</div>
        </div>
      </div>

      <div className="bg-neutral-900 p-4 rounded h-80">
        <div className="text-sm mb-2">Output tokens por día</div>
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={data}>
            <CartesianGrid strokeDasharray="3 3" />
            <XAxis dataKey="day" />
            <YAxis />
            <Tooltip />
            <Line type="monotone" dataKey="value" />
          </LineChart>
        </ResponsiveContainer>
      </div>
    </div>
  )
}