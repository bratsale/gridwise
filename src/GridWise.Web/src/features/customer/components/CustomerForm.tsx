import type { FormEvent } from 'react'
import { taxGroupLabel } from '../../../shared/i18n/taxGroupName.ts'
import { useT } from '../../../shared/i18n/useLanguage.ts'
import type { Message } from '../../../shared/i18n/message.ts'
import { FieldErrors } from '../../../shared/ui/FieldErrors.tsx'
import type { TaxGroup } from '../api/customerApi.ts'
import type { CustomerField } from '../validation.ts'

export function CustomerForm({
  consumption, taxGroup, taxGroups, taxGroupsLoading, submitting, errors,
  onConsumption, onTaxGroup, onBlur, onSubmit,
}: {
  consumption: string
  taxGroup: string
  taxGroups: TaxGroup[]
  taxGroupsLoading: boolean
  submitting: boolean
  errors: (field: CustomerField) => Message[]
  onConsumption: (value: string) => void
  onTaxGroup: (value: string) => void
  onBlur: (field: CustomerField) => void
  onSubmit: (event: FormEvent) => void
}) {
  const t = useT()
  return (
    <form onSubmit={onSubmit} className="form" noValidate>
      <label>
        {t('customer.consumption')}
        <div className="unit">
          <input
            type="number"
            step="0.001"
            min="0"
            required
            value={consumption}
            onChange={(e) => onConsumption(e.target.value)}
            onBlur={() => onBlur('consumptionKwh')}
          />
          <span>kWh</span>
        </div>
        <FieldErrors messages={errors('consumptionKwh')} />
      </label>

      <label>
        {t('customer.taxGroup')}
        <select required value={taxGroup} onChange={(e) => onTaxGroup(e.target.value)} onBlur={() => onBlur('taxGroup')}>
          {taxGroupsLoading && <option value="">{t('common.loading')}</option>}
          {taxGroups.map((g) => (
            <option key={g.code} value={g.code}>{taxGroupLabel(t, g.code)}</option>
          ))}
        </select>
        <FieldErrors messages={errors('taxGroup')} />
      </label>

      <button type="submit" className="primary" disabled={submitting || taxGroups.length === 0}>
        {submitting ? t('customer.calculating') : t('customer.calculate')}
      </button>
    </form>
  )
}
