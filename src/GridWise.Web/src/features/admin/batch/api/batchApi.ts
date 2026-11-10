import { currentAccessToken, reportUnauthorized } from '../../../../shared/auth/session.ts'
import type { components } from '../../../../shared/api/batch.ts'
import { safeRequest, type ApiResponse } from '../../../../shared/api/client.ts'

export type JobStatus = 'Pending' | 'Running' | 'Completed' | 'Cancelled' | 'Failed'

// What the live stream sends (GET /jobs/{id}/events): the current progress of one job.
export interface JobProgress {
  status: JobStatus
  totalChunks: number
  completedChunks: number
  processedRows: number
  failedRows: number
}

export interface Job extends JobProgress {
  id: string
  fileName: string
  totalRows: number
  createdAt: string
  startedAt: string | null
  finishedAt: string | null
}

const isTerminal = (status: JobStatus) => status === 'Completed' || status === 'Cancelled' || status === 'Failed'
export { isTerminal }

// The generated types allow numbers as strings; the screens work with numbers.
function toJob(dto: components['schemas']['JobResponse']): Job {
  return {
    id: dto.id,
    status: dto.status as JobStatus,
    fileName: dto.fileName,
    totalRows: Number(dto.totalRows),
    totalChunks: Number(dto.totalChunks),
    completedChunks: Number(dto.completedChunks),
    processedRows: Number(dto.processedRows),
    failedRows: Number(dto.failedRows),
    createdAt: dto.createdAt,
    startedAt: dto.startedAt,
    finishedAt: dto.finishedAt,
  }
}

async function mapBody<D, T>(response: Promise<ApiResponse<D>>, map: (body: D) => T): Promise<ApiResponse<T>> {
  const r = await response
  return r.ok ? { ...r, body: map(r.body) } : r
}

const call = <T>(path: string, options?: Parameters<typeof safeRequest>[2]) => safeRequest<T>('batch', path, options)

export const listJobs = () => mapBody(call<components['schemas']['JobResponse'][]>('/jobs'), (jobs) => jobs.map(toJob))

export const cancelJob = (id: string) => mapBody(call<components['schemas']['JobResponse']>(`/jobs/${id}/cancel`, { method: 'POST' }), toJob)

// 202 Accepted with the new job's id; the job itself is read from the list.
export function uploadJob(file: File) {
  const body = new FormData()
  body.append('file', file)
  return call<components['schemas']['JobCreatedResponse']>('/jobs', { method: 'POST', body })
}

// Plain links: the browser downloads the files itself.
// EventSource cannot send headers, so the token goes in the URL (the gateway accepts it there for this route only, and keeps
// it out of logs and traces).
export const eventsUrl = (id: string, accessToken?: string | null) =>
  `/api/batch/jobs/${id}/events${accessToken ? `?access_token=${encodeURIComponent(accessToken)}` : ''}`
export const resultsCsvUrl = (id: string) => `/api/batch/jobs/${id}/results.csv`
export const errorsCsvUrl = (id: string) => `/api/batch/jobs/${id}/errors.csv`

/**
 * Downloads a CSV with the user's token (a plain link cannot send it) and saves it under the given name.
 * Returns false when the download failed; a 401 starts a login like any other request.
 */
export async function downloadCsv(url: string, fileName: string): Promise<boolean> {
  const token = currentAccessToken()
  let response: Response
  try {
    response = await fetch(url, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
  } catch {
    return false
  }
  if (response.status === 401) reportUnauthorized()
  if (!response.ok) return false

  const href = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = href
  link.download = fileName
  link.click()
  URL.revokeObjectURL(href)
  return true
}
