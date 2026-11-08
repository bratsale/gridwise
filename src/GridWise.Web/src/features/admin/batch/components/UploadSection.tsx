import { useRef, useState, type DragEvent } from 'react'
import { useMessageText, useT } from '../../../../shared/i18n/LanguageProvider.tsx'
import { Banner } from '../../../../shared/ui/Banner.tsx'
import { useUpload } from '../hooks/useUpload.ts'

export function UploadSection({ onAccepted }: { onAccepted: (jobId: string) => void }) {
  const t = useT()
  const text = useMessageText()
  const { uploading, error, upload, clearError } = useUpload(onAccepted)
  const input = useRef<HTMLInputElement>(null)
  const [dragging, setDragging] = useState(false)

  function pick(file: File | undefined) {
    if (file && !uploading) void upload(file)
    if (input.current) input.current.value = '' // choosing the same file again must trigger a change
  }

  function onDrop(event: DragEvent) {
    event.preventDefault()
    setDragging(false)
    pick(event.dataTransfer.files[0])
  }

  return (
    <section className="card">
      <h3>{t('batch.upload.title')}</h3>
      <div
        className={`dropzone${dragging ? ' dragging' : ''}`}
        onDragOver={(e) => { e.preventDefault(); setDragging(true) }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
      >
        <span>{uploading ? t('batch.upload.uploading') : t('batch.upload.drop')}</span>
        <button type="button" disabled={uploading} onClick={() => input.current?.click()}>{t('batch.upload.choose')}</button>
        <input
          ref={input} type="file" accept=".csv,text/csv" hidden aria-label={t('batch.upload.choose')}
          onChange={(e) => pick(e.target.files?.[0])}
        />
      </div>
      <p className="hint">
        {t('batch.upload.format')} <a href="/sample-batch.csv" download>{t('batch.upload.sample')}</a>
      </p>
      {error && <Banner kind="error" onDismiss={clearError}>{text(error)}</Banner>}
    </section>
  )
}
