import { useCallback, useEffect, useState } from 'react'
import { failureMessage } from '../../../../shared/api/failure.ts'
import { msg, type Message } from '../../../../shared/i18n/message.ts'
import { listJobs, type Job, type JobProgress } from '../api/batchApi.ts'

export function useJobs() {
  const [jobs, setJobs] = useState<Job[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<Message | null>(null)

  // Shows the response of the list request. It is called when the response arrives, never synchronously from an effect.
  const apply = useCallback((response: Awaited<ReturnType<typeof listJobs>>) => {
    if (response.ok) {
      setJobs(response.body)
      setError(null)
    } else {
      setError(failureMessage(response, msg('batch.jobs.loadError')))
    }
    setLoading(false)
  }, [])

  // `silent` refreshes in the background: the list stays on screen instead of turning into a spinner.
  const load = useCallback(async (silent = false) => {
    if (!silent) setLoading(true)
    apply(await listJobs())
  }, [apply])

  // `loading` starts as true, so the first fetch does not need to set it.
  useEffect(() => {
    void listJobs().then(apply)
  }, [apply])

  // The live stream of the open job keeps its row up to date.
  const applyProgress = useCallback((id: string, progress: JobProgress) => {
    setJobs((current) => current.map((job) => (job.id === id ? { ...job, ...progress } : job)))
  }, [])

  return { jobs, loading, error, load, applyProgress }
}
