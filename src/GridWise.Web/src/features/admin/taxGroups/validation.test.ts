import { describe, expect, it } from 'vitest'
import { CODE_MAX_LENGTH, validateTaxGroup, type TaxGroupValues } from './validation.ts'

const today = '2026-11-07'

const valid: TaxGroupValues = { code: 'household-2', vat: '20', ecoTax: '0.0123', effectiveFrom: today }

const errorsFor = (change: Partial<TaxGroupValues>, checkCode = true) => {
  const errors = validateTaxGroup({ ...valid, ...change }, today, checkCode)
  return Object.fromEntries(Object.entries(errors).map(([field, m]) => [field, 'key' in m ? m.key : m.text]))
}

describe('validateTaxGroup', () => {
  it('accepts valid input', () => {
    expect(validateTaxGroup(valid, today, true)).toEqual({})
  })

  it('accepts the boundaries', () => {
    expect(errorsFor({ vat: '0', ecoTax: '0' })).toEqual({})
    expect(errorsFor({ vat: '100', ecoTax: '1' })).toEqual({})
    expect(errorsFor({ code: 'a'.repeat(CODE_MAX_LENGTH) })).toEqual({})
    expect(errorsFor({ ecoTax: '0.0001' })).toEqual({})
  })

  describe('code', () => {
    it('is required', () => {
      expect(errorsFor({ code: '' })).toEqual({ code: 'taxGroupForm.validation.codeRequired' })
    })
    it('has a maximum length', () => {
      expect(errorsFor({ code: 'a'.repeat(CODE_MAX_LENGTH + 1) })).toEqual({ code: 'taxGroupForm.validation.codeMax' })
    })
    it.each(['Household', 'house_hold', 'house hold', '1house', '-house', 'house!', 'hoüse'])('rejects "%s"', (code) => {
      expect(errorsFor({ code })).toEqual({ code: 'taxGroupForm.validation.codeFormat' })
    })
    it('is not checked for a new version', () => {
      expect(errorsFor({ code: '' }, false)).toEqual({})
    })
  })

  describe('VAT', () => {
    it.each(['', ' ', 'abc', '-1', '100.01'])('rejects "%s"', (vat) => {
      expect(errorsFor({ vat })).toEqual({ vat: 'taxGroupForm.validation.vatRange' })
    })
  })

  describe('eco tax', () => {
    it('is required', () => {
      expect(errorsFor({ ecoTax: '' })).toEqual({ ecoTax: 'taxGroupForm.validation.ecoTaxRequired' })
      expect(errorsFor({ ecoTax: 'abc' })).toEqual({ ecoTax: 'taxGroupForm.validation.ecoTaxRequired' })
    })
    it('cannot be negative', () => {
      expect(errorsFor({ ecoTax: '-0.1' })).toEqual({ ecoTax: 'taxGroupForm.validation.ecoTaxMin' })
    })
    it('allows at most 4 decimals', () => {
      expect(errorsFor({ ecoTax: '0.12345' })).toEqual({ ecoTax: 'taxGroupForm.validation.ecoTaxDecimals' })
    })
  })

  describe('effective date', () => {
    it('is required', () => {
      expect(errorsFor({ effectiveFrom: '' })).toEqual({ effectiveFrom: 'taxGroupForm.validation.dateRequired' })
    })
    it('cannot be in the past (UTC today is allowed)', () => {
      expect(errorsFor({ effectiveFrom: '2026-11-06' })).toEqual({ effectiveFrom: 'taxGroupForm.validation.datePast' })
      expect(errorsFor({ effectiveFrom: today })).toEqual({})
    })
  })
})
