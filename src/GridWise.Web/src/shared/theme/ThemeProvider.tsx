import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'

export type Theme = 'light' | 'dark' | 'system'
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

const Context = createContext<{ theme: Theme; setTheme: (theme: Theme) => void } | null>(null)

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

export function useTheme() {
  const value = useContext(Context)
  if (!value) throw new Error('ThemeProvider is missing')
  return value
}
