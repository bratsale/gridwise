import { useFormat, useT } from '../../../../shared/i18n/useLanguage.ts'
import { Loading } from '../../../../shared/ui/Loading.tsx'
import { ProgressBar } from '../../../../shared/ui/ProgressBar.tsx'
import type { Job } from '../api/batchApi.ts'
import { StatusBadge } from './StatusBadge.tsx'

export function JobList({
  jobs, loading, selectedId, onOpen,
}: { jobs: Job[]; loading: boolean; selectedId: string | null; onOpen: (jobId: string) => void }) {
  const t = useT()
  const format = useFormat()

  if (loading) return <div className="card"><Loading /></div>
  if (jobs.length === 0) return <div className="card"><p className="state">{t('batch.jobs.empty')}</p></div>

  return (
    <div className="card flush table-wrap">
      <table className="plans">
        <thead>
          <tr>
            <th>{t('batch.col.file')}</th><th>{t('batch.col.status')}</th><th>{t('batch.col.progress')}</th>
            <th>{t('batch.col.rows')}</th><th className="wide-only">{t('batch.col.created')}</th>
          </tr>
        </thead>
        <tbody>
          {jobs.map((job) => (
            <tr key={job.id} className={job.id === selectedId ? 'selected' : undefined}>
              <td><button type="button" className="link" onClick={() => onOpen(job.id)}>{job.fileName}</button></td>
              <td><StatusBadge status={job.status} /></td>
              <td className="progress-cell">
                <ProgressBar value={job.completedChunks} max={job.totalChunks} label={t('batch.col.progress')} />
                <span className="hint">{t('batch.chunks', { done: job.completedChunks, total: job.totalChunks })}</span>
              </td>
              <td className="mono">{t('batch.rows', { processed: format.integer(job.processedRows), failed: format.integer(job.failedRows) })}</td>
              <td className="mono wide-only">{format.dateTime(job.createdAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
