import type { Message } from '../i18n/message.ts'
import { raw } from '../i18n/message.ts'
import type { ProblemDetails } from './client.ts'

// ProblemDetails error keys look like "name" or "tiers[2].threshold"; compare ignoring case.
export function errorsFor(problem: ProblemDetails | undefined, field: string): Message[] {
  const errors = problem?.errors ?? {}
  const key = Object.keys(errors).find((k) => k.toLowerCase() === field.toLowerCase())
  return key ? errors[key].map(raw) : []
}

// Messages that have no field next to them.
export function generalMessages(problem: ProblemDetails | undefined, knownFields: string[]): Message[] {
  if (!problem) return []
  const known = new Set(knownFields.map((f) => f.toLowerCase()))
  const errors = Object.entries(problem.errors ?? {})
  if (errors.length === 0) return problem.title ? [raw(problem.title)] : []
  return errors.filter(([key]) => !known.has(key.toLowerCase())).flatMap(([, messages]) => messages.map(raw))
}
