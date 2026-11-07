import { lazy, Suspense, useState } from 'react'
import { useT } from '../../shared/i18n/LanguageProvider.tsx'
import { Loading } from '../../shared/ui/Loading.tsx'

// Each admin page is its own chunk, loaded when its sub-tab is first opened.
const PlansPage = lazy(() => import('./plans/PlansPage.tsx').then((m) => ({ default: m.PlansPage })))
const TaxGroupsPage = lazy(() => import('./taxGroups/TaxGroupsPage.tsx').then((m) => ({ default: m.TaxGroupsPage })))

type Section = 'plans' | 'taxGroups'

export function AdminPage() {
  const t = useT()
  const [section, setSection] = useState<Section>('plans')

  return (
    <div>
      <div className="subtabs" role="tablist">
        <button role="tab" aria-selected={section === 'plans'} onClick={() => setSection('plans')}>
          {t('admin.plans')}
        </button>
        <button role="tab" aria-selected={section === 'taxGroups'} onClick={() => setSection('taxGroups')}>
          {t('admin.taxGroups')}
        </button>
      </div>
      <Suspense fallback={<Loading />}>{section === 'plans' ? <PlansPage /> : <TaxGroupsPage />}</Suspense>
    </div>
  )
}
