import { useT } from '../../../../shared/i18n/LanguageProvider.tsx'
import type { JobStatus } from '../api/batchApi.ts'

const classes: Record<JobStatus, string> = {
  Pending: 'pending',
  Running: 'pending',
  Completed: 'active',
  Cancelled: 'retired',
  Failed: 'failed',
}

export function StatusBadge({ status }: { status: JobStatus }) {
  const t = useT()
  const label = {
    Pending: t('batch.status.pending'),
    Running: t('batch.status.running'),
    Completed: t('batch.status.completed'),
    Cancelled: t('batch.status.cancelled'),
    Failed: t('batch.status.failed'),
  }[status]
  return <span className={`badge ${classes[status]}`}>{label}</span>
}
