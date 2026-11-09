import { useFormat, useT } from '../../../shared/i18n/useLanguage.ts'
import type { Breakdown } from '../api/customerApi.ts'

export function BreakdownTable({ breakdown }: { breakdown: Breakdown }) {
  const t = useT()
  const format = useFormat()
  return (
    <table className="breakdown">
      <tbody>
        {breakdown.tiers.map((tier, i) => (
          <tr key={i}>
            <td>
              {format.kwh(tier.kwh)} × {format.price(tier.pricePerKwh)}
            </td>
            <td className="num">{format.money(tier.amount)}</td>
          </tr>
        ))}
        <tr><td>{t('breakdown.energySubtotal')}</td><td className="num">{format.money(breakdown.energySubtotal)}</td></tr>
        <tr><td>{t('breakdown.discount')}</td><td className="num">{format.money(breakdown.discountAmount)}</td></tr>
        <tr><td>{t('breakdown.netEnergy')}</td><td className="num">{format.money(breakdown.netEnergy)}</td></tr>
        <tr><td>{t('breakdown.ecoTax')}</td><td className="num">{format.money(breakdown.ecoTax)}</td></tr>
        <tr><td>{t('breakdown.vatBase')}</td><td className="num">{format.money(breakdown.vatBase)}</td></tr>
        <tr><td>{t('breakdown.vat')}</td><td className="num">{format.money(breakdown.vatAmount)}</td></tr>
        <tr className="total"><td>{t('breakdown.total')}</td><td className="num">{format.money(breakdown.total)}</td></tr>
      </tbody>
    </table>
  )
}
