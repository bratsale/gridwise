import { useFormat, useT } from '../../../../shared/i18n/useLanguage.ts'
import { Loading } from '../../../../shared/ui/Loading.tsx'
import type { TaxGroup } from '../api/taxGroupsApi.ts'

export function TaxGroupList({
  taxGroups, pending, loading, onHistory, onEdit, onRetire,
}: {
  taxGroups: TaxGroup[]
  pending: ReadonlySet<string>
  loading: boolean
  onHistory: (taxGroup: TaxGroup) => void
  onEdit: (taxGroup: TaxGroup) => void
  onRetire: (taxGroup: TaxGroup) => void
}) {
  const t = useT()
  const format = useFormat()

  if (loading) return <div className="card"><Loading /></div>
  if (taxGroups.length === 0) return <div className="card"><p className="state">{t('taxGroups.empty')}</p></div>

  return (
    <div className="card flush table-wrap">
      <table className="plans">
        <thead>
          <tr>
            <th>{t('taxGroups.code')}</th><th>{t('taxGroups.version')}</th><th>{t('taxGroups.status')}</th>
            <th>{t('taxGroups.effectiveFrom')}</th><th className="num">{t('taxGroups.vat')}</th>
            <th className="num">{t('taxGroups.ecoTax')}</th><th />
          </tr>
        </thead>
        <tbody>
          {taxGroups.map((group) => {
            const retired = group.status === 'Retired'
            const isPending = pending.has(group.code)
            return (
              <tr key={group.code} className={isPending ? 'pending' : retired ? 'retired' : undefined}>
                <td>
                  <button type="button" className="link" onClick={() => onHistory(group)}>{group.code}</button>
                </td>
                <td className="mono">{String(group.versionNo)}</td>
                <td>
                  {isPending
                    ? <span className="badge pending">{t('common.saving')}</span>
                    : <span className={`badge ${retired ? 'retired' : 'active'}`}>{retired ? t('plans.status.retired') : t('plans.status.active')}</span>}
                </td>
                <td className="mono">{format.date(group.effectiveFrom)}</td>
                <td className="num">{format.percent(group.vat)}</td>
                <td className="num">{format.price(group.ecoTax)}</td>
                <td>
                  {!retired && !isPending && (
                    <div className="actions">
                      <button type="button" className="small" onClick={() => onEdit(group)}>{t('taxGroups.edit')}</button>
                      <button type="button" className="small" onClick={() => onRetire(group)}>{t('taxGroups.retire')}</button>
                    </div>
                  )}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
