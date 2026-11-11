import { useCallback, useEffect, useRef, useState } from 'react'
import { failureMessage } from '../../../../shared/api/failure.ts'
import { useOptimisticList } from '../../../../shared/hooks/useOptimisticList.ts'
import { msg, type Message } from '../../../../shared/i18n/message.ts'
import { getPlan, listPlans, listVersions, type Plan } from '../api/plansApi.ts'
import type { FormSession } from './usePlanForm.ts'

export type Notice =
  | { kind: 'created'; id: string }
  | { kind: 'versionSaved'; version: string }
  | { kind: 'retired'; date: string }

// Long enough to still show after a smooth scroll over a long list (about 1.5 s from one end of 200 rows to the other).
const FLASH_MS = 3000

export function usePlans() {
  const { items: plans, setItems: setPlans, pending, optimistic } = useOptimisticList<Plan>((plan) => plan.id)
  const [loading, setLoading] = useState(true)
  const [listError, setListError] = useState<Message | null>(null)
  const [rowError, setRowError] = useState<Message | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)

  // `key` makes every opening of the form a new form with a new Idempotency-Key.
  const [form, setForm] = useState<{ key: number; session: FormSession } | null>(null)
  const [retiring, setRetiring] = useState<Plan | null>(null)
  const [history, setHistory] = useState<{ plan: Plan; versions: Plan[] | null } | null>(null)
  // The row to bring back into view and highlight briefly after a form or retire panel closes (saved or cancelled).
  const returnTo = useRef<string | null>(null)
  const [flash, setFlash] = useState<string | null>(null)
  function flashRow(key: string | null) {
    returnTo.current = null
    if (!key) return
    setFlash(key)
    setTimeout(() => setFlash((current) => (current === key ? null : current)), FLASH_MS)
  }

  // Shows the response of the list request. It is called when the response arrives, never synchronously from an effect.
  const apply = useCallback((response: Awaited<ReturnType<typeof listPlans>>) => {
    if (response.ok) {
      setPlans(response.body)
      setListError(null)
    } else {
      setListError(failureMessage(response, msg('plans.loadError')))
    }
    setLoading(false)
  }, [setPlans])

  // `silent` refreshes in the background: the list stays on screen instead of turning into a spinner.
  const load = useCallback(async (silent = false) => {
    if (!silent) setLoading(true)
    apply(await listPlans())
  }, [apply])

  // `loading` starts as true, so the first fetch does not need to set it.
  useEffect(() => {
    void listPlans().then(apply)
  }, [apply])

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
    else {
      returnTo.current = plan.id
      openForm({ mode: 'version', plan: response.body, etag })
    }
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
    flashRow(next.kind === 'created' ? next.id : returnTo.current)
    setForm(null)
    setRetiring(null)
    setHistory(null)
    setNotice(next)
    void load(true)
  }

  return {
    plans, pending, optimistic, loading, listError, rowError, notice, dismissNotice: () => setNotice(null),
    form, openCreate: () => { returnTo.current = null; openForm({ mode: 'create' }) },
    closeForm: () => { flashRow(returnTo.current); setForm(null) },
    retiring, openRetire: (plan: Plan) => { returnTo.current = plan.id; setForm(null); setRetiring(plan) },
    closeRetire: () => { flashRow(returnTo.current); setRetiring(null) },
    flash, history, load, edit, showHistory, changed,
  }
}
