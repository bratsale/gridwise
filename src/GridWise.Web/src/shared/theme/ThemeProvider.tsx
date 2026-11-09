import { useEffect, useState, type ReactNode } from 'react'
import { Context, type Theme } from './useTheme.ts'

const STORAGE_KEY = 'gridwise.theme'

function initialTheme(): Theme {
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved === 'light' || saved === 'dark') return saved
  } catch {
    // storage may be unavailable
  }
  return 'system'
}

// index.html applies the saved theme before React loads; this keeps data-theme and storage in step afterwards.
// "system" has no data-theme: the CSS follows prefers-color-scheme then.
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>(initialTheme)

  useEffect(() => {
    const root = document.documentElement
    if (theme === 'system') root.removeAttribute('data-theme')
    else root.dataset.theme = theme
    try {
      if (theme === 'system') localStorage.removeItem(STORAGE_KEY)
      else localStorage.setItem(STORAGE_KEY, theme)
    } catch {
      // not saved; the choice still applies to this session
    }
  }, [theme])

  return <Context.Provider value={{ theme, setTheme }}>{children}</Context.Provider>
}
