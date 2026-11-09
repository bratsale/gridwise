import { useFormat, useMessageText, useT } from '../../../../shared/i18n/useLanguage.ts'
import { Banner } from '../../../../shared/ui/Banner.tsx'
import { ConfirmDialog } from '../../../../shared/ui/ConfirmDialog.tsx'
import { ProgressBar } from '../../../../shared/ui/ProgressBar.tsx'
import { errorsCsvUrl, isTerminal, resultsCsvUrl, type Job, type JobProgress } from '../api/batchApi.ts'
import { useCancelJob } from '../hooks/useCancelJob.ts'
import { useJobProgress } from '../hooks/useJobProgress.ts'
import { StatusBadge } from './StatusBadge.tsx'

export function JobDetail({
  job, onProgress, onFinished, onChanged, onClose,
}: {
  job: Job
  onProgress: (progress: JobProgress) => void
  onFinished: () => void
  onChanged: () => void
  onClose: () => void
}) {
  const t = useT()
  const text = useMessageText()
  const format = useFormat()
  const { progress, reconnecting } = useJobProgress(job.id, { onProgress, onTerminal: onFinished })
  const cancel = useCancelJob(job.id, onChanged)

  // The stream is the freshest source; the list row is what we know until its first event arrives.
  const current = progress ?? job
  const done = isTerminal(current.status)

  return (
    <section className="card" aria-live="polite">
      <div className="detail-head">
        <h3>{job.fileName}</h3>
        <StatusBadge status={current.status} />
        <button type="button" className="small" onClick={onClose}>{t('batch.detail.close')}</button>
      </div>

      <ProgressBar value={current.completedChunks} max={current.totalChunks} label={t('batch.col.progress')} />
      <p className="meta">
        {t('batch.chunks', { done: current.completedChunks, total: current.totalChunks })}
        {' · '}
        {t('batch.detail.rows', {
          processed: format.integer(current.processedRows), failed: format.integer(current.failedRows), total: format.integer(job.totalRows),
        })}
      </p>

      {reconnecting && !done && <Banner kind="warning">{t('batch.reconnecting')}</Banner>}
      {cancel.error && <Banner kind="error">{text(cancel.error)}</Banner>}

      <div className="actions">
        {!done && (
          <button type="button" className="danger" disabled={cancel.cancelling} onClick={cancel.ask}>{t('batch.cancel')}</button>
        )}
        {(current.status === 'Completed' || current.status === 'Cancelled') && (
          <>
            <a className="button" href={resultsCsvUrl(job.id)} download>{t('batch.download.results')}</a>
            <a className="button" href={errorsCsvUrl(job.id)} download>
              {t('batch.download.errors', { count: format.integer(current.failedRows) })}
            </a>
          </>
        )}
      </div>

      <ConfirmDialog
        open={cancel.confirming} danger message={t('batch.cancel.confirm')} confirmLabel={t('batch.cancel')}
        onConfirm={() => void cancel.confirm()} onCancel={cancel.dismiss}
      />
    </section>
  )
}
