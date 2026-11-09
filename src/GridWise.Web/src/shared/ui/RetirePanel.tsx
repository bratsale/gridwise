import type { FormEvent } from 'react'
import { useFormat, useMessageText, useT } from '../i18n/useLanguage.ts'
import type { Message } from '../i18n/message.ts'
import { Banner } from './Banner.tsx'
import { ConfirmDialog } from './ConfirmDialog.tsx'
import { FieldErrors } from './FieldErrors.tsx'

export function RetirePanel({
  name, effectiveFrom, submitting, confirming, message, general, dateErrors,
  onDate, onAsk, onConfirm, onCancelConfirm, onCancel,
}: {
  name: string
  effectiveFrom: string
  submitting: boolean
  confirming: boolean
  message: Message | null
  general: Message[]
  dateErrors: Message[]
  onDate: (value: string) => void
  onAsk: (event: FormEvent) => void
  onConfirm: () => void
  onCancelConfirm: () => void
  onCancel: () => void
}) {
  const t = useT()
  const text = useMessageText()
  const format = useFormat()

  return (
    <form className="panel" onSubmit={onAsk}>
      <h3>{t('retire.title', { name })}</h3>
      <label>
        {t('retire.from')}
        <input type="date" required value={effectiveFrom} onChange={(e) => onDate(e.target.value)} />
        <FieldErrors messages={dateErrors} />
      </label>
      {general.length > 0 && <Banner kind="error">{general.map(text).join(' ')}</Banner>}
      {message && <Banner kind="error">{text(message)}</Banner>}
      <div className="actions">
        <button type="submit" className="danger" disabled={submitting}>{submitting ? t('retire.submitting') : t('retire.submit')}</button>
        <button type="button" onClick={onCancel}>{t('common.cancel')}</button>
      </div>
      <ConfirmDialog
        open={confirming}
        message={t('retire.confirm', { name, date: format.date(effectiveFrom) })}
        confirmLabel={t('retire.confirmButton')}
        danger
        onConfirm={onConfirm}
        onCancel={onCancelConfirm}
      />
    </form>
  )
}
