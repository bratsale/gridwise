import type { TranslationKey } from './en.ts'

export type Params = Record<string, string | number>

// Text that is translated when it is shown (so a language switch updates it) or text that came from the API as is.
// `detail` is the API's own text behind a translated message (English, as the server wrote it): shown smaller, under it.
export type Message = { key: TranslationKey; params?: Params; detail?: string } | { text: string }

export const msg = (key: TranslationKey, params?: Params): Message => ({ key, params })
export const raw = (text: string): Message => ({ text })
export const detailOf = (message: Message): string | undefined => ('detail' in message ? message.detail : undefined)
