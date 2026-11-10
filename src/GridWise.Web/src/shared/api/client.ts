import { currentAccessToken, reportUnauthorized } from '../auth/session.ts'

export type Service = 'catalog' | 'recommendation' | 'batch'

const basePaths: Record<Service, string> = {
  catalog: '/api/catalog',
  recommendation: '/api/recommendation',
  batch: '/api/batch',
}

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
  [key: string]: unknown
}

interface ResponseBase {
  status: number
  headers: Headers
}

export type ApiResponse<T> =
  | (ResponseBase & { ok: true; body: T })
  | (ResponseBase & { ok: false; body: ProblemDetails | undefined })

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  body?: unknown
  headers?: Record<string, string>
}

// Thin fetch wrapper: no throwing on HTTP errors, the caller looks at status, body and headers (e.g. ETag).
// Every request carries the user's access token. A 401 (no token, or one the API no longer accepts) starts a login.
export async function request<T>(
  service: Service,
  path: string,
  { method = 'GET', body, headers = {} }: RequestOptions = {},
): Promise<ApiResponse<T>> {
  const token = currentAccessToken()
  const response = await fetch(`${basePaths[service]}${path}`, {
    method,
    headers: {
      'X-Correlation-Id': crypto.randomUUID(),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      // A FormData body (a file upload) sets its own Content-Type, with the multipart boundary.
      ...(body !== undefined && !(body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}),
      ...headers,
    },
    body: body === undefined ? undefined : body instanceof FormData ? body : JSON.stringify(body),
  })

  if (response.status === 401) reportUnauthorized()

  const text = await response.text()
  let parsed: unknown
  try {
    parsed = text ? JSON.parse(text) : undefined
  } catch (error) {
    // An error page that is not JSON (for example nginx answering 413) still has a status the caller can use.
    if (response.ok) throw error
  }

  return response.ok
    ? { ok: true, status: response.status, headers: response.headers, body: parsed as T }
    : { ok: false, status: response.status, headers: response.headers, body: parsed as ProblemDetails | undefined }
}

// Like request(), but a network failure or a non-JSON answer becomes a response with status 0 instead of an exception.
export async function safeRequest<T>(service: Service, path: string, options?: RequestOptions): Promise<ApiResponse<T>> {
  try {
    return await request<T>(service, path, options)
  } catch {
    return { ok: false, status: 0, headers: new Headers(), body: undefined }
  }
}
