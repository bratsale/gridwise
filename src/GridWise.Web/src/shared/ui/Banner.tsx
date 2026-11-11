import type { ReactNode } from 'react'
import { detailOf, type Message } from '../i18n/message.ts'
import { useMessageText, useT } from '../i18n/useLanguage.ts'

// A coloured notice; onDismiss adds a dismiss button. With `message` it shows that message, and the API's own text behind it
// (its detail, in English as the server wrote it) as a smaller second line.
export function Banner({
  kind, children, message, onDismiss,
}: { kind: 'success' | 'info' | 'warning' | 'error'; children?: ReactNode; message?: Message; onDismiss?: () => void }) {
  const t = useT()
  const text = useMessageText()
  const detail = message ? detailOf(message) : undefined
  return (
    <div className={`banner ${kind}`}>
      <span>
        {message ? text(message) : children}
        {detail && <small className="banner-detail">{detail}</small>}
      </span>
      {onDismiss && <button type="button" className="icon" aria-label={t('common.dismiss')} onClick={onDismiss}>✕</button>}
    </div>
  )
}
