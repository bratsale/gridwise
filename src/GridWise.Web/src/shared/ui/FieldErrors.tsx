import { useMessageText } from '../i18n/LanguageProvider.tsx'
import type { Message } from '../i18n/message.ts'

export function FieldErrors({ messages }: { messages: Message[] }) {
  const text = useMessageText()
  return messages.length > 0 ? <p className="field-error">{messages.map(text).join(' ')}</p> : null
}
