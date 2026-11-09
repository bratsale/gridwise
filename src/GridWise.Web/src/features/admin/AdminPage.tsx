import { lazy, Suspense } from 'react'
import { useT } from '../../shared/i18n/useLanguage.ts'
import type { AdminSection } from '../../shared/routing/route.ts'
import { Loading } from '../../shared/ui/Loading.tsx'

// Each admin page is its own chunk, loaded when its sub-tab is first opened.
const PlansPage = lazy(() => import('./plans/PlansPage.tsx').then((m) => ({ default: m.PlansPage })))
const TaxGroupsPage = lazy(() => import('./taxGroups/TaxGroupsPage.tsx').then((m) => ({ default: m.TaxGroupsPage })))
const BatchPage = lazy(() => import('./batch/BatchPage.tsx').then((m) => ({ default: m.BatchPage })))

export function AdminPage({ section, onSection }: { section: AdminSection; onSection: (section: AdminSection) => void }) {
  const t = useT()

  return (
    <div>
      <div className="subtabs" role="tablist">
        <button role="tab" aria-selected={section === 'plans'} onClick={() => onSection('plans')}>
          {t('admin.plans')}
        </button>
        <button role="tab" aria-selected={section === 'taxGroups'} onClick={() => onSection('taxGroups')}>
          {t('admin.taxGroups')}
        </button>
        <button role="tab" aria-selected={section === 'batch'} onClick={() => onSection('batch')}>
          {t('admin.batch')}
        </button>
      </div>
      <Suspense fallback={<Loading />}>{section === 'plans' ? <PlansPage /> : section === 'taxGroups' ? <TaxGroupsPage /> : <BatchPage />}</Suspense>
    </div>
  )
}
