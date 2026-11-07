import { useState, type FormEvent } from 'react'
import { CONFLICT_MESSAGE, getPlan, retirePlan, type Plan } from '../../api/catalogApi.ts'
import type { ProblemDetails } from '../../api/client.ts'
import { errorsFor, generalMessages } from '../../api/problems.ts'
import { todayUtc } from '../../format.ts'
import { ConfirmDialog } from '../ConfirmDialog.tsx'
import { FieldErrors } from '../FieldErrors.tsx'

export function RetirePanel({
  plan, onDone, onCancel,
}: { plan: Plan; onDone: (message: string) => void; onCancel: () => void }) {
  const [effectiveFrom, setEffectiveFrom] = useState(todayUtc())
  const [problem, setProblem] = useState<ProblemDetails | undefined>()
  const [message, setMessage] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [confirming, setConfirming] = useState(false)

  function ask(event: FormEvent) {
    event.preventDefault()
    if (!submitting) setConfirming(true)
  }

  async function retire() {
    setConfirming(false)
    if (submitting) return
    setSubmitting(true)
    setProblem(undefined)
    setMessage(null)

    // The current version is needed for If-Match, so load it first.
    const current = await getPlan(plan.id)
    const etag = current.headers.get('ETag')
    if (!current.ok || !etag) {
      setMessage((!current.ok && current.body?.title) || 'The plan could not be loaded.')
      setSubmitting(false)
      return
    }

    const response = await retirePlan(plan.id, effectiveFrom, etag)
    setSubmitting(false)

    if (response.ok) {
      onDone(`Plan retired from ${effectiveFrom}`)
    } else if (response.status === 409) {
      setMessage(CONFLICT_MESSAGE)
    } else if (response.status === 400) {
      setProblem(response.body)
    } else {
      setMessage(response.body?.title ?? `Unexpected error (HTTP ${response.status}).`)
    }
  }

  const general = generalMessages(problem, ['effectiveFrom'])

  return (
    <form className="panel" onSubmit={ask}>
      <h3>Retire "{plan.name}"</h3>
      <label>
        Retired from (UTC)
        <input type="date" required value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} />
        <FieldErrors messages={errorsFor(problem, 'effectiveFrom')} />
      </label>
      {general.length > 0 && <p className="error">{general.join(' ')}</p>}
      {message && <p className="error">{message}</p>}
      <div className="actions">
        <button type="submit" disabled={submitting}>{submitting ? 'Retiring…' : 'Retire plan'}</button>
        <button type="button" onClick={onCancel}>Cancel</button>
      </div>
      <ConfirmDialog
        open={confirming}
        message={`Retire "${plan.name}" from ${effectiveFrom}?`}
        confirmLabel="Retire"
        onConfirm={() => void retire()}
        onCancel={() => setConfirming(false)}
      />
    </form>
  )
}
