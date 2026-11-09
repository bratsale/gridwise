import { createContext, useContext } from 'react'

export type Theme = 'light' | 'dark' | 'system'

// The context and the hook that reads it. ThemeProvider.tsx has the component that fills it (kept apart so that file exports only a component).
export const Context = createContext<{ theme: Theme; setTheme: (theme: Theme) => void } | null>(null)

export function useTheme() {
  const value = useContext(Context)
  if (!value) throw new Error('ThemeProvider is missing')
  return value
}
