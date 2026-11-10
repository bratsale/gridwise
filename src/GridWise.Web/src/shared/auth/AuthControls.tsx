import { useT } from '../i18n/useLanguage.ts'
import { login, logout, useAuth } from './auth.ts'
import { primaryRole } from './roles.ts'

// The header part: who is logged in (name and role) with a logout button, or a login button.
export function AuthControls() {
  const t = useT()
  const auth = useAuth()

  if (auth.status === 'loading') return <span className="auth-user">{t('auth.signingIn')}</span>
  if (auth.status === 'anonymous') {
    return <button type="button" className="small primary" onClick={login}>{t('auth.login')}</button>
  }

  const role = primaryRole(auth.roles)
  return (
    <div className="auth">
      <span className="auth-user">
        {t('auth.user', { name: auth.name ?? '?', role: t(role ? `auth.role.${role}` : 'auth.role.none') })}
      </span>
      <button type="button" className="small" onClick={logout}>{t('auth.logout')}</button>
    </div>
  )
}
