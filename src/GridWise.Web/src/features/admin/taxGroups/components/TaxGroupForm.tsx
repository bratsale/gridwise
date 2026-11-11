import type { FormEvent } from 'react'
import { useMessageText, useT } from '../../../../shared/i18n/useLanguage.ts'
import type { Message } from '../../../../shared/i18n/message.ts'
import { Banner } from '../../../../shared/ui/Banner.tsx'
import { FieldErrors } from '../../../../shared/ui/FieldErrors.tsx'
import type { TaxGroup } from '../api/taxGroupsApi.ts'

export interface TaxGroupFormProps {
  editing: TaxGroup | null
  code: string
  vat: string
  ecoTax: string
  effectiveFrom: string
  submitting: boolean
  conflict: boolean
  message: Message | null
  general: Message[]
  errors: (field: string) => Message[]
  onCode: (value: string) => void
  onVat: (value: string) => void
  onEcoTax: (value: string) => void
  onEffectiveFrom: (value: string) => void
  onBlur: (field: string) => void
  onSubmit: (event: FormEvent) => void
  onReload: () => void
  onCancel: () => void
}

export function TaxGroupForm(p: TaxGroupFormProps) {
  const t = useT()
  const text = useMessageText()

  return (
    <form className="panel tax-group-form" onSubmit={p.onSubmit} noValidate>
      <h3>{p.editing ? t('taxGroupForm.titleVersion', { code: p.editing.code }) : t('taxGroupForm.titleNew')}</h3>

      {!p.editing && (
        <label>
          {t('taxGroupForm.code')}
          <input required maxLength={60} value={p.code} onChange={(e) => p.onCode(e.target.value)} onBlur={() => p.onBlur('code')} />
          <span className="hint">{t('taxGroupForm.codeHint')}</span>
          <FieldErrors messages={p.errors('code')} />
        </label>
      )}

      <label>
        {t('taxGroupForm.vat')}
        <div className="unit">
          <input
            type="number" step="0.01" min="0" max="100" required
            value={p.vat} onChange={(e) => p.onVat(e.target.value)} onBlur={() => p.onBlur('vat')}
          />
          <span>%</span>
        </div>
        <span className="hint">{t('taxGroupForm.vatHint')}</span>
        <FieldErrors messages={p.errors('vat')} />
      </label>

      <label>
        {t('taxGroupForm.ecoTax')}
        <div className="unit">
          <input
            type="number" step="0.0001" min="0" required
            value={p.ecoTax} onChange={(e) => p.onEcoTax(e.target.value)} onBlur={() => p.onBlur('ecoTax')}
          />
          <span>{t('common.perKwh')}</span>
        </div>
        <span className="hint">{t('taxGroupForm.ecoTaxHint')}</span>
        <FieldErrors messages={p.errors('ecoTax')} />
      </label>

      <label>
        {t('taxGroupForm.effectiveFrom')}
        <input
          type="date" required value={p.effectiveFrom}
          onChange={(e) => p.onEffectiveFrom(e.target.value)} onBlur={() => p.onBlur('effectiveFrom')}
        />
        <FieldErrors messages={p.errors('effectiveFrom')} />
      </label>

      {p.general.length > 0 && <Banner kind="error">{p.general.map(text).join(' ')}</Banner>}
      {p.message && <Banner kind="error" message={p.message} />}
      {p.conflict && (
        <Banner kind="error">
          {t('taxGroups.conflict')} <button type="button" className="small" onClick={p.onReload}>{t('common.reload')}</button>
        </Banner>
      )}

      <div className="actions">
        <button type="submit" className="primary" disabled={p.submitting}>
          {p.submitting ? t('common.saving') : p.editing ? t('taxGroupForm.saveVersion') : t('taxGroupForm.create')}
        </button>
        <button type="button" onClick={p.onCancel}>{t('common.cancel')}</button>
      </div>
    </form>
  )
}
