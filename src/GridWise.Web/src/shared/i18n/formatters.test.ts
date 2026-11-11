import { describe, expect, it } from 'vitest'
import { createFormatters } from './formatters.ts'

describe('rate (eco tax per kWh)', () => {
  it('always shows 4 decimals, so a column of rates lines up', () => {
    const en = createFormatters('en')
    expect(en.rate('0.01')).toBe('0.0100')
    expect(en.rate(0.005)).toBe('0.0050')
    expect(en.rate('0.1234')).toBe('0.1234')
  })

  it('uses the decimal comma in Serbian', () => {
    const sr = createFormatters('sr')
    expect(sr.rate('0.01')).toBe('0,0100')
    expect(sr.rate(0.005)).toBe('0,0050')
  })
})
