import { useQuery } from '@tanstack/react-query'
import api from '../../lib/api/axios'

export function Logs() {
  const tenant = localStorage.getItem('TENANT_SLUG') || import.meta.env.VITE_DEFAULT_TENANT
  const { data, isLoading, error } = useQuery({
    queryKey: ['metrics-errors', tenant],
    queryFn: async () => (await api.get('/admin/metrics/errors', { params: { tenant, limit: 100 } })).data
  })

  if (isLoading) return <div>Cargando...</div>
  if (error) return <div className="text-red-400">Error al cargar</div>

  return (
    <div className="space-y-3">
      <h1 className="text-xl font-semibold">Logs (errores IA recientes)</h1>
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left border-b border-neutral-800">
            <th className="py-2">Fecha (UTC)</th>
            <th>Proveedor</th>
            <th>Modelo</th>
            <th>Operación</th>
            <th>Mensaje</th>
          </tr>
        </thead>
        <tbody>
          {data?.items?.map((it: any) => (
            <tr key={it.id} className="border-b border-neutral-900">
              <td className="py-2">{new Date(it.when_utc).toISOString()}</td>
              <td>{it.provider ?? '-'}</td>
              <td>{it.model ?? '-'}</td>
              <td>{it.operation ?? '-'}</td>
              <td className="max-w-[600px] truncate" title={it.message ?? ''}>{it.message ?? '-'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}