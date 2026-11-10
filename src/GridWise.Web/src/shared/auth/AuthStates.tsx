import { useT } from '../i18n/useLanguage.ts'
import { login } from './auth.ts'

// Shown instead of a page when nobody is logged in.
export function LoginRequired() {
  const t = useT()
  return (
    <section className="card state-card">
      <p className="state">{t('auth.required')}</p>
      <button type="button" className="primary" onClick={login}>{t('auth.login')}</button>
    </section>
  )
}

// Shown instead of a page the user's role does not allow (for example a customer opening #admin/plans).
export function NoPermission() {
  const t = useT()
  return (
    <section className="card state-card" role="alert">
      <p className="state">{t('auth.forbidden')}</p>
    </section>
  )
}
