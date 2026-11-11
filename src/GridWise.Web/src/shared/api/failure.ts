import type { TranslationKey } from '../i18n/en.ts'
import { msg, type Message } from '../i18n/message.ts'
import type { ApiResponse } from './client.ts'

// Statuses whose meaning does not depend on where they happened.
const byStatus: Partial<Record<number, TranslationKey>> = {
  409: 'error.conflict',
  413: 'error.tooLarge',
  415: 'error.unsupportedType',
  429: 'error.tooManyRequests',
  502: 'error.unavailable',
  503: 'error.unavailable',
  504: 'error.unavailable',
}

// The message for a failed response, always translated. The API's own title (English) is kept as the detail line, never as the
// main message. A 400 or 500 says what failed where it happened (the fallback), else a general text.
// No connection, 401 and 403 have their own texts: a login is already on its way, or the user's role does not allow the action.
export function failureMessage(response: ApiResponse<unknown>, fallback?: Message): Message {
  if (response.status === 0) return msg('error.unreachable')
  if (response.status === 401) return msg('auth.required')
  if (response.status === 403) return msg('auth.forbidden')

  const detail = response.ok ? undefined : response.body?.title
  const own = byStatus[response.status]
  if (own) return { key: own, params: undefined, detail }
  if (fallback && 'key' in fallback && (response.status === 400 || response.status === 500)) return { ...fallback, detail }
  if (response.status === 400) return { key: 'error.badRequest', params: undefined, detail }
  if (response.status === 500) return { key: 'error.server', params: undefined, detail }
  if (fallback && 'key' in fallback) return { ...fallback, detail }
  return { key: 'error.unexpected', params: { status: response.status }, detail }
}
