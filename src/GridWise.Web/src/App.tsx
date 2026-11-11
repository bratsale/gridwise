import { lazy, Suspense, useEffect } from 'react'
import { isKnownHash, useRoute } from './shared/routing/route.ts'
import { LanguageSwitcher } from './shared/i18n/LanguageSwitcher.tsx'
import { useT } from './shared/i18n/useLanguage.ts'
import { ThemeSwitcher } from './shared/theme/ThemeSwitcher.tsx'
import { Loading } from './shared/ui/Loading.tsx'
import { useAuth } from './shared/auth/auth.ts'
import { AuthControls } from './shared/auth/AuthControls.tsx'
import { LoginRequired, NoPermission } from './shared/auth/AuthStates.tsx'
import { defaultRoute, isAdmin, isCustomerOrAdmin, pageFor } from './shared/auth/roles.ts'

// Each page is its own chunk, loaded when its tab is first opened.
const AdminPage = lazy(() => import('./features/admin/AdminPage.tsx').then((m) => ({ default: m.AdminPage })))
const CustomerPage = lazy(() => import('./features/customer/CustomerPage.tsx').then((m) => ({ default: m.CustomerPage })))

export default function App() {
  const t = useT()
  const auth = useAuth()
  const [route, navigate] = useRoute()
  const admin = isAdmin(auth.roles)

  // An address that names no page (none, a typo, an old link) opens the page that fits the role; a customer would otherwise
  // land on the admin plans and see "no permission". `route` changes with every hash, so a new unknown hash is caught too.
  const knownPage = isKnownHash(window.location.hash)
  useEffect(() => {
    if (auth.status === 'authenticated' && !knownPage) navigate(defaultRoute(auth.roles))
  }, [auth.status, auth.roles, navigate, knownPage, route])

  function content() {
    if (auth.status === 'loading') return <Loading />
    if (auth.status === 'anonymous') return <LoginRequired />
    if (!knownPage) return <Loading /> // for the moment until the effect above has moved to the default page
    switch (pageFor(route, auth.roles)) {
      case 'forbidden':
        return <NoPermission />
      case 'admin':
        return route.tab === 'admin' && (
          <AdminPage section={route.section} onSection={(section) => navigate({ tab: 'admin', section })} />
        )
      default:
        return <CustomerPage />
    }
  }

  return (
    <div className="app">
      <header className="app-header">
        <div>
          <h1>GridWise</h1>
          <p className="subtitle">{t('app.subtitle')}</p>
        </div>
        <div className="switchers">
          <LanguageSwitcher />
          <ThemeSwitcher />
          <AuthControls />
        </div>
      </header>
      {auth.status === 'authenticated' && (
        <div className="tabs" role="tablist">
          {/* Only admins see the Admin tab; the admin pages are still guarded (and the API checks the role itself). */}
          {admin && (
            <button role="tab" aria-selected={route.tab === 'admin'} onClick={() => navigate({ tab: 'admin', section: route.tab === 'admin' ? route.section : 'plans' })}>
              {t('tab.admin')}
            </button>
          )}
          {isCustomerOrAdmin(auth.roles) && (
            <button role="tab" aria-selected={route.tab === 'customer'} onClick={() => navigate({ tab: 'customer' })}>
              {t('tab.customer')}
            </button>
          )}
        </div>
      )}
      <Suspense fallback={<Loading />}>{content()}</Suspense>
    </div>
  )
}
