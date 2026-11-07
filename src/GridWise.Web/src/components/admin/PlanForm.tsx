import { useRef, useState, type FormEvent } from 'react'
import { addVersion, createPlan, CONFLICT_MESSAGE, getPlan, type Plan, type PlanRequest } from '../../api/catalogApi.ts'
import type { ProblemDetails } from '../../api/client.ts'
import { errorsFor, generalMessages } from '../../api/problems.ts'
import { fractionToPercent, percentToFraction, todayUtc } from '../../format.ts'
import { FieldErrors } from '../FieldErrors.tsx'
import { TiersEditor, type TierRow } from './TiersEditor.tsx'

export type FormSession = { mode: 'create' } | { mode: 'version'; plan: Plan; etag: string }

export function PlanForm({
  session, onSaved, onCancel,
}: { session: FormSession; onSaved: (message: string) => void; onCancel: () => void }) {
  const editing = session.mode === 'version' ? session.plan : null

  // One key per opening of the form: a retry after an error sends the same key.
  const [idempotencyKey] = useState(() => crypto.randomUUID())
  const [etag, setEtag] = useState(session.mode === 'version' ? session.etag : '')

  const [name, setName] = useState(editing?.name ?? '')
  const [discount, setDiscount] = useState(editing ? String(fractionToPercent(editing.discount)) : '0')
  const [effectiveFrom, setEffectiveFrom] = useState(todayUtc())
  const [tiers, setTiers] = useState<TierRow[]>(
    editing
      ? editing.tiers.map((t) => ({ threshold: t.threshold == null ? '' : String(t.threshold), price: String(t.pricePerKwh) }))
      : [{ threshold: '', price: '' }],
  )

  const [problem, setProblem] = useState<ProblemDetails | undefined>()
  const [message, setMessage] = useState<string | null>(null)
  const [conflict, setConflict] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const submittingRef = useRef(false)

  const knownFields = [
    'name', 'discount', 'effectiveFrom', 'tiers',
    ...tiers.flatMap((_, i) => [`tiers[${i}].threshold`, `tiers[${i}].pricePerKwh`]),
  ]

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (submittingRef.current) return
    submittingRef.current = true
    setSubmitting(true)
    setProblem(undefined)
    setMessage(null)
    setConflict(false)

    // Numbers are sent as entered (the discount as a fraction); the last tier never has a threshold.
    const body: PlanRequest = {
      name,
      discount: percentToFraction(Number(discount)),
      effectiveFrom,
      tiers: tiers.map((row, i) => ({
        threshold: i === tiers.length - 1 || row.threshold.trim() === '' ? null : Number(row.threshold),
        pricePerKwh: Number(row.price),
      })),
    }

    const response = editing ? await addVersion(editing.id, body, etag) : await createPlan(body, idempotencyKey)

    submittingRef.current = false
    setSubmitting(false)

    if (response.ok) {
      onSaved(editing ? `Version ${String(response.body.versionNo)} saved` : 'Plan created')
    } else if (response.status === 409) {
      setConflict(true)
    } else if (response.status === 400) {
      setProblem(response.body)
    } else {
      setMessage(response.body?.title ?? `Unexpected error (HTTP ${response.status}).`)
    }
  }

  // Fetches the plan again for its new ETag; the values in the form stay.
  async function reload() {
    if (!editing) return
    const response = await getPlan(editing.id)
    const fresh = response.headers.get('ETag')
    if (!response.ok) {
      setMessage(response.body?.title ?? 'The plan could not be reloaded.')
    } else if (!fresh) {
      setMessage('The plan could not be reloaded.')
    } else {
      setEtag(fresh)
      setConflict(false)
      setMessage(null)
    }
  }

  const general = generalMessages(problem, knownFields)

  return (
    <form className="panel plan-form" onSubmit={(e) => void submit(e)}>
      <h3>{editing ? `New version of "${editing.name}"` : 'New plan'}</h3>

      <label>
        Name
        <input required value={name} onChange={(e) => setName(e.target.value)} />
        <FieldErrors messages={errorsFor(problem, 'name')} />
      </label>

      <label>
        Discount (%)
        <input
          type="number" step="0.01" min="0" max="100" required
          value={discount} onChange={(e) => setDiscount(e.target.value)}
        />
        <span className="hint">Percentage of the energy sum: 5 = 5%</span>
        <FieldErrors messages={errorsFor(problem, 'discount')} />
      </label>

      <label>
        Effective from (UTC)
        <input type="date" required value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} />
        <FieldErrors messages={errorsFor(problem, 'effectiveFrom')} />
      </label>

      <TiersEditor tiers={tiers} onChange={setTiers} problem={problem} />

      {general.length > 0 && <p className="error">{general.join(' ')}</p>}
      {message && <p className="error">{message}</p>}
      {conflict && (
        <p className="error">
          {CONFLICT_MESSAGE} <button type="button" onClick={() => void reload()}>Reload</button>
        </p>
      )}

      <div className="actions">
        <button type="submit" disabled={submitting}>
          {submitting ? 'Saving…' : editing ? 'Save new version' : 'Create plan'}
        </button>
        <button type="button" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  )
}
