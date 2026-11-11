import { describe, expect, it } from 'vitest'
import { isKnownHash, parseRoute, routeToHash, type Route } from './route.ts'

describe('route hash', () => {
  const routes: [string, Route][] = [
    ['#customer', { tab: 'customer' }],
    ['#admin/plans', { tab: 'admin', section: 'plans' }],
    ['#admin/tax-groups', { tab: 'admin', section: 'taxGroups' }],
    ['#admin/batch', { tab: 'admin', section: 'batch' }],
  ]

  it.each(routes)('%s round-trips', (hash, route) => {
    expect(parseRoute(hash)).toEqual(route)
    expect(routeToHash(route)).toBe(hash)
  })

  it.each(routes)('%s is a known page', (hash) => {
    expect(isKnownHash(hash)).toBe(true)
    expect(isKnownHash(hash.replace('#', '#/'))).toBe(true)
  })

  it('does not know an empty, partial or unknown hash', () => {
    for (const hash of ['', '#', '#admin', '#nonsense', '#customer/x', '#admin/plans/1']) expect(isKnownHash(hash)).toBe(false)
  })

  it('falls back to the plans page', () => {
    for (const hash of ['', '#', '#admin', '#nonsense']) expect(parseRoute(hash)).toEqual({ tab: 'admin', section: 'plans' })
  })
})
