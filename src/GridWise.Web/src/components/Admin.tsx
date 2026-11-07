import { useCallback, useEffect, useState } from 'react'
import { getPlan, listPlans, listVersions, type Plan } from '../api/catalogApi.ts'
import { PlanForm, type FormSession } from './admin/PlanForm.tsx'
import { PlanHistory } from './admin/PlanHistory.tsx'
import { PlanList } from './admin/PlanList.tsx'
import { RetirePanel } from './admin/RetirePanel.tsx'

export function Admin() {
  const [plans, setPlans] = useState<Plan[]>([])
  const [loading, setLoading] = useState(true)
  const [listError, setListError] = useState<string | null>(null)
  const [rowError, setRowError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

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
      setListError(response.body?.title ?? 'The plans could not be loaded.')
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
    if (!response.ok) setRowError(response.body?.title ?? 'The plan could not be loaded.')
    else if (!etag) setRowError('The plan could not be loaded.')
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
      setRowError(response.body?.title ?? 'The history could not be loaded.')
    }
  }

  function changed(message: string) {
    setForm(null)
    setRetiring(null)
    setHistory(null)
    setNotice(message)
    void load()
  }

  return (
    <section>
      <div className="toolbar">
        <button type="button" className="primary" onClick={() => openForm({ mode: 'create' })}>New plan</button>
        <button type="button" disabled={loading} onClick={() => void load()}>Refresh</button>
      </div>

      {notice && (
        <div className="notice" role="status">
          <div className="banner success">
            <span>{notice}.</span>
            <button type="button" className="icon" aria-label="Dismiss" onClick={() => setNotice(null)}>✕</button>
          </div>
          <div className="banner info">
            <span>Changes reach recommendations within a few seconds.</span>
          </div>
        </div>
      )}
      {listError && <p className="error">{listError}</p>}
      {rowError && <p className="error">{rowError}</p>}

      {form && <PlanForm key={form.key} session={form.session} onSaved={changed} onCancel={() => setForm(null)} />}
      {retiring && <RetirePanel key={retiring.id} plan={retiring} onDone={changed} onCancel={() => setRetiring(null)} />}

      <PlanList
        plans={plans}
        loading={loading}
        onHistory={(plan) => void showHistory(plan)}
        onEdit={(plan) => void edit(plan)}
        onRetire={(plan) => {
          setForm(null)
          setRetiring(plan)
        }}
      />

      {history && <PlanHistory plan={history.plan} versions={history.versions} />}
    </section>
  )
}
