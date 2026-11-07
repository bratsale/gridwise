import { useLanguage, useT } from './LanguageProvider.tsx'
import type { Language } from './formatters.ts'

const options: { value: Language; label: string }[] = [
  { value: 'en', label: 'EN' },
  { value: 'sr', label: 'SR' },
]

export function LanguageSwitcher() {
  const { language, setLanguage } = useLanguage()
  const t = useT()
  return (
    <div className="segmented" role="group" aria-label={t('lang.label')}>
      {options.map((o) => (
        <button key={o.value} type="button" aria-pressed={language === o.value} onClick={() => setLanguage(o.value)}>
          {o.label}
        </button>
      ))}
    </div>
  )
}
