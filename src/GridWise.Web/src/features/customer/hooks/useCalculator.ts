import { useEffect, useState, type FormEvent } from 'react'
import type { ProblemDetails } from '../../../shared/api/client.ts'
import { failureMessage } from '../../../shared/api/failure.ts'
import { errorsFor } from '../../../shared/api/problems.ts'
import { msg, raw, type Message } from '../../../shared/i18n/message.ts'
import { acceptRecommendation, calculate as requestRecommendation, type Quote, type Recommendation, type TaxGroup } from '../api/customerApi.ts'
import { validateCustomer, type CustomerField } from '../validation.ts'

export const quoteKey = (quote: Quote) => `${quote.planId}:${String(quote.planVersionNo)}`

export function useCalculator(taxGroups: TaxGroup[]) {
  const [consumption, setConsumption] = useState('')
  const [taxGroup, setTaxGroup] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [problem, setProblem] = useState<ProblemDetails | undefined>()
  const [message, setMessage] = useState<Message | null>(null)
  const [touched, setTouched] = useState<ReadonlySet<CustomerField>>(new Set())
  const [submitted, setSubmitted] = useState(false)

  const [result, setResult] = useState<Recommendation | null>(null)
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set())
  const [accepting, setAccepting] = useState<string | null>(null)
  const [accepted, setAccepted] = useState<{ plan: string; total: number | string } | null>(null)
  const [acceptError, setAcceptError] = useState<Message | null>(null)

  // The first tax group is chosen when they arrive.
  useEffect(() => {
    setTaxGroup((current) => current || taxGroups[0]?.code || '')
  }, [taxGroups])

  const clientErrors = validateCustomer({ consumption, taxGroup })

  // Client errors show after submit or once the field was touched; server errors sit at the same place.
  function fieldErrors(field: CustomerField): Message[] {
    const client = submitted || touched.has(field) ? clientErrors[field] : undefined
    return client ? [client] : errorsFor(problem, field)
  }

  function touch(field: CustomerField) {
    setTouched((current) => new Set(current).add(field))
  }

  async function calculate(event: FormEvent) {
    event.preventDefault()
    setSubmitted(true)
    if (Object.keys(clientErrors).length > 0) return

    setSubmitting(true)
    setProblem(undefined)
    setMessage(null)
    setResult(null)
    setExpanded(new Set())
    setAccepted(null)
    setAcceptError(null)

    const response = await requestRecommendation(Number(consumption), taxGroup)

    if (response.ok) {
      setResult(response.body)
    } else if (response.status === 400) {
      setProblem(response.body)
      // Errors for other keys have no field to sit next to: show them as a message.
      const others = Object.entries(response.body?.errors ?? {})
        .filter(([key]) => !['consumptionkwh', 'taxgroup'].includes(key.toLowerCase()))
        .flatMap(([, messages]) => messages)
      if (others.length > 0 || !response.body?.errors) setMessage(raw([...others, response.body?.title].filter(Boolean).join(' ')))
    } else if (response.status === 503) {
      setMessage(msg('customer.noPlansYet'))
    } else {
      setMessage(failureMessage(response))
    }
    setSubmitting(false)
  }

  async function accept(quote: Quote) {
    if (!result) return
    setAccepting(quoteKey(quote))
    setAcceptError(null)

    const response = await acceptRecommendation(result.id, quote.planId)

    if (response.ok) setAccepted({ plan: quote.name, total: response.body.total })
    else if (response.status === 409) setAcceptError(msg('customer.alreadyAccepted'))
    else setAcceptError(failureMessage(response))
    setAccepting(null)
  }

  function toggle(key: string) {
    setExpanded((current) => {
      const next = new Set(current)
      if (!next.delete(key)) next.add(key)
      return next
    })
  }

  return {
    consumption, setConsumption, taxGroup, setTaxGroup, submitting, message, fieldErrors, touch,
    calculate, result, expanded, toggle, accepting, accepted, acceptError, accept,
  }
}
