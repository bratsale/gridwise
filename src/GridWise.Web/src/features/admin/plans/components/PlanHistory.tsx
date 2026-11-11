import { useEffect, useRef } from 'react'
import { useFormat, useT } from '../../../../shared/i18n/useLanguage.ts'
import { Banner } from '../../../../shared/ui/Banner.tsx'
import { Loading } from '../../../../shared/ui/Loading.tsx'
import { reveal } from '../../../../shared/ui/reveal.ts'
import type { Plan } from '../api/plansApi.ts'

// The latest version (highest number) tells whether the plan is retired, and from when.
function retiredFrom(versions: Plan[]) {
  const latest = versions.reduce((a, b) => (Number(b.versionNo) > Number(a.versionNo) ? b : a))
  return latest.status === 'Retired' ? latest.effectiveFrom : null
}

// versions is null while they are loading.
export function PlanHistory({ plan, versions }: { plan: Plan; versions: Plan[] | null }) {
  const t = useT()
  const format = useFormat()
  const ref = useRef<HTMLDivElement>(null)

  // The row that was clicked can be far down a long list: bring the panel into view when it opens for a new item.
  useEffect(() => {
    reveal(ref.current, { block: 'nearest' })
  }, [plan.id])

  const retiredDate = versions && versions.length > 0 ? retiredFrom(versions) : null

  return (
    <div className="panel" ref={ref}>
      <h3>{t('plans.history.title', { name: plan.name })}</h3>
      {versions === null ? (
        <Loading />
      ) : versions.length === 0 ? (
        <p className="state">{t('plans.history.empty')}</p>
      ) : (
        <>
          {retiredDate && <Banner kind="info">{t('plans.history.retiredFrom', { date: format.date(retiredDate) })}</Banner>}
          <div className="table-wrap">
            <table className="plans">
              <thead>
                <tr>
                  <th>{t('plans.version')}</th><th>{t('plans.status')}</th><th>{t('plans.effectiveFrom')}</th>
                  <th className="num">{t('plans.discount')}</th><th>{t('plans.tiers')}</th>
                </tr>
              </thead>
              <tbody>
                {versions.map((v) => (
                  <tr key={String(v.versionNo)}>
                    <td className="mono">{String(v.versionNo)}</td>
                    <td>
                      <span className={`badge ${v.status === 'Retired' ? 'retired' : 'active'}`}>
                        {v.status === 'Retired' ? t('plans.status.retired') : t('plans.status.active')}
                      </span>
                    </td>
                    <td className="mono">{format.date(v.effectiveFrom)}</td>
                    <td className="num">{format.percent(v.discount)}</td>
                    <td>
                      {v.tiers.length === 0
                        ? '-'
                        : v.tiers
                            .map((tier) => {
                              const limit = tier.threshold == null ? t('common.noLimit') : t('common.upTo', { kwh: format.kwh(tier.threshold) })
                              return `${limit}: ${format.price(tier.pricePerKwh)} ${t('common.perKwh')}`
                            })
                            .join('; ')}
                    </td>
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
