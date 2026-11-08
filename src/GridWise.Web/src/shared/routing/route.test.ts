import { describe, expect, it } from 'vitest'
import { parseRoute, routeToHash, type Route } from './route.ts'

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

  it('falls back to the plans page', () => {
    for (const hash of ['', '#', '#admin', '#nonsense']) expect(parseRoute(hash)).toEqual({ tab: 'admin', section: 'plans' })
  })
})
