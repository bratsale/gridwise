// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LanguageProvider } from '../i18n/LanguageProvider.tsx'
import { AUTO_DISMISS_MS, Banner, FADE_MS } from './Banner.tsx'

;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true

let host: HTMLDivElement
let root: Root
const reducedMotion = (on: boolean) => vi.stubGlobal('matchMedia', (query: string) => ({ matches: on && query.includes('reduce') }))
const show = (banner: React.ReactElement) => act(() => root.render(<LanguageProvider>{banner}</LanguageProvider>))
const wait = (ms: number) => act(() => { vi.advanceTimersByTime(ms) })
const banner = () => host.querySelector('.banner') as HTMLElement | null
// The fade timer starts when React has re-rendered the banner as leaving, so it needs its own step after the countdown.
const waitToClose = async (ms: number) => { await wait(ms); await wait(FADE_MS) }

describe('Banner', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    reducedMotion(false)
    host = document.createElement('div')
    document.body.append(host)
    root = createRoot(host)
  })
  afterEach(() => {
    act(() => root.unmount())
    host.remove()
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('a success banner closes itself after 6 s, fading out first', async () => {
    const onDismiss = vi.fn()
    await show(<Banner kind="success" onDismiss={onDismiss}>Plan created.</Banner>)

    await wait(AUTO_DISMISS_MS - 1)
    expect(banner()?.className).not.toContain('leaving')
    await wait(1)
    expect(banner()?.className).toContain('leaving')
    expect(onDismiss).not.toHaveBeenCalled()
    await wait(FADE_MS)
    expect(onDismiss).toHaveBeenCalledOnce()
  })

  it('hovering pauses the timer, which then goes on with the time that was left', async () => {
    const onDismiss = vi.fn()
    await show(<Banner kind="info" onDismiss={onDismiss}>Changes reach recommendations in a few seconds.</Banner>)

    await wait(4000)
    await act(() => { banner()!.dispatchEvent(new MouseEvent('mouseover', { bubbles: true })) })
    await wait(20_000)
    expect(onDismiss).not.toHaveBeenCalled()

    await act(() => { banner()!.dispatchEvent(new MouseEvent('mouseout', { bubbles: true, relatedTarget: document.body })) })
    await wait(AUTO_DISMISS_MS - 4000 - 1)
    expect(banner()?.className).not.toContain('leaving')
    await waitToClose(1)
    expect(onDismiss).toHaveBeenCalledOnce()
  })

  it('keyboard focus in the banner pauses the timer too', async () => {
    const onDismiss = vi.fn()
    await show(<Banner kind="success" onDismiss={onDismiss}>Saved.</Banner>)

    await act(() => (host.querySelector('button') as HTMLButtonElement).focus())
    await wait(20_000)
    expect(onDismiss).not.toHaveBeenCalled()

    await act(() => (host.querySelector('button') as HTMLButtonElement).blur())
    await waitToClose(AUTO_DISMISS_MS)
    expect(onDismiss).toHaveBeenCalledOnce()
  })

  it('error and warning banners stay until they are closed', async () => {
    const onDismiss = vi.fn()
    await show(<><Banner kind="error" onDismiss={onDismiss}>Upload failed.</Banner><Banner kind="warning">Reconnecting…</Banner></>)

    await wait(60_000)
    expect(host.querySelectorAll('.banner')).toHaveLength(2)
    expect(onDismiss).not.toHaveBeenCalled()
    expect(host.querySelectorAll('button')).toHaveLength(1) // only where the page lets it be closed

    await act(() => (host.querySelector('button') as HTMLButtonElement).click())
    await wait(FADE_MS)
    expect(onDismiss).toHaveBeenCalledOnce()
  })

  it('a banner without onDismiss hides itself, and a persistent one stays without a close button', async () => {
    await show(<><Banner kind="success">Accepted.</Banner><Banner kind="info" persistent>Retired from Dec 1, 2026.</Banner></>)
    expect(host.querySelectorAll('button')).toHaveLength(1)

    await waitToClose(AUTO_DISMISS_MS)
    expect(host.textContent).not.toContain('Accepted.')
    expect(host.textContent).toContain('Retired from Dec 1, 2026.')
  })

  it('with reduced motion it closes without the fade', async () => {
    reducedMotion(true)
    const onDismiss = vi.fn()
    await show(<Banner kind="success" onDismiss={onDismiss}>Saved.</Banner>)

    await wait(AUTO_DISMISS_MS)
    await wait(0)
    expect(onDismiss).toHaveBeenCalledOnce()
  })
})
