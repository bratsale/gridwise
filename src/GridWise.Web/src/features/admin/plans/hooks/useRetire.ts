import { useState, type FormEvent } from 'react'
import type { ProblemDetails } from '../../../../shared/api/client.ts'
import { failureMessage } from '../../../../shared/api/failure.ts'
import { errorsFor, generalMessages } from '../../../../shared/api/problems.ts'
import { todayUtc } from '../../../../shared/format.ts'
import { msg, type Message } from '../../../../shared/i18n/message.ts'
import { getPlan, retirePlan, type Plan } from '../api/plansApi.ts'
import type { Notice } from './usePlans.ts'

export function useRetire(plan: Plan, onDone: (notice: Notice) => void) {
  const [effectiveFrom, setEffectiveFrom] = useState(todayUtc())
  const [problem, setProblem] = useState<ProblemDetails | undefined>()
  const [message, setMessage] = useState<Message | null>(null)
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
      setMessage(current.ok ? msg('plans.planLoadError') : failureMessage(current, msg('plans.planLoadError')))
      setSubmitting(false)
      return
    }

    const response = await retirePlan(plan.id, effectiveFrom, etag)
    setSubmitting(false)

    if (response.ok) {
      onDone({ kind: 'retired', date: effectiveFrom })
    } else if (response.status === 409) {
      setMessage(msg('plans.conflict'))
    } else if (response.status === 400) {
      setProblem(response.body)
    } else {
      setMessage(failureMessage(response))
    }
  }

  return {
    effectiveFrom, setEffectiveFrom, submitting, confirming, message,
    dateErrors: errorsFor(problem, 'effectiveFrom'), general: generalMessages(problem, ['effectiveFrom']),
    ask, retire, cancelConfirm: () => setConfirming(false),
  }
}
