import { useCallback, useEffect, useState } from 'react'
import { failureMessage } from '../../../../shared/api/failure.ts'
import { msg, type Message } from '../../../../shared/i18n/message.ts'
import { listJobs, type Job, type JobProgress } from '../api/batchApi.ts'

export function useJobs() {
  const [jobs, setJobs] = useState<Job[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<Message | null>(null)

  // `silent` refreshes in the background: the list stays on screen instead of turning into a spinner.
  const load = useCallback(async (silent = false) => {
    if (!silent) setLoading(true)
    const response = await listJobs()
    if (response.ok) {
      setJobs(response.body)
      setError(null)
    } else {
      setError(failureMessage(response, msg('batch.jobs.loadError')))
    }
    setLoading(false)
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  // The live stream of the open job keeps its row up to date.
  const applyProgress = useCallback((id: string, progress: JobProgress) => {
    setJobs((current) => current.map((job) => (job.id === id ? { ...job, ...progress } : job)))
  }, [])

  return { jobs, loading, error, load, applyProgress }
}
