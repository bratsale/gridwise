import type { ReactNode } from 'react'
import { useT } from '../i18n/LanguageProvider.tsx'

// A coloured notice with an icon; onDismiss adds a dismiss button.
export function Banner({
  kind, children, onDismiss,
}: { kind: 'success' | 'info' | 'warning' | 'error'; children: ReactNode; onDismiss?: () => void }) {
  const t = useT()
  return (
    <div className={`banner ${kind}`}>
      <span>{children}</span>
      {onDismiss && <button type="button" className="icon" aria-label={t('common.dismiss')} onClick={onDismiss}>✕</button>}
    </div>
  )
}
