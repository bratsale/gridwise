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
      <div className="tiers">
        <span className="head">Up to</span>
        <span className="head">Price</span>
        <span />
        {tiers.map((row, i) => {
          const isLast = i === tiers.length - 1
          return (
            <div key={i} style={{ display: 'contents' }}>
              <div className="cell">
                <div className="unit">
                  <input
                    type="number" step="any" min="0" aria-label={`Tier ${i + 1} up to (kWh)`}
                    disabled={isLast} placeholder={isLast ? 'no limit' : ''}
                    value={isLast ? '' : row.threshold}
                    onChange={(e) => setTier(i, { threshold: e.target.value })}
                  />
                  <span>kWh</span>
                </div>
                <FieldErrors messages={errorsFor(problem, `tiers[${i}].threshold`)} />
              </div>
              <div className="cell">
                <div className="unit">
                  <input
                    type="number" step="any" min="0" required aria-label={`Tier ${i + 1} price per kWh`}
                    value={row.price} onChange={(e) => setTier(i, { price: e.target.value })}
                  />
                  <span>per kWh</span>
                </div>
                <FieldErrors messages={errorsFor(problem, `tiers[${i}].pricePerKwh`)} />
              </div>
              <button
                type="button" className="icon" aria-label={`Remove tier ${i + 1}`} title="Remove tier"
                disabled={tiers.length === 1} onClick={() => onChange(tiers.filter((_, j) => j !== i))}
              >
                ✕
              </button>
            </div>
          )
        })}
      </div>
      <button type="button" className="small" onClick={() => onChange([...tiers, { threshold: '', price: '' }])}>Add tier</button>
    </fieldset>
  )
}
