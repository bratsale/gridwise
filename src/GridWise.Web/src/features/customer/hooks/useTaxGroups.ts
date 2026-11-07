import { useEffect, useState } from 'react'
import { failureMessage } from '../../../shared/api/failure.ts'
import { msg, type Message } from '../../../shared/i18n/message.ts'
import { listTaxGroups, type TaxGroup } from '../api/customerApi.ts'

export function useTaxGroups() {
  const [taxGroups, setTaxGroups] = useState<TaxGroup[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<Message | null>(null)

  useEffect(() => {
    void (async () => {
      const response = await listTaxGroups()
      if (response.ok) setTaxGroups(response.body.filter((g) => g.status !== 'Retired'))
      else setError(failureMessage(response, msg('customer.taxGroupsError')))
      setLoading(false)
    })()
  }, [])

  return { taxGroups, loading, error }
}
