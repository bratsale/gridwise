export function FieldErrors({ messages }: { messages: string[] }) {
  return messages.length > 0 ? <p className="field-error">{messages.join(' ')}</p> : null
}
