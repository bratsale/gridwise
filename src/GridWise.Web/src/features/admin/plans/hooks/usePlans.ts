import { useCallback, useEffect, useState } from 'react'
import { failureMessage } from '../../../../shared/api/failure.ts'
import { msg, type Message } from '../../../../shared/i18n/message.ts'
import { getPlan, listPlans, listVersions, type Plan } from '../api/plansApi.ts'
import type { FormSession } from './usePlanForm.ts'

export type Notice =
  | { kind: 'created' }
  | { kind: 'versionSaved'; version: string }
  | { kind: 'retired'; date: string }

export function usePlans() {
  const [plans, setPlans] = useState<Plan[]>([])
  const [loading, setLoading] = useState(true)
  const [listError, setListError] = useState<Message | null>(null)
  const [rowError, setRowError] = useState<Message | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)

  // `key` makes every opening of the form a new form with a new Idempotency-Key.
  const [form, setForm] = useState<{ key: number; session: FormSession } | null>(null)
  const [retiring, setRetiring] = useState<Plan | null>(null)
  const [history, setHistory] = useState<{ plan: Plan; versions: Plan[] | null } | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    const response = await listPlans()
    if (response.ok) {
      setPlans(response.body)
      setListError(null)
    } else {
      setListError(failureMessage(response, msg('plans.loadError')))
    }
    setLoading(false)
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  function openForm(session: FormSession) {
    setRetiring(null)
    setForm((current) => ({ key: (current?.key ?? 0) + 1, session }))
  }

  async function edit(plan: Plan) {
    setRowError(null)
    const response = await getPlan(plan.id)
    const etag = response.headers.get('ETag')
    if (!response.ok) setRowError(failureMessage(response, msg('plans.planLoadError')))
    else if (!etag) setRowError(msg('plans.planLoadError'))
    else openForm({ mode: 'version', plan: response.body, etag })
  }

  async function showHistory(plan: Plan) {
    setRowError(null)
    if (history?.plan.id === plan.id) {
      setHistory(null)
      return
    }
    setHistory({ plan, versions: null })
    const response = await listVersions(plan.id)
    if (response.ok) {
      setHistory({ plan, versions: response.body })
    } else {
      setHistory(null)
      setRowError(failureMessage(response, msg('plans.historyLoadError')))
    }
  }

  function changed(next: Notice) {
    setForm(null)
    setRetiring(null)
    setHistory(null)
    setNotice(next)
    void load()
  }

  return {
    plans, loading, listError, rowError, notice, dismissNotice: () => setNotice(null),
    form, openCreate: () => openForm({ mode: 'create' }), closeForm: () => setForm(null),
    retiring, openRetire: (plan: Plan) => { setForm(null); setRetiring(plan) }, closeRetire: () => setRetiring(null),
    history, load, edit, showHistory, changed,
  }
}
