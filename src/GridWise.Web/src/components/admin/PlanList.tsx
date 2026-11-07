import type { Plan } from '../../api/catalogApi.ts'
import { formatPercent } from '../../format.ts'

export function PlanList({
  plans, loading, onHistory, onEdit, onRetire,
}: {
  plans: Plan[]
  loading: boolean
  onHistory: (plan: Plan) => void
  onEdit: (plan: Plan) => void
  onRetire: (plan: Plan) => void
}) {
  if (loading) {
    return <div className="card"><p className="state" role="status"><span className="spinner" aria-hidden="true" />Loading…</p></div>
  }
  if (plans.length === 0) return <div className="card"><p className="state">No plans yet.</p></div>

  return (
    <div className="card flush table-wrap">
    <table className="plans">
      <thead>
        <tr>
          <th>Name</th><th>Version</th><th>Status</th><th>Effective from</th>
          <th className="num">Discount</th><th className="num">Tiers</th><th />
        </tr>
      </thead>
      <tbody>
        {plans.map((plan) => {
          const retired = plan.status === 'Retired'
          return (
            <tr key={plan.id} className={retired ? 'retired' : undefined}>
              <td>
                <button type="button" className="link" onClick={() => onHistory(plan)}>{plan.name}</button>
              </td>
              <td>{String(plan.versionNo)}</td>
              <td><span className={`badge ${retired ? 'retired' : 'active'}`}>{plan.status}</span></td>
              <td>{plan.effectiveFrom}</td>
              <td className="num">{formatPercent(plan.discount)}</td>
              <td className="num">{plan.tiers.length}</td>
              <td>
                {!retired && (
                  <div className="actions">
                    <button type="button" className="small" onClick={() => onEdit(plan)}>Edit</button>
                    <button type="button" className="small" onClick={() => onRetire(plan)}>Retire</button>
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
