import { useState, type FormEvent } from 'react'
import type { ApiResponse, ProblemDetails } from '../api/client.ts'
import { failureMessage } from '../api/failure.ts'
import { errorsFor, generalMessages } from '../api/problems.ts'
import { todayUtc } from '../format.ts'
import { type Message } from '../i18n/message.ts'
import type { Optimistic } from './useOptimisticList.ts'

// Retire with confirmation: ask, load the current version for its ETag, send DELETE with If-Match.
// The list shows the expected result at once and is put back if the request fails.
export function useRetireFlow<T>({
  load, retire: send, expected, optimistic, loadError, conflict, onDone,
}: {
  load: () => Promise<ApiResponse<T>>
  retire: (effectiveFrom: string, etag: string) => Promise<ApiResponse<undefined>>
  expected: (effectiveFrom: string) => T
  optimistic: Optimistic<T>
  loadError: Message
  conflict: Message
  onDone: (effectiveFrom: string) => void
}) {
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

    const change = optimistic.begin(expected(effectiveFrom))

    // The current version is needed for If-Match, so load it first.
    const current = await load()
    const etag = current.headers.get('ETag')
    if (!current.ok || !etag) {
      change.rollback()
      setMessage(current.ok ? loadError : failureMessage(current, loadError))
      setSubmitting(false)
      return
    }

    const response = await send(effectiveFrom, etag)
    setSubmitting(false)

    if (response.ok) change.commit()
    else change.rollback()

    if (response.ok) {
      onDone(effectiveFrom)
    } else if (response.status === 409) {
      setMessage(conflict)
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
