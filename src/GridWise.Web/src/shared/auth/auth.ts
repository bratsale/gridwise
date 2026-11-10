import { useSyncExternalStore } from 'react'
import { UserManager, type User } from 'oidc-client-ts'
import { oidcSettings } from './oidcSettings.ts'
import { rolesFromAccessToken } from './roles.ts'
import { configureSession } from './session.ts'

export interface AuthState {
  status: 'loading' | 'anonymous' | 'authenticated'
  name: string | null
  roles: string[]
  accessToken: string | null
}

const anonymous: AuthState = { status: 'anonymous', name: null, roles: [], accessToken: null }

let state: AuthState = { ...anonymous, status: 'loading' }
const listeners = new Set<() => void>()
let manager: UserManager | null = null
let redirecting = false

function setState(next: AuthState) {
  state = next
  listeners.forEach((listener) => listener())
}

function fromUser(user: User | null): AuthState {
  if (!user || user.expired) return anonymous
  const profile = user.profile as { preferred_username?: string; name?: string }
  return {
    status: 'authenticated',
    name: profile.preferred_username ?? profile.name ?? null,
    roles: rolesFromAccessToken(user.access_token),
    accessToken: user.access_token,
  }
}

function userManager(): UserManager {
  manager ??= new UserManager(oidcSettings())
  return manager
}

/** Sends the user to Keycloak's login page; afterwards the app opens on the page they were on. */
export function login() {
  if (redirecting) return // several requests can answer 401 at the same time
  redirecting = true
  void userManager().signinRedirect({ state: { returnTo: window.location.hash } })
}

/** Ends the Keycloak session too, then comes back to the app (logged out). */
export function logout() {
  redirecting = true
  void userManager().signoutRedirect()
}

/**
 * Called once at start: finishes a login that just came back from Keycloak (?code=...), otherwise tries to restore the session
 * silently. Renewals (a minute before the token expires) update the state, so every request uses the current token.
 */
export async function initAuth() {
  const um = userManager()
  um.events.addUserLoaded((user) => setState(fromUser(user)))
  um.events.addUserUnloaded(() => setState(anonymous))
  um.events.addAccessTokenExpired(() => setState(anonymous))
  configureSession({ accessToken: () => state.accessToken, onUnauthorized: login })

  const params = new URLSearchParams(window.location.search)
  if (params.has('state') && (params.has('code') || params.has('error'))) {
    try {
      const user = await um.signinRedirectCallback()
      const returnTo = (user.state as { returnTo?: string } | undefined)?.returnTo ?? ''
      window.history.replaceState(null, '', `${window.location.pathname}${returnTo}`)
      setState(fromUser(user))
      return
    } catch {
      window.history.replaceState(null, '', window.location.pathname) // a stale or failed login: start over below
    }
  }

  try {
    setState(fromUser(await um.signinSilent()))
  } catch {
    setState(anonymous) // no Keycloak session (login_required): the app offers the login button
  }
}

export function getAuthState(): AuthState {
  return state
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useAuth(): AuthState {
  return useSyncExternalStore(subscribe, getAuthState)
}
