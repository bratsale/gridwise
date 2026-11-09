import { createContext, useContext } from 'react'
import type { TranslationKey } from './en.ts'
import type { Formatters, Language } from './formatters.ts'
import type { Message, Params } from './message.ts'

// The context and the hooks that read it. LanguageProvider.tsx has the component that fills it (kept apart so that file exports only a component).
export type T = (key: TranslationKey, params?: Params) => string

export interface LanguageContext {
  language: Language
  setLanguage: (language: Language) => void
  t: T
  text: (message: Message) => string
  format: Formatters
}

export const Context = createContext<LanguageContext | null>(null)

function useLanguageContext() {
  const value = useContext(Context)
  if (!value) throw new Error('LanguageProvider is missing')
  return value
}

export const useT = () => useLanguageContext().t
export const useMessageText = () => useLanguageContext().text
export const useFormat = () => useLanguageContext().format
export const useLanguage = () => {
  const { language, setLanguage } = useLanguageContext()
  return { language, setLanguage }
}
