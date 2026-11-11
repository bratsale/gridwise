import { useState } from 'react'
import { useMessageText, useT } from '../../../shared/i18n/useLanguage.ts'
import { Banner } from '../../../shared/ui/Banner.tsx'
import { JobDetail } from './components/JobDetail.tsx'
import { JobList } from './components/JobList.tsx'
import { UploadSection } from './components/UploadSection.tsx'
import { useJobs } from './hooks/useJobs.ts'

export function BatchPage() {
  const t = useT()
  const text = useMessageText()
  const jobs = useJobs()
  const [openId, setOpenId] = useState<string | null>(null)
  const opened = jobs.jobs.find((job) => job.id === openId)

  async function accepted(jobId: string) {
    await jobs.load(true) // the new job is in the list before its detail opens
    setOpenId(jobId)
  }

  return (
    <section>
      <div className="toolbar">
        <button type="button" disabled={jobs.loading} onClick={() => void jobs.load()}>{t('common.refresh')}</button>
      </div>

      <UploadSection onAccepted={(id) => void accepted(id)} />

      {jobs.error && <Banner kind="error">{text(jobs.error)}</Banner>}

      {opened && (
        <JobDetail
          key={opened.id} job={opened}
          onProgress={(progress) => jobs.applyProgress(opened.id, progress)}
          onFinished={() => void jobs.load(true)}
          onChanged={() => void jobs.load(true)}
          onClose={() => setOpenId(null)}
        />
      )}

      {/* When loading failed and nothing is known, the error above is the whole message: no "no jobs yet". */}
      {!(jobs.error && jobs.jobs.length === 0) && (
        <JobList jobs={jobs.jobs} loading={jobs.loading} selectedId={openId} onOpen={setOpenId} />
      )}
    </section>
  )
}
