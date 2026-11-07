import { useT } from '../i18n/LanguageProvider.tsx'

export function Loading() {
  const t = useT()
  return (
    <p className="state" role="status">
      <span className="spinner" aria-hidden="true" />
      {t('common.loading')}
    </p>
  )
}
