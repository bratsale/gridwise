import { describe, expect, it } from 'vitest'
import { NAME_MAX_LENGTH, validatePlan, type PlanValues } from './validation.ts'

const today = '2026-11-07'

const valid: PlanValues = {
  name: 'Standard',
  discount: '5',
  effectiveFrom: today,
  tiers: [{ threshold: '100', price: '0.10' }, { threshold: '', price: '0.06' }],
}

const errorsFor = (change: Partial<PlanValues>) => {
  const errors = validatePlan({ ...valid, ...change }, today)
  return Object.fromEntries(Object.entries(errors).map(([field, m]) => [field, 'key' in m ? m.key : m.text]))
}

describe('validatePlan', () => {
  it('accepts valid input', () => {
    expect(validatePlan(valid, today)).toEqual({})
  })

  it('accepts the boundaries', () => {
    expect(errorsFor({ discount: '0' })).toEqual({})
    expect(errorsFor({ discount: '100' })).toEqual({})
    expect(errorsFor({ name: 'x'.repeat(NAME_MAX_LENGTH) })).toEqual({})
    expect(errorsFor({ tiers: [{ threshold: '', price: '0' }] })).toEqual({})
  })

  describe('name', () => {
    it('is required', () => {
      expect(errorsFor({ name: '' })).toEqual({ name: 'planForm.validation.nameRequired' })
      expect(errorsFor({ name: '   ' })).toEqual({ name: 'planForm.validation.nameRequired' })
    })
    it('has a maximum length', () => {
      expect(errorsFor({ name: 'x'.repeat(NAME_MAX_LENGTH + 1) })).toEqual({ name: 'planForm.validation.nameMax' })
    })
  })

  describe('discount', () => {
    it.each(['', '  ', 'abc', '-0.01', '100.01', '150'])('rejects "%s"', (discount) => {
      expect(errorsFor({ discount })).toEqual({ discount: 'planForm.validation.discountRange' })
    })
  })

  describe('effective date', () => {
    it('is required', () => {
      expect(errorsFor({ effectiveFrom: '' })).toEqual({ effectiveFrom: 'planForm.validation.dateRequired' })
    })
    it('cannot be in the past (UTC today is allowed)', () => {
      expect(errorsFor({ effectiveFrom: '2026-11-06' })).toEqual({ effectiveFrom: 'planForm.validation.datePast' })
      expect(errorsFor({ effectiveFrom: today })).toEqual({})
      expect(errorsFor({ effectiveFrom: '2026-11-08' })).toEqual({})
    })
  })

  describe('tiers', () => {
    it('needs at least one', () => {
      expect(errorsFor({ tiers: [] })).toEqual({ tiers: 'planForm.validation.tiersRequired' })
    })
    it('needs a limit on every tier except the last', () => {
      expect(errorsFor({ tiers: [{ threshold: '', price: '1' }, { threshold: '', price: '1' }] })).toEqual({
        'tiers[0].threshold': 'planForm.validation.thresholdRequired',
      })
    })
    it('needs limits greater than 0', () => {
      const tiers = (threshold: string) => [{ threshold, price: '1' }, { threshold: '', price: '1' }]
      expect(errorsFor({ tiers: tiers('0') })).toEqual({ 'tiers[0].threshold': 'planForm.validation.thresholdPositive' })
      expect(errorsFor({ tiers: tiers('-5') })).toEqual({ 'tiers[0].threshold': 'planForm.validation.thresholdPositive' })
    })
    it('needs strictly increasing limits', () => {
      const tiers = (second: string) => [
        { threshold: '100', price: '1' }, { threshold: second, price: '1' }, { threshold: '', price: '1' },
      ]
      expect(errorsFor({ tiers: tiers('100') })).toEqual({ 'tiers[1].threshold': 'planForm.validation.thresholdIncreasing' })
      expect(errorsFor({ tiers: tiers('50') })).toEqual({ 'tiers[1].threshold': 'planForm.validation.thresholdIncreasing' })
      expect(errorsFor({ tiers: tiers('200') })).toEqual({})
    })
    it('ignores the limit of the last tier (it is never sent)', () => {
      expect(errorsFor({ tiers: [{ threshold: '100', price: '1' }, { threshold: 'junk', price: '1' }] })).toEqual({})
    })
    it('needs a price of 0 or more on every tier', () => {
      expect(errorsFor({ tiers: [{ threshold: '100', price: '' }, { threshold: '', price: '-1' }] })).toEqual({
        'tiers[0].pricePerKwh': 'planForm.validation.priceRequired',
        'tiers[1].pricePerKwh': 'planForm.validation.priceMin',
      })
    })
  })

  it('reports several problems at once', () => {
    const errors = validatePlan({ name: '', discount: '-1', effectiveFrom: '', tiers: [] }, today)
    expect(Object.keys(errors).sort()).toEqual(['discount', 'effectiveFrom', 'name', 'tiers'])
  })
})
