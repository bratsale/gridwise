import type { Plan } from '../../api/catalogApi.ts'
import { formatKwh, formatPercent, formatPrice } from '../../format.ts'

// versions is null while they are loading.
export function PlanHistory({ plan, versions }: { plan: Plan; versions: Plan[] | null }) {
  return (
    <div className="panel">
      <h3>History of "{plan.name}"</h3>
      {versions === null ? (
        <p>Loading…</p>
      ) : versions.length === 0 ? (
        <p>No versions.</p>
      ) : (
        <table className="plans">
          <thead>
            <tr>
              <th>Version</th><th>Status</th><th>Effective from</th><th className="num">Discount</th><th>Tiers</th>
            </tr>
          </thead>
          <tbody>
            {versions.map((v) => (
              <tr key={String(v.versionNo)}>
                <td>{String(v.versionNo)}</td>
                <td>{v.status}</td>
                <td>{v.effectiveFrom}</td>
                <td className="num">{formatPercent(v.discount)}</td>
                <td>
                  {v.tiers.length === 0
                    ? '-'
                    : v.tiers
                        .map((t) => `${t.threshold == null ? 'no limit' : `up to ${formatKwh(t.threshold)}`}: ${formatPrice(t.pricePerKwh)} per kWh`)
                        .join('; ')}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
