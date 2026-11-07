import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { components as CatalogTypes } from '../api/catalog.ts'
import type { components as RecommendationTypes } from '../api/recommendation.ts'
import { request, type ProblemDetails } from '../api/client.ts'
import { errorsFor } from '../api/problems.ts'
import { formatKwh, formatMoney, formatPrice } from '../format.ts'
import { FieldErrors } from './FieldErrors.tsx'

type TaxGroup = CatalogTypes['schemas']['TaxGroupResponse']
type Recommendation = RecommendationTypes['schemas']['RecommendationResponse']
type Quote = RecommendationTypes['schemas']['PlanQuoteResponse']
type Acceptance = RecommendationTypes['schemas']['AcceptanceResponse']
type Breakdown = RecommendationTypes['schemas']['BreakdownResponse']

function BreakdownTable({ breakdown }: { breakdown: Breakdown }) {
  return (
    <table className="breakdown">
      <tbody>
        {breakdown.tiers.map((tier, i) => (
          <tr key={i}>
            <td>
              {formatKwh(tier.kwh)} × {formatPrice(tier.pricePerKwh)}
            </td>
            <td className="num">{formatMoney(tier.amount)}</td>
          </tr>
        ))}
        <tr><td>Energy subtotal</td><td className="num">{formatMoney(breakdown.energySubtotal)}</td></tr>
        <tr><td>Discount</td><td className="num">{formatMoney(breakdown.discountAmount)}</td></tr>
        <tr><td>Net energy</td><td className="num">{formatMoney(breakdown.netEnergy)}</td></tr>
        <tr><td>Eco tax</td><td className="num">{formatMoney(breakdown.ecoTax)}</td></tr>
        <tr><td>VAT base</td><td className="num">{formatMoney(breakdown.vatBase)}</td></tr>
        <tr><td>VAT</td><td className="num">{formatMoney(breakdown.vatAmount)}</td></tr>
        <tr className="total"><td>Total</td><td className="num">{formatMoney(breakdown.total)}</td></tr>
      </tbody>
    </table>
  )
}

