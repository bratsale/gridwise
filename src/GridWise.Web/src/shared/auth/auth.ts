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
let recovering: Promise<boolean> | null = null
// While a recovery runs: the user this tab was signed in as. A user loaded for someone else is not shown, not even for a moment.
let recoveringFor: string | undefined

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

/**
 * Renews the session without user interaction through the hidden iframe (prompt=none), which uses Keycloak's session cookie.
 * Used when the access token expired or the automatic renewal with the refresh token failed: for example after the tab slept
 * (back/forward cache, background tab), when the refresh token can be stale although the Keycloak session is still alive.
 * Only one attempt runs at a time: expiry, a failed renewal and several 401s can arrive together and share it.
 * It never switches to another user: the tabs of a browser share Keycloak's session cookie, so another tab may have logged in as
 * someone else meanwhile. Then, as when there is no session (login_required) or no answer (timeout), the user is removed and the
 * login screen shows. Resolves true only when signed in again as the same user.
 */
export function recoverSession(): Promise<boolean> {
  if (redirecting) return Promise.resolve(false) // logging in or out: nothing to recover (after logout the password is asked)
  recovering ??= (async () => {
    const um = userManager()
    const previous = (await um.getUser())?.profile.sub
    recoveringFor = previous
    try {
      const user = await um.signinSilent({ forceIframeAuth: true })
      if (previous && user?.profile.sub !== previous) throw new Error('another user is signed in to Keycloak now')
      setState(fromUser(user))
      return state.status === 'authenticated'
    } catch {
      await um.removeUser()
      setState(anonymous)
      return false
    } finally {
      recoveringFor = undefined
      recovering = null
    }
  })()
  return recovering
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
  um.events.addUserLoaded((user) => {
    if (recoveringFor && user.profile.sub !== recoveringFor) return // someone else: recoverSession shows the login screen
    setState(fromUser(user))
  })
  // The session monitor saw Keycloak's session end, or change to another user (a logout or login in another tab): sign out here
  // too and show the login screen. Not during this tab's own login or logout redirect.
  um.events.addUserSignedOut(async () => {
    if (redirecting) return
    await um.removeUser()
    setState(anonymous)
  })
  um.events.addUserUnloaded(() => setState(anonymous))
  // The login screen is shown only when the silent recovery fails, not as soon as the token expires.
  um.events.addAccessTokenExpired(() => void recoverSession())
  um.events.addSilentRenewError(() => void recoverSession())
  configureSession({ accessToken: () => state.accessToken, recover: recoverSession, onUnauthorized: login })

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
