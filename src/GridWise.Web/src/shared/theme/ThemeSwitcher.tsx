import { useT } from '../i18n/useLanguage.ts'
import { useTheme, type Theme } from './useTheme.ts'

const options: { value: Theme; label: 'theme.light' | 'theme.dark' | 'theme.system' }[] = [
  { value: 'light', label: 'theme.light' },
  { value: 'dark', label: 'theme.dark' },
  { value: 'system', label: 'theme.system' },
]

export function ThemeSwitcher() {
  const { theme, setTheme } = useTheme()
  const t = useT()
  return (
    <div className="segmented" role="group" aria-label={t('theme.label')}>
      {options.map((o) => (
        <button key={o.value} type="button" aria-pressed={theme === o.value} onClick={() => setTheme(o.value)}>
          {t(o.label)}
        </button>
      ))}
    </div>
  )
}
