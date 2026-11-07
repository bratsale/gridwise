export type Service = 'catalog' | 'recommendation'

const basePaths: Record<Service, string> = {
  catalog: '/api/catalog',
  recommendation: '/api/recommendation',
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
export async function request<T>(
  service: Service,
  path: string,
  { method = 'GET', body, headers = {} }: RequestOptions = {},
): Promise<ApiResponse<T>> {
  const response = await fetch(`${basePaths[service]}${path}`, {
    method,
    headers: {
      'X-Correlation-Id': crypto.randomUUID(),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...headers,
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  const text = await response.text()
  const parsed: unknown = text ? JSON.parse(text) : undefined

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
