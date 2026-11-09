import { useFormat, useT } from '../../../../shared/i18n/useLanguage.ts'
import { Banner } from '../../../../shared/ui/Banner.tsx'
import { Loading } from '../../../../shared/ui/Loading.tsx'
import type { TaxGroup } from '../api/taxGroupsApi.ts'

// The latest version (highest number) tells whether the tax group is retired, and from when.
function retiredFrom(versions: TaxGroup[]) {
  const latest = versions.reduce((a, b) => (Number(b.versionNo) > Number(a.versionNo) ? b : a))
  return latest.status === 'Retired' ? latest.effectiveFrom : null
}

// versions is null while they are loading.
export function TaxGroupHistory({ taxGroup, versions }: { taxGroup: TaxGroup; versions: TaxGroup[] | null }) {
  const t = useT()
  const format = useFormat()
  const retiredDate = versions && versions.length > 0 ? retiredFrom(versions) : null

  return (
    <div className="panel">
      <h3>{t('taxGroups.history.title', { code: taxGroup.code })}</h3>
      {versions === null ? (
        <Loading />
      ) : versions.length === 0 ? (
        <p className="state">{t('taxGroups.history.empty')}</p>
      ) : (
        <>
          {retiredDate && <Banner kind="info">{t('taxGroups.history.retiredFrom', { date: format.date(retiredDate) })}</Banner>}
          <div className="table-wrap">
            <table className="plans">
              <thead>
                <tr>
                  <th>{t('taxGroups.version')}</th><th>{t('taxGroups.status')}</th><th>{t('taxGroups.effectiveFrom')}</th>
                  <th className="num">{t('taxGroups.vat')}</th><th className="num">{t('taxGroups.ecoTax')}</th>
                </tr>
              </thead>
              <tbody>
                {versions.map((v) => (
                  <tr key={String(v.versionNo)}>
                    <td>{String(v.versionNo)}</td>
                    <td>
                      <span className={`badge ${v.status === 'Retired' ? 'retired' : 'active'}`}>
                        {v.status === 'Retired' ? t('plans.status.retired') : t('plans.status.active')}
                      </span>
                    </td>
                    <td>{format.date(v.effectiveFrom)}</td>
                    <td className="num">{format.percent(v.vat)}</td>
                    <td className="num">{format.price(v.ecoTax)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  )
}
