import { useState } from 'react'
import { failureMessage } from '../../../../shared/api/failure.ts'
import { msg, type Message } from '../../../../shared/i18n/message.ts'
import { cancelJob } from '../api/batchApi.ts'

export function useCancelJob(jobId: string, onChanged: () => void) {
  const [confirming, setConfirming] = useState(false)
  const [cancelling, setCancelling] = useState(false)
  const [error, setError] = useState<Message | null>(null)

  async function confirm() {
    setConfirming(false)
    setCancelling(true)
    setError(null)
    const response = await cancelJob(jobId)
    setCancelling(false)

    if (!response.ok) {
      setError(response.status === 409 ? msg('batch.cancel.finished') : failureMessage(response, msg('batch.cancel.failed')))
    }
    onChanged() // either way the list shows the real state now
  }

  return { confirming, cancelling, error, ask: () => setConfirming(true), dismiss: () => setConfirming(false), confirm }
}
