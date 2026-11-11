import { useEffect, useRef, useState } from 'react'
import { taxGroupLabel } from '../../../shared/i18n/taxGroupName.ts'
import { useFormat, useT } from '../../../shared/i18n/useLanguage.ts'
import type { Message } from '../../../shared/i18n/message.ts'
import { Banner } from '../../../shared/ui/Banner.tsx'
import { ConfirmDialog } from '../../../shared/ui/ConfirmDialog.tsx'
import type { Quote, Recommendation } from '../api/customerApi.ts'
import { TOP_PLANS, visiblePlans } from '../display.ts'
import { quoteKey } from '../hooks/useCalculator.ts'
import { BreakdownTable } from './BreakdownTable.tsx'

// Mounted once per result (the page gives it the result id as key), so "show all" starts closed for every calculation.
export function RecommendationResult({
  result, expanded, accepting, accepted, acceptError, onToggle, onAccept,
}: {
  result: Recommendation
  expanded: ReadonlySet<string>
  accepting: string | null
  accepted: { planId: string; plan: string; total: number | string } | null
  acceptError: Message | null
  onToggle: (key: string) => void
  onAccept: (quote: Quote) => void
}) {
  const t = useT()
  const format = useFormat()
  const ref = useRef<HTMLDivElement>(null)
  const [showAll, setShowAll] = useState(false)
  // Accepting is final (another plan is refused afterwards), so it is confirmed first.
  const [confirming, setConfirming] = useState<Quote | null>(null)

  // After calculating, focus moves to the result.
  useEffect(() => {
    ref.current?.focus()
  }, [result])

  return (
    <div className="result" ref={ref} tabIndex={-1}>
      <p className="meta">
        {t('customer.meta', {
          kwh: format.kwh(result.consumptionKwh),
          code: taxGroupLabel(t, result.taxGroup.code),
          version: String(result.taxGroup.versionNo),
          at: format.dateTime(result.calculatedAt),
        })}
      </p>

      {/* Above the table, so they are seen without scrolling. */}
      {accepted && (
        <Banner kind="success">{t('customer.accepted', { plan: accepted.plan, total: format.money(accepted.total) })}</Banner>
      )}
      {acceptError && <Banner kind="error" message={acceptError} />}

      <div className="card flush table-wrap">
        <table className="plans">
          <thead>
            <tr>
              <th>{t('customer.plan')}</th>
              <th>{t('customer.version')}</th>
              <th className="num">{t('customer.total')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {visiblePlans(result.plans, showAll).map((quote) => {
              const key = quoteKey(quote)
              const isBest = quote.planId === result.recommended?.planId
              const isAccepted = quote.planId === accepted?.planId
              const isOpen = expanded.has(key)
              return [
                <tr key={key} className={isBest ? 'best' : undefined}>
                  <td>
                    <button type="button" className="link" aria-expanded={isOpen} onClick={() => onToggle(key)}>
                      {isOpen ? '▾' : '▸'} {quote.name}
                    </button>
                    {isBest && <span className="badge best">{t('customer.best')}</span>}
                    {isAccepted && <span className="badge active accepted">{t('customer.acceptedLabel')}</span>}
                  </td>
                  <td className="mono">{String(quote.planVersionNo)}</td>
                  <td className="num">{format.money(quote.total)}</td>
                  <td>
                    <button
                      type="button" className="small primary"
                      disabled={accepting !== null || accepted !== null} onClick={() => setConfirming(quote)}
                    >
                      {accepting === key ? t('customer.accepting') : t('customer.accept')}
                    </button>
                  </td>
                </tr>,
                isOpen && (
                  <tr key={`${key}:details`} className="details">
                    <td colSpan={4}>
                      <BreakdownTable breakdown={quote.breakdown} />
                    </td>
                  </tr>
                ),
              ]
            })}
          </tbody>
        </table>
      </div>

      {result.plans.length > TOP_PLANS && (
        <div className="actions list-more">
          <button type="button" className="small" aria-expanded={showAll} onClick={() => setShowAll(!showAll)}>
            {showAll ? t('customer.showTop', { count: TOP_PLANS }) : t('customer.showAll', { count: result.plans.length })}
          </button>
        </div>
      )}

      <ConfirmDialog
        open={confirming !== null}
        message={confirming ? t('customer.acceptConfirm', { plan: confirming.name, total: format.money(confirming.total) }) : ''}
        confirmLabel={t('customer.accept')}
        onConfirm={() => {
          if (confirming) onAccept(confirming)
          setConfirming(null)
        }}
        onCancel={() => setConfirming(null)}
      />
    </div>
  )
}
