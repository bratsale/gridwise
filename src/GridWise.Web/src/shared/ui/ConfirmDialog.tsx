import { useEffect, useRef } from 'react'
import { useT } from '../i18n/LanguageProvider.tsx'

// Modal confirmation built on the native <dialog>. Escape and Cancel call onCancel, OK calls onConfirm.
export function ConfirmDialog({
  open, message, confirmLabel, danger = false, onConfirm, onCancel,
}: { open: boolean; message: string; confirmLabel?: string; danger?: boolean; onConfirm: () => void; onCancel: () => void }) {
  const t = useT()
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  return (
    <dialog
      ref={ref}
      className="confirm"
      // Escape fires "cancel"; let the parent state decide when the dialog closes.
      onCancel={(e) => { e.preventDefault(); onCancel() }}
    >
      <p>{message}</p>
      <div className="actions">
        <button type="button" autoFocus onClick={onCancel}>{t('common.cancel')}</button>
        <button type="button" className={danger ? 'danger' : 'primary'} onClick={onConfirm}>{confirmLabel ?? t('common.ok')}</button>
      </div>
    </dialog>
  )
}
