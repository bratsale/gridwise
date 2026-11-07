// @vitest-environment jsdom
import { act, useEffect } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { useOptimisticList } from './useOptimisticList.ts'

interface Row {
  id: string
  version: number
  label: string
}

const original: Row[] = [
  { id: 'a', version: 1, label: 'first' },
  { id: 'b', version: 1, label: 'second' },
]

type Api = ReturnType<typeof useOptimisticList<Row>>

// Renders the hook in a real React tree and gives the tests the latest value.
function mount() {
  const holder: { current: Api | null } = { current: null }
  function Probe() {
    const api = useOptimisticList<Row>((row) => row.id)
    useEffect(() => {
      holder.current = api
    })
    return null
  }
  const container = document.createElement('div')
  const root: Root = createRoot(container)
  act(() => root.render(<Probe />))
  return { api: () => holder.current as Api, root }
}

let mounted: ReturnType<typeof mount>

beforeEach(() => {
  ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
  mounted = mount()
  act(() => mounted.api().setItems(original))
})

afterEach(() => {
  act(() => mounted.root.unmount())
})

describe('useOptimisticList', () => {
  it('shows the expected row at once, marked as pending', () => {
    act(() => {
      mounted.api().optimistic.begin({ id: 'a', version: 2, label: 'expected' })
    })
    expect(mounted.api().items).toEqual([{ id: 'a', version: 2, label: 'expected' }, original[1]])
    expect([...mounted.api().pending]).toEqual(['a'])
  })

  it('adds a new row when the key is not in the list', () => {
    act(() => {
      mounted.api().optimistic.begin({ id: 'new', version: 1, label: 'created' })
    })
    expect(mounted.api().items.map((r) => r.id)).toEqual(['new', 'a', 'b'])
    expect(mounted.api().pending.has('new')).toBe(true)
  })

  it('replaces the pending row with the server response on success', () => {
    let change!: ReturnType<Api['optimistic']['begin']>
    act(() => {
      change = mounted.api().optimistic.begin({ id: 'pending-1', version: 1, label: 'expected' })
    })
    act(() => change.commit({ id: 'real-id', version: 1, label: 'from server' }))
    expect(mounted.api().items).toEqual([{ id: 'real-id', version: 1, label: 'from server' }, ...original])
    expect(mounted.api().pending.size).toBe(0)
  })

  it('keeps the expected row when committed without a response (retire returns 204)', () => {
    let change!: ReturnType<Api['optimistic']['begin']>
    act(() => {
      change = mounted.api().optimistic.begin({ id: 'b', version: 2, label: 'retired' })
    })
    act(() => change.commit())
    expect(mounted.api().items[1]).toEqual({ id: 'b', version: 2, label: 'retired' })
    expect(mounted.api().pending.size).toBe(0)
  })

  it('restores the exact previous list on failure', () => {
    const before = mounted.api().items
    let change!: ReturnType<Api['optimistic']['begin']>
    act(() => {
      change = mounted.api().optimistic.begin({ id: 'a', version: 2, label: 'expected' })
    })
    act(() => change.rollback())
    expect(mounted.api().items).toBe(before) // the same array, not just an equal one
    expect(mounted.api().items).toEqual(original)
    expect(mounted.api().pending.size).toBe(0)
  })

  it('removes a created row again on failure', () => {
    let change!: ReturnType<Api['optimistic']['begin']>
    act(() => {
      change = mounted.api().optimistic.begin({ id: 'new', version: 1, label: 'created' })
    })
    act(() => change.rollback())
    expect(mounted.api().items).toEqual(original)
  })
})
