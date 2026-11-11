// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { reveal, useRevealRow } from './reveal.ts'

function panel(html: string) {
  const element = document.createElement('form')
  element.innerHTML = html
  document.body.append(element)
  element.scrollIntoView = vi.fn()
  return element
}
const reducedMotion = (on: boolean) => vi.stubGlobal('matchMedia', (query: string) => ({ matches: on && query.includes('reduce') }))

describe('reveal', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  it('scrolls smoothly and focuses the first field that can be typed into', () => {
    reducedMotion(false)
    const form = panel('<input type="hidden"><input disabled id="a"><select id="b"></select><input id="c">')

    reveal(form, { focus: true })

    expect(form.scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' })
    expect(document.activeElement?.id).toBe('b')
  })

  it('jumps instead of scrolling smoothly when the user asked for less motion', () => {
    reducedMotion(true)
    const form = panel('<input id="a">')

    reveal(form, { block: 'center' })

    expect(form.scrollIntoView).toHaveBeenCalledWith({ behavior: 'auto', block: 'center' })
    expect(document.activeElement).toBe(document.body) // no focus unless asked for
  })

  it('does nothing without an element', () => {
    expect(() => reveal(null, { focus: true })).not.toThrow()
  })
})

describe('useRevealRow', () => {
  it('scrolls to the row once it exists, and only once per key', async () => {
    const { act, createElement, useRef } = await import('react')
    const { createRoot } = await import('react-dom/client')
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    reducedMotion(false)
    const scrolled: string[] = []
    Element.prototype.scrollIntoView = function () { scrolled.push((this as HTMLElement).dataset.key ?? '?') }

    // A list whose rows come from props, like PlanList: the row of a new plan appears only after the list reloads.
    function List({ rows, flash }: { rows: string[]; flash: string | null }) {
      const ref = useRef<HTMLDivElement>(null)
      useRevealRow(ref, flash, rows)
      return createElement('div', { ref }, rows.map((key) => createElement('div', { key, 'data-key': key })))
    }
    const host = document.createElement('div')
    document.body.append(host)
    const root = createRoot(host)

    await act(async () => root.render(createElement(List, { rows: ['a'], flash: 'b' })))
    expect(scrolled).toEqual([]) // the row is not there yet

    await act(async () => root.render(createElement(List, { rows: ['a', 'b'], flash: 'b' })))
    await act(async () => root.render(createElement(List, { rows: ['a', 'b', 'c'], flash: 'b' })))
    expect(scrolled).toEqual(['b']) // scrolled when it appeared, not again on the next reload

    await act(async () => root.render(createElement(List, { rows: ['a', 'b', 'c'], flash: null })))
    await act(async () => root.render(createElement(List, { rows: ['a', 'b', 'c'], flash: 'b' })))
    expect(scrolled).toEqual(['b', 'b']) // a later flash of the same row scrolls again

    await act(async () => root.unmount())
  })
})
