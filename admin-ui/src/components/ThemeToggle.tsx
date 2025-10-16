import { useEffect, useState } from 'react'
import { getTheme, toggleTheme } from '../theme/theme'

export function ThemeToggle() {
  const [theme, setTheme] = useState(getTheme())
  useEffect(() => { setTheme(getTheme()) }, [])

  return (
    <button
      className="text-sm px-2 py-1 rounded border border-neutral-200 dark:border-neutral-700
                 bg-white hover:bg-neutral-50 dark:bg-neutral-900 dark:hover:bg-neutral-800"
      title={theme === 'dark' ? 'Cambiar a claro' : 'Cambiar a oscuro'}
      onClick={() => setTheme(toggleTheme())}
    >
      {theme === 'dark' ? '🌞 Claro' : '🌙 Oscuro'}
    </button>
  )
}
