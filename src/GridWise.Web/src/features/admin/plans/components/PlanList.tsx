import { useFormat, useT } from '../../../../shared/i18n/useLanguage.ts'
import { Loading } from '../../../../shared/ui/Loading.tsx'
import type { Plan } from '../api/plansApi.ts'

export function PlanList({
  plans, pending, loading, onHistory, onEdit, onRetire,
}: {
  plans: Plan[]
  pending: ReadonlySet<string>
  loading: boolean
  onHistory: (plan: Plan) => void
  onEdit: (plan: Plan) => void
  onRetire: (plan: Plan) => void
}) {
  const t = useT()
  const format = useFormat()

  if (loading) return <div className="card"><Loading /></div>
  if (plans.length === 0) return <div className="card"><p className="state">{t('plans.empty')}</p></div>

  return (
    <div className="card flush table-wrap">
      <table className="plans">
        <thead>
          <tr>
            <th>{t('plans.name')}</th><th>{t('plans.version')}</th><th>{t('plans.status')}</th><th>{t('plans.effectiveFrom')}</th>
            <th className="num">{t('plans.discount')}</th><th className="num">{t('plans.tiers')}</th><th />
          </tr>
        </thead>
        <tbody>
          {plans.map((plan) => {
            const retired = plan.status === 'Retired'
            const isPending = pending.has(plan.id)
            return (
              <tr key={plan.id} className={isPending ? 'pending' : retired ? 'retired' : undefined}>
                <td>
                  <button type="button" className="link" onClick={() => onHistory(plan)}>{plan.name}</button>
                </td>
                <td>{String(plan.versionNo)}</td>
                <td>
                  {isPending
                    ? <span className="badge pending">{t('common.saving')}</span>
                    : <span className={`badge ${retired ? 'retired' : 'active'}`}>{retired ? t('plans.status.retired') : t('plans.status.active')}</span>}
                </td>
                <td>{format.date(plan.effectiveFrom)}</td>
                <td className="num">{format.percent(plan.discount)}</td>
                <td className="num">{plan.tiers.length}</td>
                <td>
                  {!retired && !isPending && (
                    <div className="actions">
                      <button type="button" className="small" onClick={() => onEdit(plan)}>{t('plans.edit')}</button>
                      <button type="button" className="small" onClick={() => onRetire(plan)}>{t('plans.retire')}</button>
                    </div>
                  )}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
