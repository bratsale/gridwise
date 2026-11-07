import { msg, raw, type Message } from '../i18n/message.ts'
import type { ApiResponse } from './client.ts'

// The message for a failed response: the API's own title as received, a translated text when there is none.
export function failureMessage(response: ApiResponse<unknown>, fallback?: Message): Message {
  if (response.status === 0) return msg('error.unreachable')
  const title = response.ok ? undefined : response.body?.title
  return title ? raw(title) : (fallback ?? msg('error.unexpected', { status: response.status }))
}
