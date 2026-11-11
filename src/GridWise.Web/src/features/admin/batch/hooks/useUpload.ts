import { useState } from 'react'
import type { Message } from '../../../../shared/i18n/message.ts'
import { uploadJob } from '../api/batchApi.ts'
import { uploadFailure } from '../uploadErrors.ts'

export function useUpload(onAccepted: (jobId: string) => void) {
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<Message | null>(null)

  async function upload(file: File) {
    setUploading(true)
    setError(null)
    const response = await uploadJob(file)
    setUploading(false)

    if (response.ok) onAccepted(response.body.id)
    else setError(uploadFailure(response))
  }

  return { uploading, error, upload, clearError: () => setError(null) }
}
