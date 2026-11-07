import { describe, expect, it } from 'vitest'
import { validateCustomer } from './validation.ts'

const keyOf = (values: { consumption: string; taxGroup: string }, field: 'consumptionKwh' | 'taxGroup') => {
  const message = validateCustomer(values)[field]
  return message && 'key' in message ? message.key : undefined
}

describe('validateCustomer', () => {
  it('accepts valid input', () => {
    expect(validateCustomer({ consumption: '250', taxGroup: 'household' })).toEqual({})
    expect(validateCustomer({ consumption: '0', taxGroup: 'household' })).toEqual({})
    expect(validateCustomer({ consumption: '1.234', taxGroup: 'household' })).toEqual({})
    expect(validateCustomer({ consumption: '1e3', taxGroup: 'household' })).toEqual({})
  })

  it('requires the consumption', () => {
    expect(keyOf({ consumption: '', taxGroup: 'a' }, 'consumptionKwh')).toBe('customer.validation.consumptionRequired')
    expect(keyOf({ consumption: '   ', taxGroup: 'a' }, 'consumptionKwh')).toBe('customer.validation.consumptionRequired')
    expect(keyOf({ consumption: 'abc', taxGroup: 'a' }, 'consumptionKwh')).toBe('customer.validation.consumptionRequired')
  })

  it('rejects a negative consumption', () => {
    expect(keyOf({ consumption: '-1', taxGroup: 'a' }, 'consumptionKwh')).toBe('customer.validation.consumptionMin')
  })

  it('allows at most 3 decimals', () => {
    expect(keyOf({ consumption: '1.2345', taxGroup: 'a' }, 'consumptionKwh')).toBe('customer.validation.consumptionDecimals')
    expect(keyOf({ consumption: '0.0001', taxGroup: 'a' }, 'consumptionKwh')).toBe('customer.validation.consumptionDecimals')
  })

  it('requires a tax group', () => {
    expect(keyOf({ consumption: '1', taxGroup: '' }, 'taxGroup')).toBe('customer.validation.taxGroupRequired')
  })

  it('reports both fields at once', () => {
    expect(Object.keys(validateCustomer({ consumption: '', taxGroup: '' })).sort()).toEqual(['consumptionKwh', 'taxGroup'])
  })
})
