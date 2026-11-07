import { lazy, Suspense } from 'react'
import { useRoute } from './shared/routing/route.ts'
import { LanguageSwitcher } from './shared/i18n/LanguageSwitcher.tsx'
import { useT } from './shared/i18n/LanguageProvider.tsx'
import { ThemeSwitcher } from './shared/theme/ThemeSwitcher.tsx'
import { Loading } from './shared/ui/Loading.tsx'

// Each page is its own chunk, loaded when its tab is first opened.
const AdminPage = lazy(() => import('./features/admin/AdminPage.tsx').then((m) => ({ default: m.AdminPage })))
const CustomerPage = lazy(() => import('./features/customer/CustomerPage.tsx').then((m) => ({ default: m.CustomerPage })))

export default function App() {
  const t = useT()
  const [route, navigate] = useRoute()

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
        </div>
      </header>
      <div className="tabs" role="tablist">
        <button role="tab" aria-selected={route.tab === 'admin'} onClick={() => navigate({ tab: 'admin', section: route.tab === 'admin' ? route.section : 'plans' })}>
          {t('tab.admin')}
        </button>
        <button role="tab" aria-selected={route.tab === 'customer'} onClick={() => navigate({ tab: 'customer' })}>
          {t('tab.customer')}
        </button>
      </div>
      <Suspense fallback={<Loading />}>{route.tab === 'admin'
          ? <AdminPage section={route.section} onSection={(section) => navigate({ tab: 'admin', section })} />
          : <CustomerPage />}</Suspense>
    </div>
  )
}
