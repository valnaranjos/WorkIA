import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { activateTenant, deleteTenant, listTenants, TenantDto } from '../../features/tenants/api'

export function TenantsList() {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const { data, isLoading, error } = useQuery<TenantDto[]>({
    queryKey: ['admintenants'],
    queryFn: listTenants,
  })

  const refresh = () => qc.invalidateQueries({ queryKey: ['admintenants'] })

  if (isLoading) return <div>Cargando…</div>
  if (error) return <div className="text-red-400">Error: {(error as any)?.message ?? 'error'}</div>

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">Tenants</h1>
        <button
          onClick={() => navigate('/tenants/new')}
          className="bg-neutral-800 hover:bg-neutral-700 px-3 py-2 rounded"
        >
          + Nuevo
        </button>
      </div>

      <table className="w-full text-sm">
        <thead className="text-neutral-400">
          <tr className="text-left">
            <th className="py-2">Slug</th>
            <th className="py-2">Nombre</th>
            <th className="py-2">Activo</th>
            <th className="py-2">Acciones</th>
          </tr>
        </thead>
        <tbody>
          {data?.map(t => (
            <tr key={t.slug} className="border-t border-neutral-800">
              <td className="py-2">{t.slug}</td>
              <td className="py-2">{t.displayName}</td>
              <td className="py-2">{t.isActive ? 'Sí' : 'No'}</td>
              <td className="py-2 flex gap-2">
                <Link to={`/tenants/edit/${t.slug}`} className="px-2 py-1 rounded bg-neutral-800 hover:bg-neutral-700">Editar</Link>
                {!t.isActive && (
                  <button
                    className="px-2 py-1 rounded bg-neutral-800 hover:bg-neutral-700"
                    onClick={async () => { await activateTenant(t.slug); refresh() }}
                  >
                    Activar
                  </button>
                )}
                <button
                  className="px-2 py-1 rounded bg-red-900 hover:bg-red-800"
                  onClick={async () => {
                    if (!confirm(`¿Eliminar (soft-delete) ${t.slug}?`)) return
                    await deleteTenant(t.slug)
                    refresh()
                  }}
                >
                  Desactivar
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
