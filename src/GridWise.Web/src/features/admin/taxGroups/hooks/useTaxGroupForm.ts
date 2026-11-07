import { useRef, useState, type FormEvent } from 'react'
import type { ProblemDetails } from '../../../../shared/api/client.ts'
import { failureMessage } from '../../../../shared/api/failure.ts'
import { errorsFor, generalMessages } from '../../../../shared/api/problems.ts'
import { fractionToPercent, percentToFraction, todayUtc } from '../../../../shared/format.ts'
import type { Optimistic } from '../../../../shared/hooks/useOptimisticList.ts'
import { msg, type Message } from '../../../../shared/i18n/message.ts'
import { addVersion, createTaxGroup, getTaxGroup, type TaxGroup } from '../api/taxGroupsApi.ts'
import { validateTaxGroup } from '../validation.ts'
import type { TaxGroupNotice } from './useTaxGroupList.ts'

export type TaxGroupFormSession = { mode: 'create' } | { mode: 'version'; taxGroup: TaxGroup; etag: string }

export function useTaxGroupForm(
  session: TaxGroupFormSession, optimistic: Optimistic<TaxGroup>, onSaved: (notice: TaxGroupNotice) => void,
) {
  const editing = session.mode === 'version' ? session.taxGroup : null
  const [etag, setEtag] = useState(session.mode === 'version' ? session.etag : '')

  const [code, setCodeValue] = useState(editing?.code ?? '')
  const [vat, setVat] = useState(editing ? String(fractionToPercent(editing.vat)) : '0')
  const [ecoTax, setEcoTax] = useState(editing ? String(editing.ecoTax) : '0')
  const [effectiveFrom, setEffectiveFrom] = useState(todayUtc())

  const [problem, setProblem] = useState<ProblemDetails | undefined>()
  const [message, setMessage] = useState<Message | null>(null)
  const [codeExists, setCodeExists] = useState(false)
  const [conflict, setConflict] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const submittingRef = useRef(false)
  const [touched, setTouched] = useState<ReadonlySet<string>>(new Set())
  const [submitted, setSubmitted] = useState(false)

  const clientErrors = validateTaxGroup({ code, vat, ecoTax, effectiveFrom }, todayUtc(), !editing)

  // Client errors show after submit or once the field was touched; server errors sit at the same place.
  // A 409 on create means the code is taken: it is shown next to the code field.
  function fieldErrors(field: string): Message[] {
    const client = submitted || touched.has(field) ? clientErrors[field] : undefined
    if (client) return [client]
    if (field === 'code' && codeExists) return [msg('taxGroups.codeExists')]
    return errorsFor(problem, field)
  }

  function touch(field: string) {
    setTouched((current) => new Set(current).add(field))
  }

  function setCode(value: string) {
    setCodeValue(value)
    setCodeExists(false)
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (submittingRef.current) return
    setSubmitted(true)
    if (Object.keys(clientErrors).length > 0) return

    submittingRef.current = true
    setSubmitting(true)
    setProblem(undefined)
    setMessage(null)
    setConflict(false)
    setCodeExists(false)

    // The VAT is typed as a percentage and sent as a fraction.
    const values = { vat: percentToFraction(Number(vat)), ecoTax: Number(ecoTax), effectiveFrom }

    // The list shows the expected result at once; a failed request puts the list back as it was.
    const change = optimistic.begin({
      code: editing?.code ?? code,
      versionNo: editing ? Number(editing.versionNo) + 1 : 1,
      status: 'Active',
      ...values,
    })

    const response = editing ? await addVersion(editing.code, values, etag) : await createTaxGroup({ code, ...values })

    submittingRef.current = false
    setSubmitting(false)

    if (response.ok) change.commit(response.body)
    else change.rollback()

    if (response.ok) {
      onSaved(editing ? { kind: 'versionSaved', version: String(response.body.versionNo) } : { kind: 'created' })
    } else if (response.status === 409) {
      if (editing) setConflict(true)
      else setCodeExists(true)
    } else if (response.status === 400) {
      setProblem(response.body)
    } else {
      setMessage(failureMessage(response))
    }
  }

  // Fetches the tax group again for its new ETag; the values in the form stay.
  async function reload() {
    if (!editing) return
    const response = await getTaxGroup(editing.code)
    const fresh = response.headers.get('ETag')
    if (!response.ok) {
      setMessage(failureMessage(response, msg('taxGroups.reloadError')))
    } else if (!fresh) {
      setMessage(msg('taxGroups.reloadError'))
    } else {
      setEtag(fresh)
      setConflict(false)
      setMessage(null)
    }
  }

  return {
    editing, code, setCode, vat, setVat, ecoTax, setEcoTax, effectiveFrom, setEffectiveFrom,
    submitting, conflict, message, general: generalMessages(problem, ['code', 'vat', 'ecoTax', 'effectiveFrom']),
    fieldErrors, touch, submit, reload,
  }
}
