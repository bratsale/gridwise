import { useCallback, useEffect, useState } from 'react'
import { failureMessage } from '../../../../shared/api/failure.ts'
import { useOptimisticList } from '../../../../shared/hooks/useOptimisticList.ts'
import { msg, type Message } from '../../../../shared/i18n/message.ts'
import { getTaxGroup, listTaxGroups, listVersions, type TaxGroup } from '../api/taxGroupsApi.ts'
import type { TaxGroupFormSession } from './useTaxGroupForm.ts'

export type TaxGroupNotice =
  | { kind: 'created' }
  | { kind: 'versionSaved'; version: string }
  | { kind: 'retired'; date: string }

const byCode = (a: TaxGroup, b: TaxGroup) => a.code.localeCompare(b.code)

export function useTaxGroupList() {
  const { items: taxGroups, setItems, pending, optimistic } = useOptimisticList<TaxGroup>((g) => g.code, byCode)
  const [loading, setLoading] = useState(true)
  const [listError, setListError] = useState<Message | null>(null)
  const [rowError, setRowError] = useState<Message | null>(null)
  const [notice, setNotice] = useState<TaxGroupNotice | null>(null)

  // `key` makes every opening of the form a new form.
  const [form, setForm] = useState<{ key: number; session: TaxGroupFormSession } | null>(null)
  const [retiring, setRetiring] = useState<TaxGroup | null>(null)
  const [history, setHistory] = useState<{ taxGroup: TaxGroup; versions: TaxGroup[] | null } | null>(null)

  // Shows the response of the list request. It is called when the response arrives, never synchronously from an effect.
  const apply = useCallback((response: Awaited<ReturnType<typeof listTaxGroups>>) => {
    if (response.ok) {
      setItems(response.body)
      setListError(null)
    } else {
      setListError(failureMessage(response, msg('taxGroups.loadError')))
    }
    setLoading(false)
  }, [setItems])

  // `silent` refreshes in the background: the list stays on screen instead of turning into a spinner.
  const load = useCallback(async (silent = false) => {
    if (!silent) setLoading(true)
    apply(await listTaxGroups())
  }, [apply])

  // `loading` starts as true, so the first fetch does not need to set it.
  useEffect(() => {
    void listTaxGroups().then(apply)
  }, [apply])

  function openForm(session: TaxGroupFormSession) {
    setRetiring(null)
    setForm((current) => ({ key: (current?.key ?? 0) + 1, session }))
  }

  async function edit(taxGroup: TaxGroup) {
    setRowError(null)
    const response = await getTaxGroup(taxGroup.code)
    const etag = response.headers.get('ETag')
    if (!response.ok) setRowError(failureMessage(response, msg('taxGroups.oneLoadError')))
    else if (!etag) setRowError(msg('taxGroups.oneLoadError'))
    else openForm({ mode: 'version', taxGroup: response.body, etag })
  }

  async function showHistory(taxGroup: TaxGroup) {
    setRowError(null)
    if (history?.taxGroup.code === taxGroup.code) {
      setHistory(null)
      return
    }
    setHistory({ taxGroup, versions: null })
    const response = await listVersions(taxGroup.code)
    if (response.ok) {
      setHistory({ taxGroup, versions: response.body })
    } else {
      setHistory(null)
      setRowError(failureMessage(response, msg('taxGroups.historyLoadError')))
    }
  }

  function changed(next: TaxGroupNotice) {
    setForm(null)
    setRetiring(null)
    setHistory(null)
    setNotice(next)
    void load(true)
  }

  return {
    taxGroups, pending, optimistic, loading, listError, rowError, notice, dismissNotice: () => setNotice(null),
    form, openCreate: () => openForm({ mode: 'create' }), closeForm: () => setForm(null),
    retiring, openRetire: (taxGroup: TaxGroup) => { setForm(null); setRetiring(taxGroup) }, closeRetire: () => setRetiring(null),
    history, load, edit, showHistory, changed,
  }
}
