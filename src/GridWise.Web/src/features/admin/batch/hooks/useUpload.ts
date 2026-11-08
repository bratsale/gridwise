import { useState } from 'react'
import { failureMessage } from '../../../../shared/api/failure.ts'
import { msg, type Message } from '../../../../shared/i18n/message.ts'
import { uploadJob } from '../api/batchApi.ts'

export function useUpload(onAccepted: (jobId: string) => void) {
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<Message | null>(null)

  async function upload(file: File) {
    setUploading(true)
    setError(null)
    const response = await uploadJob(file)
    setUploading(false)

    if (response.ok) onAccepted(response.body.id)
    else if (response.status === 413) setError(msg('batch.upload.tooLarge'))
    else setError(failureMessage(response, msg('batch.upload.failed')))
  }

  return { uploading, error, upload, clearError: () => setError(null) }
}
