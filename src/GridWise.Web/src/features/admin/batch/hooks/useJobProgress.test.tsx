// @vitest-environment jsdom
import { act, useEffect } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { JobProgress } from '../api/batchApi.ts'
import { useJobProgress } from './useJobProgress.ts'

// Just enough of EventSource for the hook: handlers, readyState and close().
class FakeEventSource {
  static instances: FakeEventSource[] = []
  onmessage: ((event: MessageEvent<string>) => void) | null = null
  onerror: (() => void) | null = null
  readyState = 1
  closed = false

  readonly url: string

  constructor(url: string) {
    this.url = url
    FakeEventSource.instances.push(this)
  }

  close() {
    this.closed = true
    this.readyState = 2
  }

  send(progress: JobProgress) {
    this.onmessage?.({ data: JSON.stringify(progress) } as MessageEvent<string>)
  }

  // The browser keeps the connection object and reconnects by itself: readyState goes back to CONNECTING.
  fail() {
    this.readyState = 0
    this.onerror?.()
  }
}

const running = (completedChunks: number): JobProgress => ({
  status: 'Running', totalChunks: 4, completedChunks, processedRows: completedChunks * 500, failedRows: completedChunks,
})

type Hook = ReturnType<typeof useJobProgress>

function mount(jobId: string, callbacks: Parameters<typeof useJobProgress>[1] = {}) {
  const holder: { current: Hook | null } = { current: null }
  function Probe({ id, token }: { id: string; token?: string }) {
    const value = useJobProgress(id, { ...callbacks, accessToken: token ?? callbacks.accessToken })
    useEffect(() => {
      holder.current = value
    })
    return null
  }
  const container = document.createElement('div')
  const root: Root = createRoot(container)
  act(() => root.render(<Probe id={jobId} />))
  return {
    holder, root,
    rerender: (id: string, token?: string) => act(() => root.render(<Probe id={id} token={token} />)),
  }
}

describe('useJobProgress', () => {
  it('passes the access token in the URL and reconnects with the new token after a renewal', () => {
    const { holder, rerender } = mount('job-1', { accessToken: 'token-1' })
    const first = FakeEventSource.instances[0]
    expect(first.url).toBe('/api/batch/jobs/job-1/events?access_token=token-1')
    act(() => first.send(running(1)))

    rerender('job-1', 'token-2')

    expect(first.closed).toBe(true)
    expect(FakeEventSource.instances).toHaveLength(2)
    expect(FakeEventSource.instances[1].url).toBe('/api/batch/jobs/job-1/events?access_token=token-2')
    expect(holder.current?.progress?.completedChunks).toBe(1) // the progress shown stays until the next event
  })

  it('does not reopen a finished job when the token is renewed', () => {
    const { rerender } = mount('job-2', { accessToken: 'token-1' })
    act(() => FakeEventSource.instances[0].send({ ...running(4), status: 'Completed' }))

    rerender('job-2', 'token-2')

    expect(FakeEventSource.instances).toHaveLength(1)
  })

  beforeEach(() => {
    FakeEventSource.instances = []
    vi.stubGlobal('EventSource', FakeEventSource)
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('opens the stream of the job and applies each event', () => {
    const { holder } = mount('job-1')
    const source = FakeEventSource.instances[0]
    expect(source.url).toBe('/api/batch/jobs/job-1/events')
    expect(holder.current?.progress).toBeNull()

    act(() => source.send(running(1)))
    expect(holder.current?.progress).toEqual(running(1))

    act(() => source.send(running(2)))
    expect(holder.current?.progress).toEqual(running(2))
    expect(source.closed).toBe(false)
  })

  it('tells the caller about every event', () => {
    const onProgress = vi.fn()
    mount('job-1', { onProgress })

    act(() => FakeEventSource.instances[0].send(running(1)))

    expect(onProgress).toHaveBeenCalledWith(running(1))
  })

  it('shows "reconnecting" after an error and clears it on the next event', () => {
    const { holder } = mount('job-1')
    const source = FakeEventSource.instances[0]
    act(() => source.send(running(1)))
    expect(holder.current?.reconnecting).toBe(false)

    act(() => source.fail())
    expect(holder.current?.reconnecting).toBe(true)
    expect(holder.current?.progress).toEqual(running(1)) // what we knew stays on screen
    expect(source.closed).toBe(false) // the browser reconnects by itself, we do not touch the connection

    act(() => source.send(running(2)))
    expect(holder.current?.reconnecting).toBe(false)
    expect(holder.current?.progress).toEqual(running(2))
  })

  describe('when the browser gives up (readyState CLOSED, for example after an HTTP 502 during a restart)', () => {
    beforeEach(() => {
      vi.useFakeTimers()
    })

    afterEach(() => {
      vi.useRealTimers()
    })

    it('opens a new connection after a pause, keeps "reconnecting" and resumes with the next event', () => {
      const { holder } = mount('job-1')
      const first = FakeEventSource.instances[0]
      act(() => first.send(running(1)))

      act(() => {
        first.readyState = 2
        first.onerror?.()
      })
      expect(holder.current?.reconnecting).toBe(true)
      expect(FakeEventSource.instances).toHaveLength(1) // not at once

      act(() => vi.advanceTimersByTime(3000))
      expect(FakeEventSource.instances).toHaveLength(2)
      expect(FakeEventSource.instances[1].url).toBe('/api/batch/jobs/job-1/events')
      expect(holder.current?.reconnecting).toBe(true) // still waiting for an event

      act(() => FakeEventSource.instances[1].send(running(3)))
      expect(holder.current?.reconnecting).toBe(false)
      expect(holder.current?.progress).toEqual(running(3))
    })

    it('tries again while the service is still down', () => {
      mount('job-1')
      for (let attempt = 1; attempt <= 3; attempt++) {
        const source = FakeEventSource.instances[attempt - 1]
        act(() => {
          source.readyState = 2
          source.onerror?.()
        })
        act(() => vi.advanceTimersByTime(3000))
        expect(FakeEventSource.instances).toHaveLength(attempt + 1)
      }
    })

    it('stops trying when the component goes away', () => {
      const { root } = mount('job-1')
      const source = FakeEventSource.instances[0]
      act(() => {
        source.readyState = 2
        source.onerror?.()
      })

      act(() => root.unmount())
      act(() => vi.advanceTimersByTime(10_000))

      expect(FakeEventSource.instances).toHaveLength(1)
    })
  })

  it.each(['Completed', 'Cancelled', 'Failed'] as const)('closes the stream on the terminal status %s', (status) => {
    const onTerminal = vi.fn()
    const { holder } = mount('job-1', { onTerminal })
    const source = FakeEventSource.instances[0]
    const final: JobProgress = { status, totalChunks: 4, completedChunks: 4, processedRows: 1997, failedRows: 3 }

    act(() => source.send(final))

    expect(holder.current?.progress).toEqual(final)
    expect(source.closed).toBe(true)
    expect(onTerminal).toHaveBeenCalledExactlyOnceWith(final)
  })

  it('closes the stream when the component goes away', () => {
    const { root } = mount('job-1')
    const source = FakeEventSource.instances[0]
    expect(source.closed).toBe(false)

    act(() => root.unmount())

    expect(source.closed).toBe(true)
  })

  it('opens a new stream when the job changes and closes the old one', () => {
    const { rerender } = mount('job-1')

    rerender('job-2')

    expect(FakeEventSource.instances.map((s) => [s.url, s.closed])).toEqual([
      ['/api/batch/jobs/job-1/events', true],
      ['/api/batch/jobs/job-2/events', false],
    ])
  })
})
