import type { FormEvent } from 'react'
import { useFormat, useMessageText, useT } from '../../../../shared/i18n/LanguageProvider.tsx'
import type { Message } from '../../../../shared/i18n/message.ts'
import { Banner } from '../../../../shared/ui/Banner.tsx'
import { ConfirmDialog } from '../../../../shared/ui/ConfirmDialog.tsx'
import { FieldErrors } from '../../../../shared/ui/FieldErrors.tsx'
import type { Plan } from '../api/plansApi.ts'

export function RetirePanel({
  plan, effectiveFrom, submitting, confirming, message, general, dateErrors,
  onDate, onAsk, onConfirm, onCancelConfirm, onCancel,
}: {
  plan: Plan
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
      <h3>{t('retire.title', { name: plan.name })}</h3>
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
        message={t('retire.confirm', { name: plan.name, date: format.date(effectiveFrom) })}
        confirmLabel={t('retire.confirmButton')}
        danger
        onConfirm={onConfirm}
        onCancel={onCancelConfirm}
      />
    </form>
  )
}
