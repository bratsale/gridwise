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
  if (loading) return <p>Loading…</p>
  if (plans.length === 0) return <p>No plans yet.</p>

  return (
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
              <td>{plan.status}</td>
              <td>{plan.effectiveFrom}</td>
              <td className="num">{formatPercent(plan.discount)}</td>
              <td className="num">{plan.tiers.length}</td>
              <td>
                {!retired && (
                  <div className="actions">
                    <button type="button" onClick={() => onEdit(plan)}>Edit</button>
                    <button type="button" onClick={() => onRetire(plan)}>Retire</button>
                  </div>
                )}
              </td>
            </tr>
          )
        })}
      </tbody>
    </table>
  )
}
