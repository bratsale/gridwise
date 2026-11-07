import type { Plan } from '../../api/catalogApi.ts'
import { formatKwh, formatPercent, formatPrice } from '../../format.ts'

// versions is null while they are loading.
// The latest version (highest number) tells whether the plan is retired, and from when.
function retiredFrom(versions: Plan[]) {
  const latest = versions.reduce((a, b) => (Number(b.versionNo) > Number(a.versionNo) ? b : a))
  return latest.status === 'Retired' ? latest.effectiveFrom : null
}

export function PlanHistory({ plan, versions }: { plan: Plan; versions: Plan[] | null }) {
  return (
    <div className="panel">
      <h3>History of "{plan.name}"</h3>
      {versions === null ? (
        <p className="state" role="status"><span className="spinner" aria-hidden="true" />Loading…</p>
      ) : versions.length === 0 ? (
        <p className="state">No versions.</p>
      ) : (
        <>
        {retiredFrom(versions) && <p className="banner info"><span>Plan retired from {retiredFrom(versions)}</span></p>}
        <div className="table-wrap">
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
                <td><span className={`badge ${v.status === 'Retired' ? 'retired' : 'active'}`}>{v.status}</span></td>
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
        </div>
        </>
      )}
    </div>
  )
}
