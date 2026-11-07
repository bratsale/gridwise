import { useFormat, useMessageText, useT } from '../../../shared/i18n/LanguageProvider.tsx'
import { Banner } from '../../../shared/ui/Banner.tsx'
import { PlanHistory } from './components/PlanHistory.tsx'
import { PlanList } from './components/PlanList.tsx'
import { usePlans, type Notice } from './hooks/usePlans.ts'
import { PlanFormSection } from './PlanFormSection.tsx'
import { RetireSection } from './RetireSection.tsx'

export function PlansPage() {
  const t = useT()
  const text = useMessageText()
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
        <div className="notice" role="status">
          <Banner kind="success" onDismiss={plans.dismissNotice}>{noticeText(plans.notice)}.</Banner>
          <Banner kind="info">{t('plans.notice.delay')}</Banner>
        </div>
      )}
      {plans.listError && <Banner kind="error">{text(plans.listError)}</Banner>}
      {plans.rowError && <Banner kind="error">{text(plans.rowError)}</Banner>}

      {plans.form && (
        <PlanFormSection key={plans.form.key} session={plans.form.session} onSaved={plans.changed} onCancel={plans.closeForm} />
      )}
      {plans.retiring && (
        <RetireSection key={plans.retiring.id} plan={plans.retiring} onDone={plans.changed} onCancel={plans.closeRetire} />
      )}

      <PlanList
        plans={plans.plans}
        loading={plans.loading}
        onHistory={(plan) => void plans.showHistory(plan)}
        onEdit={(plan) => void plans.edit(plan)}
        onRetire={plans.openRetire}
      />

      {plans.history && <PlanHistory plan={plans.history.plan} versions={plans.history.versions} />}
    </section>
  )
}
