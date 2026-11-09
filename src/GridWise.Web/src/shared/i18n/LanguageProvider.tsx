import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { en } from './en.ts'
import { createFormatters, LOCALES, type Language } from './formatters.ts'
import type { Message } from './message.ts'
import { sr } from './sr.ts'
import { Context, type LanguageContext, type T } from './useLanguage.ts'

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