export function Customer() {
  const [taxGroups, setTaxGroups] = useState<TaxGroup[]>([])
  const [taxGroupsLoading, setTaxGroupsLoading] = useState(true)
  const [taxGroupsError, setTaxGroupsError] = useState<string | null>(null)

  const [consumption, setConsumption] = useState('')
  const [taxGroup, setTaxGroup] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [problem, setProblem] = useState<ProblemDetails | undefined>()
  const [message, setMessage] = useState<string | null>(null)

  const [result, setResult] = useState<Recommendation | null>(null)
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set())
  const [accepting, setAccepting] = useState<string | null>(null)
  const resultRef = useRef<HTMLDivElement>(null)
  const [accepted, setAccepted] = useState<string | null>(null)
  const [acceptError, setAcceptError] = useState<string | null>(null)

  // After calculating, focus moves to the result.
  useEffect(() => {
    if (result) resultRef.current?.focus()
  }, [result])

  useEffect(() => {
    void (async () => {
      try {
        const response = await request<TaxGroup[]>('catalog', '/tax-groups')
        if (response.ok) {
          const active = response.body.filter((g) => g.status !== 'Retired')
          setTaxGroups(active)
          setTaxGroup((current) => current || active[0]?.code || '')
        } else {
          setTaxGroupsError(response.body?.title ?? 'Tax groups could not be loaded.')
        }
      } catch {
        setTaxGroupsError('Tax groups could not be loaded.')
      }
      setTaxGroupsLoading(false)
    })()
  }, [])

  async function calculate(event: FormEvent) {
    event.preventDefault()
    setSubmitting(true)
    setProblem(undefined)
    setMessage(null)
    setResult(null)
    setExpanded(new Set())
    setAccepted(null)
    setAcceptError(null)

    try {
      const response = await request<Recommendation>('recommendation', '/recommendations', {
        method: 'POST',
        body: { consumptionKwh: Number(consumption), taxGroup },
      })

      if (response.ok) {
        setResult(response.body)
      } else if (response.status === 400) {
        setProblem(response.body)
        // Errors for other keys have no field to sit next to: show them as a message.
        const others = Object.entries(response.body?.errors ?? {})
          .filter(([key]) => !['consumptionkwh', 'taxgroup'].includes(key.toLowerCase()))
          .flatMap(([, messages]) => messages)
        if (others.length > 0 || !response.body?.errors) setMessage([...others, response.body?.title].filter(Boolean).join(' '))
      } else if (response.status === 503) {
        setMessage('No plans are available yet. Try again in a moment.')
      } else {
        setMessage(response.body?.title ?? `Unexpected error (HTTP ${response.status}).`)
      }
    } catch {
      setMessage('The service could not be reached.')
    } finally {
      setSubmitting(false)
    }
  }

  async function accept(quote: Quote) {
    if (!result) return
    setAccepting(`${quote.planId}:${String(quote.planVersionNo)}`)
    setAcceptError(null)

    try {
      const response = await request<Acceptance>('recommendation', `/recommendations/${result.id}/accept`, {
        method: 'POST',
        body: { planId: quote.planId },
      })

      if (response.ok) {
        setAccepted(`Accepted: ${quote.name}, ${formatMoney(response.body.total)}`)
      } else if (response.status === 409) {
        setAcceptError('This recommendation was already accepted with another plan.')
      } else {
        setAcceptError(response.body?.title ?? `Unexpected error (HTTP ${response.status}).`)
      }
    } catch {
      setAcceptError('The service could not be reached.')
    } finally {
      setAccepting(null)
    }
  }

  function toggle(key: string) {
    setExpanded((current) => {
      const next = new Set(current)
      if (!next.delete(key)) next.add(key)
      return next
    })
  }

  return (
    <section>
      <form onSubmit={(e) => void calculate(e)} className="form">
        <label>
          Consumption
          <div className="unit">
            <input
              type="number"
              step="0.001"
              min="0"
              required
              value={consumption}
              onChange={(e) => setConsumption(e.target.value)}
            />
            <span>kWh</span>
          </div>
          <FieldErrors messages={errorsFor(problem, 'consumptionKwh')} />
        </label>

        <label>
          Tax group
          <select required value={taxGroup} onChange={(e) => setTaxGroup(e.target.value)}>
            {taxGroupsLoading && <option value="">Loading…</option>}
            {taxGroups.map((g) => (
              <option key={g.code} value={g.code}>{g.code}</option>
            ))}
          </select>
          <FieldErrors messages={errorsFor(problem, 'taxGroup')} />
        </label>

        <button type="submit" className="primary" disabled={submitting || taxGroups.length === 0}>
          {submitting ? 'Calculating…' : 'Calculate'}
        </button>
      </form>

      {taxGroupsError && <p className="error">{taxGroupsError}</p>}
      {message && <p className="error">{message}</p>}

      {result && (
        <div className="result" ref={resultRef} tabIndex={-1}>
          <p className="meta">
            {formatKwh(result.consumptionKwh)}, tax group {result.taxGroup.code} (version {String(result.taxGroup.versionNo)}),
            calculated at {new Date(result.calculatedAt).toLocaleString()}
          </p>

          <div className="card flush table-wrap">
          <table className="plans">
            <thead>
              <tr>
                <th>Plan</th>
                <th>Version</th>
                <th className="num">Total</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {result.plans.map((quote) => {
                const key = `${quote.planId}:${String(quote.planVersionNo)}`
                const isBest = quote.planId === result.recommended?.planId
                const isOpen = expanded.has(key)
                return [
                  <tr key={key} className={isBest ? 'best' : undefined}>
                    <td>
                      <button
                        type="button"
                        className="link"
                        aria-expanded={isOpen}
                        onClick={() => toggle(key)}
                      >
                        {isOpen ? '▾' : '▸'} {quote.name}
                      </button>
                      {isBest && <span className="badge best">Best</span>}
                    </td>
                    <td>{String(quote.planVersionNo)}</td>
                    <td className="num">{formatMoney(quote.total)}</td>
                    <td>
                      <button type="button" className="small primary" disabled={accepting !== null || accepted !== null} onClick={() => void accept(quote)}>
                        {accepting === key ? 'Accepting…' : 'Accept'}
                      </button>
                    </td>
                  </tr>,
                  isOpen && (
                    <tr key={`${key}:details`} className="details">
                      <td colSpan={4}>
                        <BreakdownTable breakdown={quote.breakdown} />
                      </td>
                    </tr>
                  ),
                ]
              })}
            </tbody>
          </table>
          </div>

          {accepted && <p className="banner success" role="status"><span>{accepted}</span></p>}
          {acceptError && <p className="error">{acceptError}</p>}
        </div>
      )}
    </section>
  )
}
