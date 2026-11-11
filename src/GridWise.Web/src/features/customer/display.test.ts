import { describe, expect, it } from 'vitest'
import { asDeduction, preferredTaxGroup, TOP_PLANS, visiblePlans } from './display.ts'

describe('preferredTaxGroup', () => {
  it('prefers household, wherever it is in the list', () => {
    expect(preferredTaxGroup([{ code: 'business' }, { code: 'household' }])).toBe('household')
  })

  it('falls back to the first group, or nothing', () => {
    expect(preferredTaxGroup([{ code: 'business' }, { code: 'farm' }])).toBe('business')
    expect(preferredTaxGroup([])).toBe('')
  })
})

describe('visiblePlans', () => {
  const plans = Array.from({ length: 25 }, (_, i) => i)

  it('shows the first 10 until all are asked for', () => {
    expect(TOP_PLANS).toBe(10)
    expect(visiblePlans(plans, false)).toEqual(plans.slice(0, 10))
    expect(visiblePlans(plans, true)).toEqual(plans)
  })

  it('shows a short list as it is', () => {
    expect(visiblePlans([1, 2, 3], false)).toEqual([1, 2, 3])
  })
})

describe('asDeduction', () => {
  it('turns the discount into a negative amount', () => {
    expect(asDeduction('1.69')).toBe(-1.69)
    expect(asDeduction(2)).toBe(-2)
  })

  it('keeps no discount as 0, not -0', () => {
    expect(Object.is(asDeduction('0.00'), 0)).toBe(true)
  })
})
