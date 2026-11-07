import { useT } from '../../../../shared/i18n/LanguageProvider.tsx'
import type { Message } from '../../../../shared/i18n/message.ts'
import { FieldErrors } from '../../../../shared/ui/FieldErrors.tsx'
import type { TierRow } from '../validation.ts'

export function TiersEditor({
  tiers, onChange, errors, onBlur,
}: {
  tiers: TierRow[]
  onChange: (tiers: TierRow[]) => void
  errors: (field: string) => Message[]
  onBlur: (field: string) => void
}) {
  const t = useT()

  function setTier(index: number, change: Partial<TierRow>) {
    onChange(tiers.map((row, i) => (i === index ? { ...row, ...change } : row)))
  }

  return (
    <fieldset>
      <legend>{t('planForm.tiers')}</legend>
      <FieldErrors messages={errors('tiers')} />
      <div className="tiers">
        <span className="head">{t('planForm.tierUpTo')}</span>
        <span className="head">{t('planForm.tierPrice')}</span>
        <span />
        {tiers.map((row, i) => {
          const isLast = i === tiers.length - 1
          return (
            <div key={i} style={{ display: 'contents' }}>
              <div className="cell">
                <div className="unit">
                  <input
                    type="number" step="any" min="0" aria-label={t('planForm.tierUpToLabel', { n: i + 1 })}
                    disabled={isLast} placeholder={isLast ? t('common.noLimit') : ''}
                    value={isLast ? '' : row.threshold}
                    onChange={(e) => setTier(i, { threshold: e.target.value })}
                    onBlur={() => onBlur(`tiers[${i}].threshold`)}
                  />
                  <span>kWh</span>
                </div>
                <FieldErrors messages={errors(`tiers[${i}].threshold`)} />
              </div>
              <div className="cell">
                <div className="unit">
                  <input
                    type="number" step="any" min="0" required aria-label={t('planForm.tierPriceLabel', { n: i + 1 })}
                    value={row.price} onChange={(e) => setTier(i, { price: e.target.value })}
                    onBlur={() => onBlur(`tiers[${i}].pricePerKwh`)}
                  />
                  <span>{t('common.perKwh')}</span>
                </div>
                <FieldErrors messages={errors(`tiers[${i}].pricePerKwh`)} />
              </div>
              <button
                type="button" className="icon" aria-label={t('planForm.removeTier', { n: i + 1 })} title={t('planForm.removeTier', { n: i + 1 })}
                disabled={tiers.length === 1} onClick={() => onChange(tiers.filter((_, j) => j !== i))}
              >
                ✕
              </button>
            </div>
          )
        })}
      </div>
      <button type="button" className="small" onClick={() => onChange([...tiers, { threshold: '', price: '' }])}>{t('planForm.addTier')}</button>
    </fieldset>
  )
}
