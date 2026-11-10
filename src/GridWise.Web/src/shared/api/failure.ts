import { msg, raw, type Message } from '../i18n/message.ts'
import type { ApiResponse } from './client.ts'

// The message for a failed response: the API's own title as received, a translated text when there is none.
// 401 and 403 have their own texts: a login is already on its way, or the user's role does not allow the action.
export function failureMessage(response: ApiResponse<unknown>, fallback?: Message): Message {
  if (response.status === 0) return msg('error.unreachable')
  if (response.status === 401) return msg('auth.required')
  if (response.status === 403) return msg('auth.forbidden')
  const title = response.ok ? undefined : response.body?.title
  return title ? raw(title) : (fallback ?? msg('error.unexpected', { status: response.status }))
}
