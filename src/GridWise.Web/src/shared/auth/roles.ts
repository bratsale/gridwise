import type { Route } from '../routing/route.ts'

export type Role = 'admin' | 'customer'

// Keycloak puts the realm roles in the access token (realm_access.roles). The token is only read here, never trusted: the
// gateway and the services check it. The roles decide what the app shows.
export function rolesFromAccessToken(token: string | null | undefined): string[] {
  const payload = token?.split('.')[1]
  if (!payload) return []
  try {
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(payload.length / 4) * 4, '='))
    const roles = (JSON.parse(json) as { realm_access?: { roles?: unknown } }).realm_access?.roles
    return Array.isArray(roles) ? roles.filter((r): r is string => typeof r === 'string') : []
  } catch {
    return []
  }
}

export const isAdmin = (roles: readonly string[]) => roles.includes('admin')
export const isCustomerOrAdmin = (roles: readonly string[]) => roles.includes('customer') || roles.includes('admin')

// The role shown next to the user name.
export const primaryRole = (roles: readonly string[]): Role | null =>
  isAdmin(roles) ? 'admin' : roles.includes('customer') ? 'customer' : null

export type Page = 'admin' | 'customer' | 'forbidden'

// The admin pages are for admins; the customer page for customers and admins. Anything else shows the missing-permission state.
export function pageFor(route: Route, roles: readonly string[]): Page {
  if (route.tab === 'admin') return isAdmin(roles) ? 'admin' : 'forbidden'
  return isCustomerOrAdmin(roles) ? 'customer' : 'forbidden'
}

// Where a user lands when the address has no page: admins on the plans, everyone else on the customer page.
export const defaultRoute = (roles: readonly string[]): Route =>
  isAdmin(roles) ? { tab: 'admin', section: 'plans' } : { tab: 'customer' }
