import { useT } from '../i18n/useLanguage.ts'

export function Loading() {
  const t = useT()
  return (
    <p className="state" role="status">
      <span className="spinner" aria-hidden="true" />
      {t('common.loading')}
    </p>
  )
}
