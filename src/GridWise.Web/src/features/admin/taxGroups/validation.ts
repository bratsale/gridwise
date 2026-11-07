import { msg, type Message } from '../../../shared/i18n/message.ts'

export interface TaxGroupValues {
  code: string
  vat: string
  ecoTax: string
  effectiveFrom: string
}

export const CODE_MAX_LENGTH = 50

// Client-side checks before sending; the server stays authoritative. Keys match the server's error keys.
// The code is only checked when creating: a new version keeps it.
export function validateTaxGroup(values: TaxGroupValues, today: string, checkCode: boolean): Record<string, Message> {
  const errors: Record<string, Message> = {}

  if (checkCode) {
    if (values.code === '') errors.code = msg('taxGroupForm.validation.codeRequired')
    else if (values.code.length > CODE_MAX_LENGTH) errors.code = msg('taxGroupForm.validation.codeMax', { max: CODE_MAX_LENGTH })
    else if (!/^[a-z][a-z0-9-]*$/.test(values.code)) errors.code = msg('taxGroupForm.validation.codeFormat')
  }

  const vat = Number(values.vat)
  if (values.vat.trim() === '' || Number.isNaN(vat) || vat < 0 || vat > 100) errors.vat = msg('taxGroupForm.validation.vatRange')

  const ecoTax = Number(values.ecoTax)
  if (values.ecoTax.trim() === '' || Number.isNaN(ecoTax)) errors.ecoTax = msg('taxGroupForm.validation.ecoTaxRequired')
  else if (ecoTax < 0) errors.ecoTax = msg('taxGroupForm.validation.ecoTaxMin')
  else if (!/e/i.test(values.ecoTax) && (values.ecoTax.split('.')[1] ?? '').length > 4) errors.ecoTax = msg('taxGroupForm.validation.ecoTaxDecimals')

  if (values.effectiveFrom === '') errors.effectiveFrom = msg('taxGroupForm.validation.dateRequired')
  else if (values.effectiveFrom < today) errors.effectiveFrom = msg('taxGroupForm.validation.datePast')

  return errors
}
