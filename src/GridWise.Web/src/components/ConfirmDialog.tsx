import { useEffect, useRef } from 'react'

// Modal confirmation built on the native <dialog>. Escape and Cancel call onCancel, OK calls onConfirm.
export function ConfirmDialog({
  open, message, confirmLabel = 'OK', onConfirm, onCancel,
}: { open: boolean; message: string; confirmLabel?: string; onConfirm: () => void; onCancel: () => void }) {
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
        <button type="button" autoFocus onClick={onCancel}>Cancel</button>
        <button type="button" onClick={onConfirm}>{confirmLabel}</button>
      </div>
    </dialog>
  )
}
