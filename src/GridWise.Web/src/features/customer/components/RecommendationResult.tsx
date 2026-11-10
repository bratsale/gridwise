import { useEffect, useRef } from 'react'
import { useFormat, useMessageText, useT } from '../../../shared/i18n/useLanguage.ts'
import type { Message } from '../../../shared/i18n/message.ts'
import { Banner } from '../../../shared/ui/Banner.tsx'
import type { Quote, Recommendation } from '../api/customerApi.ts'
import { quoteKey } from '../hooks/useCalculator.ts'
import { BreakdownTable } from './BreakdownTable.tsx'

export function RecommendationResult({
  result, expanded, accepting, accepted, acceptError, onToggle, onAccept,
}: {
  result: Recommendation
  expanded: ReadonlySet<string>
  accepting: string | null
  accepted: { plan: string; total: number | string } | null
  acceptError: Message | null
  onToggle: (key: string) => void
  onAccept: (quote: Quote) => void
}) {
  const t = useT()
  const text = useMessageText()
  const format = useFormat()
  const ref = useRef<HTMLDivElement>(null)

  // After calculating, focus moves to the result.
  useEffect(() => {
    ref.current?.focus()
  }, [result])

  return (
    <div className="result" ref={ref} tabIndex={-1}>
      <p className="meta">
        {t('customer.meta', {
          kwh: format.kwh(result.consumptionKwh),
          code: result.taxGroup.code,
          version: String(result.taxGroup.versionNo),
          at: format.dateTime(result.calculatedAt),
        })}
      </p>

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
            {result.plans.map((quote) => {
              const key = quoteKey(quote)
              const isBest = quote.planId === result.recommended?.planId
              const isOpen = expanded.has(key)
              return [
                <tr key={key} className={isBest ? 'best' : undefined}>
                  <td>
                    <button type="button" className="link" aria-expanded={isOpen} onClick={() => onToggle(key)}>
                      {isOpen ? '▾' : '▸'} {quote.name}
                    </button>
                    {isBest && <span className="badge best">{t('customer.best')}</span>}
                  </td>
                  <td className="mono">{String(quote.planVersionNo)}</td>
                  <td className="num">{format.money(quote.total)}</td>
                  <td>
                    <button
                      type="button" className="small primary"
                      disabled={accepting !== null || accepted !== null} onClick={() => onAccept(quote)}
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

      {accepted && (
        <Banner kind="success">{t('customer.accepted', { plan: accepted.plan, total: format.money(accepted.total) })}</Banner>
      )}
      {acceptError && <Banner kind="error">{text(acceptError)}</Banner>}
    </div>
  )
}
