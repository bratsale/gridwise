import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { en, type TranslationKey } from './en.ts'
import { createFormatters, LOCALES, type Formatters, type Language } from './formatters.ts'
import type { Message, Params } from './message.ts'
import { sr } from './sr.ts'

const dictionaries = { en, sr }
const STORAGE_KEY = 'gridwise.lang'

function initialLanguage(): Language {
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved === 'en' || saved === 'sr') return saved
  } catch {
    // storage may be unavailable; fall through to the browser language
  }
  return navigator.language.toLowerCase().startsWith('sr') ? 'sr' : 'en'
}

export type T = (key: TranslationKey, params?: Params) => string

interface LanguageContext {
  language: Language
  setLanguage: (language: Language) => void
  t: T
  text: (message: Message) => string
  format: Formatters
}

const Context = createContext<LanguageContext | null>(null)

export function LanguageProvider({ children }: { children: ReactNode }) {
  const [language, setLanguage] = useState<Language>(initialLanguage)

  useEffect(() => {
    document.documentElement.lang = LOCALES[language]
    try {
      localStorage.setItem(STORAGE_KEY, language)
    } catch {
      // not saved; the choice still applies to this session
    }
  }, [language])

  const value = useMemo<LanguageContext>(() => {
    const dictionary = dictionaries[language]
    const t: T = (key, params) =>
      dictionary[key].replace(/\{(\w+)\}/g, (match, name: string) => (name in (params ?? {}) ? String(params?.[name]) : match))
    const text = (message: Message) => ('text' in message ? message.text : t(message.key, message.params))
    return { language, setLanguage, t, text, format: createFormatters(language) }
  }, [language])

  return <Context.Provider value={value}>{children}</Context.Provider>
}

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
