import { useEffect, useRef, type RefObject } from 'react'

// Brings a panel or row into view: smoothly, unless the user asked for less motion. With `focus`, the panel's first field that can
// be typed into gets the focus (without a second jump), so the keyboard is where the eyes are.
export function reveal(element: Element | null | undefined, { focus = false, block = 'start' }: { focus?: boolean; block?: ScrollLogicalPosition } = {}) {
  if (!element) return
  const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
  element.scrollIntoView?.({ behavior: reduced ? 'auto' : 'smooth', block })
  if (focus) element.querySelector<HTMLElement>('input:not([disabled]):not([type=hidden]), select:not([disabled]), textarea:not([disabled])')?.focus({ preventScroll: true })
}

// Scrolls a list to its row with data-key=`key` once that row exists: after an edit at once, after creating something only when the
// reloaded list contains the new row. `rows` is the list's data, so the check runs again when it changes. Each key scrolls once.
export function useRevealRow(container: RefObject<HTMLElement | null>, key: string | null, rows: unknown) {
  const done = useRef<string | null>(null)
  useEffect(() => {
    if (!key) {
      done.current = null
      return
    }
    if (done.current === key) return
    const row = Array.from(container.current?.querySelectorAll<HTMLElement>('[data-key]') ?? []).find((r) => r.dataset.key === key)
    if (!row) return
    done.current = key
    reveal(row, { block: 'center' })
  }, [container, key, rows])
}
