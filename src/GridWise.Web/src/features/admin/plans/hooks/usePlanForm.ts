import { useRef, useState, type FormEvent } from 'react'
import type { ProblemDetails } from '../../../../shared/api/client.ts'
import { failureMessage } from '../../../../shared/api/failure.ts'
import { errorsFor, generalMessages } from '../../../../shared/api/problems.ts'
import { fractionToPercent, percentToFraction, todayUtc } from '../../../../shared/format.ts'
import { msg, type Message } from '../../../../shared/i18n/message.ts'
import { addVersion, createPlan, getPlan, type Plan, type PlanRequest } from '../api/plansApi.ts'
import { validatePlan, type TierRow } from '../validation.ts'
import type { Notice } from './usePlans.ts'

export type FormSession = { mode: 'create' } | { mode: 'version'; plan: Plan; etag: string }

export function usePlanForm(session: FormSession, onSaved: (notice: Notice) => void) {
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
  const [message, setMessage] = useState<Message | null>(null)
  const [conflict, setConflict] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const submittingRef = useRef(false)
  const [touched, setTouched] = useState<ReadonlySet<string>>(new Set())
  const [submitted, setSubmitted] = useState(false)

  const clientErrors = validatePlan({ name, discount, effectiveFrom, tiers }, todayUtc())

  // Client errors show after submit or once the field was touched; server errors sit at the same place.
  function fieldErrors(field: string): Message[] {
    const client = submitted || touched.has(field) ? clientErrors[field] : undefined
    return client ? [client] : errorsFor(problem, field)
  }

  function touch(field: string) {
    setTouched((current) => new Set(current).add(field))
  }

  const knownFields = [
    'name', 'discount', 'effectiveFrom', 'tiers',
    ...tiers.flatMap((_, i) => [`tiers[${i}].threshold`, `tiers[${i}].pricePerKwh`]),
  ]

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (submittingRef.current) return
    setSubmitted(true)
    if (Object.keys(clientErrors).length > 0) return

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
      onSaved(editing ? { kind: 'versionSaved', version: String(response.body.versionNo) } : { kind: 'created' })
    } else if (response.status === 409) {
      setConflict(true)
    } else if (response.status === 400) {
      setProblem(response.body)
    } else {
      setMessage(failureMessage(response))
    }
  }

  // Fetches the plan again for its new ETag; the values in the form stay.
  async function reload() {
    if (!editing) return
    const response = await getPlan(editing.id)
    const fresh = response.headers.get('ETag')
    if (!response.ok) {
      setMessage(failureMessage(response, msg('plans.planReloadError')))
    } else if (!fresh) {
      setMessage(msg('plans.planReloadError'))
    } else {
      setEtag(fresh)
      setConflict(false)
      setMessage(null)
    }
  }

  return {
    editing, name, setName, discount, setDiscount, effectiveFrom, setEffectiveFrom, tiers, setTiers,
    submitting, conflict, message, general: generalMessages(problem, knownFields), problem,
    fieldErrors, touch, submit, reload,
  }
}
