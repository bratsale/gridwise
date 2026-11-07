import { useEffect, useState, type FormEvent } from 'react'
import type { components as CatalogTypes } from '../api/catalog.ts'
import type { components as RecommendationTypes } from '../api/recommendation.ts'
import { request, type ProblemDetails } from '../api/client.ts'

type TaxGroup = CatalogTypes['schemas']['TaxGroupResponse']
type Recommendation = RecommendationTypes['schemas']['RecommendationResponse']
type Quote = RecommendationTypes['schemas']['PlanQuoteResponse']
type Acceptance = RecommendationTypes['schemas']['AcceptanceResponse']
type Breakdown = RecommendationTypes['schemas']['BreakdownResponse']

type Value = number | string

const money = new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const quantity = new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 })
const price = new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 })

// Display only: the API sends the amounts, nothing is calculated here.
const formatMoney = (value: Value) => money.format(Number(value))

// ProblemDetails errors have keys like "consumptionKwh"; compare ignoring case.
function fieldErrors(problem: ProblemDetails | undefined, field: string): string[] {
  const errors = problem?.errors ?? {}
  const key = Object.keys(errors).find((k) => k.toLowerCase() === field.toLowerCase())
  return key ? errors[key] : []
}

function FieldErrors({ messages }: { messages: string[] }) {
  return messages.length > 0 ? <p className="field-error">{messages.join(' ')}</p> : null
}

function BreakdownTable({ breakdown }: { breakdown: Breakdown }) {
  return (
    <table className="breakdown">
      <tbody>
        {breakdown.tiers.map((tier, i) => (
          <tr key={i}>
            <td>
              {quantity.format(Number(tier.kwh))} kWh × {price.format(Number(tier.pricePerKwh))}
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
  const [taxGroupsError, setTaxGroupsError] = useState<string | null>(null)

  const [consumption, setConsumption] = useState('')
  const [taxGroup, setTaxGroup] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [problem, setProblem] = useState<ProblemDetails | undefined>()
  const [message, setMessage] = useState<string | null>(null)

  const [result, setResult] = useState<Recommendation | null>(null)
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set())
  const [accepting, setAccepting] = useState(false)
  const [accepted, setAccepted] = useState<string | null>(null)
  const [acceptError, setAcceptError] = useState<string | null>(null)

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
    setAccepting(true)
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
      setAccepting(false)
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
          Consumption (kWh)
          <input
            type="number"
            step="0.001"
            min="0"
            required
            value={consumption}
            onChange={(e) => setConsumption(e.target.value)}
          />
          <FieldErrors messages={fieldErrors(problem, 'consumptionKwh')} />
        </label>

        <label>
          Tax group
          <select required value={taxGroup} onChange={(e) => setTaxGroup(e.target.value)}>
            {taxGroups.map((g) => (
              <option key={g.code} value={g.code}>{g.code}</option>
            ))}
          </select>
          <FieldErrors messages={fieldErrors(problem, 'taxGroup')} />
        </label>

        <button type="submit" disabled={submitting || taxGroups.length === 0}>Calculate</button>
      </form>

      {taxGroupsError && <p className="error">{taxGroupsError}</p>}
      {message && <p className="error">{message}</p>}

      {result && (
        <div className="result">
          <p className="meta">
            Tax group {result.taxGroup.code} (version {String(result.taxGroup.versionNo)}), calculated at{' '}
            {new Date(result.calculatedAt).toLocaleString()}
          </p>

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
                      {isBest && <span className="badge">Best</span>}
                    </td>
                    <td>{String(quote.planVersionNo)}</td>
                    <td className="num">{formatMoney(quote.total)}</td>
                    <td>
                      <button type="button" disabled={accepting || accepted !== null} onClick={() => void accept(quote)}>
                        Accept
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

          {accepted && <p className="success">{accepted}</p>}
          {acceptError && <p className="error">{acceptError}</p>}
        </div>
      )}
    </section>
  )
}
