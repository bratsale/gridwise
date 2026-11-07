import { describe, expect, it } from 'vitest'
import { en } from './en.ts'
import { sr } from './sr.ts'

const placeholders = (text: string) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort()

describe('dictionaries', () => {
  const enKeys = Object.keys(en).sort()
  const srKeys = Object.keys(sr).sort()

  it('Serbian has every English key, and no others', () => {
    expect(srKeys.filter((k) => !enKeys.includes(k))).toEqual([])
    expect(enKeys.filter((k) => !srKeys.includes(k))).toEqual([])
  })

  it('has no empty texts', () => {
    for (const dictionary of [en, sr]) {
      for (const [key, text] of Object.entries(dictionary)) expect(text.trim(), key).not.toBe('')
    }
  })

  it('uses the same {placeholders} in both languages', () => {
    for (const key of enKeys as (keyof typeof en)[]) {
      expect(placeholders(sr[key]), key).toEqual(placeholders(en[key]))
    }
  })
})
