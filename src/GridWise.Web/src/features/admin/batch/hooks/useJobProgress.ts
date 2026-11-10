import { useEffect, useRef, useState } from 'react'
import { eventsUrl, isTerminal, type JobProgress } from '../api/batchApi.ts'

const EVENT_SOURCE_CLOSED = 2
const RETRY_MS = 3000

// Live progress of one job from the server-sent events stream. Every event carries the full current state, so the latest
// event is all there is to keep. After a connection error `reconnecting` is true until the next event arrives.
// The browser reconnects by itself after a dropped connection. But when its attempt gets an HTTP error (nginx answers 502
// while the batch service restarts) it gives up and the EventSource stays CLOSED: then a new one is opened every few seconds.
// The stream is closed on a terminal status and when the component goes away.
// The access token is part of the URL (EventSource cannot send headers): when it is renewed, the stream is opened again with
// the new one, so a later reconnect never uses an expired token. The progress shown stays until the next event.
export function useJobProgress(
  jobId: string,
  {
    onProgress, onTerminal, accessToken,
  }: { onProgress?: (progress: JobProgress) => void; onTerminal?: (progress: JobProgress) => void; accessToken?: string | null } = {},
) {
  const [progress, setProgress] = useState<JobProgress | null>(null)
  const [reconnecting, setReconnecting] = useState(false)

  // Callbacks are read when an event arrives, so a new function does not reopen the connection.
  const callbacks = useRef({ onProgress, onTerminal })
  useEffect(() => {
    callbacks.current = { onProgress, onTerminal }
  })

  // A finished job is not opened again when the token is renewed (only a new job id does that).
  const finished = useRef<string | null>(null)

  useEffect(() => {
    if (finished.current === jobId) return
    let source: EventSource | null = null
    let retry: ReturnType<typeof setTimeout> | undefined
    let stopped = false

    function connect() {
      const current = new EventSource(eventsUrl(jobId, accessToken))
      source = current

      current.onmessage = (event: MessageEvent<string>) => {
        const next = JSON.parse(event.data) as JobProgress
        setProgress(next)
        setReconnecting(false)
        callbacks.current.onProgress?.(next)
        if (isTerminal(next.status)) {
          stopped = true
          finished.current = jobId
          current.close()
          callbacks.current.onTerminal?.(next)
        }
      }

      current.onerror = () => {
        if (stopped) return
        setReconnecting(true)
        // CONNECTING: the browser is reconnecting by itself. CLOSED: it gave up, so try again with a new connection.
        if (current.readyState === EVENT_SOURCE_CLOSED) retry = setTimeout(connect, RETRY_MS)
      }
    }

    connect()

    return () => {
      stopped = true
      clearTimeout(retry)
      source?.close()
    }
  }, [jobId, accessToken])

  return { progress, reconnecting }
}
