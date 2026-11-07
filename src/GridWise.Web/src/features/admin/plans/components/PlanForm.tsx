import type { FormEvent } from 'react'
import { useMessageText, useT } from '../../../../shared/i18n/LanguageProvider.tsx'
import type { Message } from '../../../../shared/i18n/message.ts'
import { Banner } from '../../../../shared/ui/Banner.tsx'
import { FieldErrors } from '../../../../shared/ui/FieldErrors.tsx'
import type { Plan } from '../api/plansApi.ts'
import type { TierRow } from '../validation.ts'
import { TiersEditor } from './TiersEditor.tsx'

export interface PlanFormProps {
  editing: Plan | null
  name: string
  discount: string
  effectiveFrom: string
  tiers: TierRow[]
  submitting: boolean
  conflict: boolean
  message: Message | null
  general: Message[]
  errors: (field: string) => Message[]
  onName: (value: string) => void
  onDiscount: (value: string) => void
  onEffectiveFrom: (value: string) => void
  onTiers: (tiers: TierRow[]) => void
  onBlur: (field: string) => void
  onSubmit: (event: FormEvent) => void
  onReload: () => void
  onCancel: () => void
}

export function PlanForm(p: PlanFormProps) {
  const t = useT()
  const text = useMessageText()

  return (
    <form className="panel plan-form" onSubmit={p.onSubmit} noValidate>
      <h3>{p.editing ? t('planForm.titleVersion', { name: p.editing.name }) : t('planForm.titleNew')}</h3>

      <label>
        {t('planForm.name')}
        <input required value={p.name} onChange={(e) => p.onName(e.target.value)} onBlur={() => p.onBlur('name')} />
        <FieldErrors messages={p.errors('name')} />
      </label>

      <label>
        {t('planForm.discount')}
        <div className="unit">
          <input
            type="number" step="0.01" min="0" max="100" required
            value={p.discount} onChange={(e) => p.onDiscount(e.target.value)} onBlur={() => p.onBlur('discount')}
          />
          <span>%</span>
        </div>
        <span className="hint">{t('planForm.discountHint')}</span>
        <FieldErrors messages={p.errors('discount')} />
      </label>

      <label>
        {t('planForm.effectiveFrom')}
        <input
          type="date" required value={p.effectiveFrom}
          onChange={(e) => p.onEffectiveFrom(e.target.value)} onBlur={() => p.onBlur('effectiveFrom')}
        />
        <FieldErrors messages={p.errors('effectiveFrom')} />
      </label>

      <TiersEditor tiers={p.tiers} onChange={p.onTiers} errors={p.errors} onBlur={p.onBlur} />

      {p.general.length > 0 && <Banner kind="error">{p.general.map(text).join(' ')}</Banner>}
      {p.message && <Banner kind="error">{text(p.message)}</Banner>}
      {p.conflict && (
        <Banner kind="error">
          {t('plans.conflict')} <button type="button" className="small" onClick={p.onReload}>{t('common.reload')}</button>
        </Banner>
      )}

      <div className="actions">
        <button type="submit" className="primary" disabled={p.submitting}>
          {p.submitting ? t('planForm.saving') : p.editing ? t('planForm.saveVersion') : t('planForm.create')}
        </button>
        <button type="button" onClick={p.onCancel}>{t('common.cancel')}</button>
      </div>
    </form>
  )
}
