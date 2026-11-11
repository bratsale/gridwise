import { useEffect, useRef, useState, type ReactNode } from 'react'
import { detailOf, type Message } from '../i18n/message.ts'
import { useMessageText, useT } from '../i18n/useLanguage.ts'

type Kind = 'success' | 'info' | 'warning' | 'error'

export const AUTO_DISMISS_MS = 6000
export const FADE_MS = 200

// A coloured notice. With `message` it shows that message, and the API's own text behind it (its detail, in English as the server
// wrote it) as a smaller second line.
// Success and info banners close themselves after AUTO_DISMISS_MS. The timer pauses while the pointer is over the banner or the
// keyboard focus is in it, and goes on with the time that was left; they always have a close button. Closing fades the banner out
// (not with prefers-reduced-motion) and then calls onDismiss, so the page removes it, or, without onDismiss, hides it.
// Error and warning banners stay until the user closes them (when the page gives onDismiss) or the page removes them.
// `persistent` keeps a success or info banner that is part of the content, not a notification (for example "retired from ...").
export function Banner({
  kind, children, message, onDismiss, persistent = false,
}: { kind: Kind; children?: ReactNode; message?: Message; onDismiss?: () => void; persistent?: boolean }) {
  const t = useT()
  const text = useMessageText()
  const detail = message ? detailOf(message) : undefined
  const autoDismiss = (kind === 'success' || kind === 'info') && !persistent

  const [phase, setPhase] = useState<'shown' | 'leaving' | 'gone'>('shown')
  const [paused, setPaused] = useState(false)
  const remaining = useRef(AUTO_DISMISS_MS)
  const hovered = useRef(false)
  const focused = useRef(false)
  const dismiss = useRef(onDismiss)

  useEffect(() => {
    dismiss.current = onDismiss
  })

  // The countdown runs while the banner is shown and not paused; pausing keeps the time that was left.
  useEffect(() => {
    if (!autoDismiss || paused || phase !== 'shown') return
    const started = Date.now()
    const timer = setTimeout(() => setPhase('leaving'), remaining.current)
    return () => {
      clearTimeout(timer)
      remaining.current = Math.max(0, remaining.current - (Date.now() - started))
    }
  }, [autoDismiss, paused, phase])

  useEffect(() => {
    if (phase !== 'leaving') return
    const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
    const timer = setTimeout(() => (dismiss.current ? dismiss.current() : setPhase('gone')), reduced ? 0 : FADE_MS)
    return () => clearTimeout(timer)
  }, [phase])

  if (phase === 'gone') return null
  const closable = Boolean(onDismiss) || autoDismiss
  return (
    <div
      className={`banner ${kind}${phase === 'leaving' ? ' leaving' : ''}`}
      onMouseEnter={() => { hovered.current = true; setPaused(true) }}
      onMouseLeave={() => { hovered.current = false; setPaused(focused.current) }}
      onFocus={() => { focused.current = true; setPaused(true) }}
      onBlur={(e) => {
        if (e.currentTarget.contains(e.relatedTarget as Node | null)) return // focus moved inside the banner
        focused.current = false
        setPaused(hovered.current)
      }}
    >
      <span>
        {message ? text(message) : children}
        {detail && <small className="banner-detail">{detail}</small>}
      </span>
      {closable && <button type="button" className="icon" aria-label={t('common.dismiss')} onClick={() => setPhase('leaving')}>✕</button>}
    </div>
  )
}
