import { describe, expect, it } from 'vitest'
import { defaultRoute, pageFor, primaryRole, rolesFromAccessToken } from './roles.ts'

// A token with the given payload; only the payload is read (the signature is checked by the gateway and the services).
const token = (payload: object) => {
  const bytes = new TextEncoder().encode(JSON.stringify(payload))
  const base64url = btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `eyJhbGciOiJSUzI1NiJ9.${base64url}.signature`
}

describe('roles from the access token', () => {
  it('reads realm_access.roles', () => {
    expect(rolesFromAccessToken(token({ realm_access: { roles: ['admin', 'offline_access'] } }))).toEqual(['admin', 'offline_access'])
  })

  it('reads payloads whose base64url needs padding and uses - and _', () => {
    const roles = ['customer', '>>>???'] // characters that turn into + and / in plain base64
    expect(rolesFromAccessToken(token({ sub: 'x', realm_access: { roles } }))).toEqual(roles)
  })

  it('has no roles without a token, without realm_access or with a broken token', () => {
    for (const value of [null, undefined, '', 'not-a-jwt', token({ sub: 'x' }), 'a.%%%.c']) expect(rolesFromAccessToken(value)).toEqual([])
  })
})

describe('role gating', () => {
  const admin = ['admin', 'default-roles-gridwise']
  const customer = ['customer', 'default-roles-gridwise']
  const neither = ['default-roles-gridwise']

  it('admin pages are for admins only', () => {
    const route = { tab: 'admin', section: 'plans' } as const
    expect(pageFor(route, admin)).toBe('admin')
    expect(pageFor(route, customer)).toBe('forbidden')
    expect(pageFor(route, neither)).toBe('forbidden')
  })

  it('the customer page is for customers and admins', () => {
    const route = { tab: 'customer' } as const
    expect(pageFor(route, customer)).toBe('customer')
    expect(pageFor(route, admin)).toBe('customer')
    expect(pageFor(route, neither)).toBe('forbidden')
  })

  it('opens the page that fits the role and names the role', () => {
    expect(defaultRoute(admin)).toEqual({ tab: 'admin', section: 'plans' })
    expect(defaultRoute(customer)).toEqual({ tab: 'customer' })
    expect([primaryRole(admin), primaryRole(customer), primaryRole(neither)]).toEqual(['admin', 'customer', null])
  })
})
