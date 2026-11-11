import { describe, expect, it } from 'vitest'
import { en, type TranslationKey } from './en.ts'
import { sr } from './sr.ts'
import { taxGroupLabel } from './taxGroupName.ts'

const tFor = (dictionary: Record<TranslationKey, string>) => (key: TranslationKey) => dictionary[key]

describe('taxGroupLabel', () => {
  it('shows the translated name next to the code', () => {
    expect(taxGroupLabel(tFor(sr), 'household')).toBe('Domaćinstvo (household)')
    expect(taxGroupLabel(tFor(sr), 'business')).toBe('Privreda (business)')
    expect(taxGroupLabel(tFor(en), 'household')).toBe('Household (household)')
  })

  it('shows an unknown code as it is', () => {
    expect(taxGroupLabel(tFor(sr), 'farm')).toBe('farm')
  })
})
