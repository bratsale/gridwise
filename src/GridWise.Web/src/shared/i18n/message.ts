import type { TranslationKey } from './en.ts'

export type Params = Record<string, string | number>

// Text that is translated when it is shown (so a language switch updates it) or text that came from the API as is.
export type Message = { key: TranslationKey; params?: Params } | { text: string }

export const msg = (key: TranslationKey, params?: Params): Message => ({ key, params })
export const raw = (text: string): Message => ({ text })
