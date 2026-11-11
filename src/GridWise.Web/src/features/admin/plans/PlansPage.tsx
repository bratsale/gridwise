import { useFormat, useT } from '../../../shared/i18n/useLanguage.ts'
import { Banner } from '../../../shared/ui/Banner.tsx'
import { PlanHistory } from './components/PlanHistory.tsx'
import { PlanList } from './components/PlanList.tsx'
import { usePlans, type Notice } from './hooks/usePlans.ts'
import { PlanFormSection } from './PlanFormSection.tsx'
import { RetireSection } from './RetireSection.tsx'

export function PlansPage() {
  const t = useT()
  const format = useFormat()
  const plans = usePlans()

  function noticeText(notice: Notice) {
    switch (notice.kind) {
      case 'created': return t('plans.notice.created')
      case 'versionSaved': return t('plans.notice.versionSaved', { version: notice.version })
      case 'retired': return t('plans.notice.retired', { date: format.date(notice.date) })
    }
  }

  return (
    <section>
      <div className="toolbar">
        <button type="button" className="primary" onClick={plans.openCreate}>{t('plans.new')}</button>
        <button type="button" disabled={plans.loading} onClick={() => void plans.load()}>{t('common.refresh')}</button>
      </div>

      {plans.notice && (
        // Keyed by the notice, so the next save shows a new banner with a new timer.
        <div className="notice" role="status" key={JSON.stringify(plans.notice)}>
          <Banner kind="success" onDismiss={plans.dismissNotice}>{noticeText(plans.notice)}.</Banner>
          <Banner kind="info">{t('admin.notice.delay')}</Banner>
        </div>
      )}
      {plans.listError && <Banner kind="error" message={plans.listError} />}
      {plans.rowError && <Banner kind="error" message={plans.rowError} />}

      {plans.form && (
        <PlanFormSection key={plans.form.key} session={plans.form.session} optimistic={plans.optimistic} onSaved={plans.changed} onCancel={plans.closeForm} />
      )}
      {plans.retiring && (
        <RetireSection key={plans.retiring.id} plan={plans.retiring} optimistic={plans.optimistic} onDone={plans.changed} onCancel={plans.closeRetire} />
      )}

      {plans.history && <PlanHistory plan={plans.history.plan} versions={plans.history.versions} />}

      {/* When loading failed and nothing is known, the error above is the whole message: no "no plans yet". */}
      {!(plans.listError && plans.plans.length === 0) && (
        <PlanList
          plans={plans.plans}
          pending={plans.pending}
          loading={plans.loading}
          selectedId={plans.history?.plan.id ?? null}
          flashId={plans.flash}
          onHistory={(plan) => void plans.showHistory(plan)}
          onEdit={(plan) => void plans.edit(plan)}
          onRetire={plans.openRetire}
        />
      )}
    </section>
  )
}
