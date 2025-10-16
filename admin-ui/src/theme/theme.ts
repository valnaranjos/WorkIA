export type Theme = 'light' | 'dark'
const KEY = 'THEME'

export function getTheme(): Theme {
  const t = (localStorage.getItem(KEY) as Theme) || 'light'
  return t === 'dark' ? 'dark' : 'light'
}

export function applyTheme(t: Theme) {
  const html = document.documentElement
  html.classList.remove('light', 'dark')
  html.classList.add(t)
  localStorage.setItem(KEY, t)
}

export function toggleTheme(): Theme {
  const next: Theme = getTheme() === 'dark' ? 'light' : 'dark'
  applyTheme(next)
  return next
}

export function initTheme() {
  applyTheme(getTheme())
}
