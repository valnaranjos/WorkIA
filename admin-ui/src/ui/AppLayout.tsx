import { NavLink, Outlet, useNavigate, useSearchParams } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { TenantSelector } from '../components/TenantSelector'
import { ThemeToggle } from '../components/ThemeToggle'

function NavItem({ to, label }: { to: string; label: string }) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        `block px-3 py-2 rounded-lg text-sm
         ${isActive
           ? 'bg-neutral-200 text-gray-900 dark:bg-neutral-800 dark:text-neutral-100'
           : 'hover:bg-neutral-100 text-gray-700 dark:hover:bg-neutral-900 dark:text-neutral-200'}`
      }
    >
      {label}
    </NavLink>
  )
}

export function AppLayout() {
  const navigate = useNavigate()
  const [ready, setReady] = useState(false)
  const [params] = useSearchParams()

  useEffect(() => {
    const tFromUrl = params.get('tenant')
    if (tFromUrl) localStorage.setItem('TENANT_SLUG', tFromUrl)
    const apiKey = localStorage.getItem('ADMIN_API_KEY') || import.meta.env.VITE_ADMIN_API_KEY
    if (!apiKey) navigate('/settings')
    setReady(true)
  }, [navigate, params])

  if (!ready) return null

  const tenant = localStorage.getItem('TENANT_SLUG') || ''

  return (
    <div className="min-h-screen flex flex-col
                    bg-gray-50 text-gray-900 dark:bg-neutral-950 dark:text-neutral-100">

      {/* Header */}
      <header className="h-12 flex items-center justify-between px-4
                   bg-white border-b border-neutral-200
                   dark:bg-neutral-950 dark:border-neutral-800">
  <div className="text-sm font-medium">KommoAIAgent Admin</div>

  <div className="flex items-center gap-3">
    <ThemeToggle />
    <div className="flex items-center gap-2">
      <span className="text-sm text-neutral-500 dark:text-neutral-300">Tenant:</span>
      <TenantSelector />
    </div>
    <NavLink
      to="/settings"
      className="text-sm text-neutral-500 hover:text-neutral-800
                 dark:text-neutral-300 dark:hover:text-neutral-100"
    >
      Ajustes
    </NavLink>
  </div>
</header>


      {/* Body: sidebar + main */}
      <div className="flex flex-1 min-h-0">
        {/* Sidebar ancho fijo */}
        <aside
          className="w-60 shrink-0 border-r p-4 overflow-y-auto
                     bg-white border-neutral-200
                     dark:bg-neutral-950 dark:border-neutral-800"
        >
          <nav className="space-y-1">
            <NavItem to="/" label="Inicio" />
            <NavItem to="/tenants" label="Tenants" />
            <NavItem to="/kb/docs" label="KB Admin" />
            <NavItem to="/metrics" label="Métricas" />
            <NavItem to="/logs" label="Logs" />
            <NavItem to="/settings" label="Ajustes" />
          </nav>

          {!tenant && (
            <div className="mt-4 text-xs text-amber-600 dark:text-amber-300">
              No hay tenant seleccionado. Elige uno arriba para ver datos específicos.
            </div>
          )}
        </aside>

        {/* Main */}
        <main className="flex-1 p-6 overflow-auto">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
