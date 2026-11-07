import type { ProblemDetails } from '../../api/client.ts'
import { errorsFor } from '../../api/problems.ts'
import { FieldErrors } from '../FieldErrors.tsx'

export interface TierRow {
  threshold: string
  price: string
}

export function TiersEditor({
  tiers, onChange, problem,
}: { tiers: TierRow[]; onChange: (tiers: TierRow[]) => void; problem: ProblemDetails | undefined }) {
  function setTier(index: number, change: Partial<TierRow>) {
    onChange(tiers.map((row, i) => (i === index ? { ...row, ...change } : row)))
  }

  return (
    <fieldset>
      <legend>Tiers</legend>
      <FieldErrors messages={errorsFor(problem, 'tiers')} />
      <table className="tiers">
        <thead>
          <tr><th>Up to (kWh)</th><th>Price per kWh</th><th /></tr>
        </thead>
        <tbody>
          {tiers.map((row, i) => {
            const isLast = i === tiers.length - 1
            return (
              <tr key={i}>
                <td>
                  <input
                    type="number" step="any" min="0"
                    disabled={isLast} placeholder={isLast ? 'no limit' : ''}
                    value={isLast ? '' : row.threshold}
                    onChange={(e) => setTier(i, { threshold: e.target.value })}
                  />
                  <FieldErrors messages={errorsFor(problem, `tiers[${i}].threshold`)} />
                </td>
                <td>
                  <input
                    type="number" step="any" min="0" required
                    value={row.price} onChange={(e) => setTier(i, { price: e.target.value })}
                  />
                  <FieldErrors messages={errorsFor(problem, `tiers[${i}].pricePerKwh`)} />
                </td>
                <td>
                  <button type="button" disabled={tiers.length === 1} onClick={() => onChange(tiers.filter((_, j) => j !== i))}>
                    Remove
                  </button>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
      <button type="button" onClick={() => onChange([...tiers, { threshold: '', price: '' }])}>Add tier</button>
    </fieldset>
  )
}
