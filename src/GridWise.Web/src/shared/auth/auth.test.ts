// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'

// oidc-client-ts replaced by a fake. By default there is no Keycloak session (silent sign-in fails); each test can change that.
// The handlers the app registers on the events are kept, so a test can raise "token expired" or "renew failed" itself.
const handlers = { expired: [] as (() => void)[], renewError: [] as (() => void)[], signedOut: [] as (() => void)[] }
const signinRedirect = vi.fn(async () => {})
const signoutRedirect = vi.fn(async () => {})
const removeUser = vi.fn(async () => {})
const signinSilent = vi.fn<(args?: { forceIframeAuth?: boolean }) => Promise<unknown>>()
// The user the UserManager has stored (the one this tab is signed in as), or null.
const getUser = vi.fn<() => Promise<unknown>>(async () => null)
vi.mock('oidc-client-ts', () => {
  class InMemoryWebStorage {}
  class WebStorageStateStore {}
  class UserManager {
    events = {
      addUserLoaded: vi.fn(),
      addUserUnloaded: vi.fn(),
      addAccessTokenExpired: (cb: () => void) => handlers.expired.push(cb),
      addSilentRenewError: (cb: () => void) => handlers.renewError.push(cb),
      addUserSignedOut: (cb: () => void) => handlers.signedOut.push(cb),
    }
    signinRedirect = signinRedirect
    signoutRedirect = signoutRedirect
    signinSilent = signinSilent
    removeUser = removeUser
    getUser = getUser
    signinRedirectCallback = vi.fn()
  }
  return { UserManager, InMemoryWebStorage, WebStorageStateStore }
})

const loginRequired = () => Promise.reject(new Error('login_required'))
// A user like the one oidc-client-ts returns, with the customer role in the access token.
const payload = btoa(JSON.stringify({ realm_access: { roles: ['customer'] } }))
const user = { expired: false, access_token: `h.${payload}.s`, profile: { sub: 'anna-id', preferred_username: 'anna' } }
const otherUser = { ...user, access_token: `h3.${payload}.s3`, profile: { sub: 'admin2-id', preferred_username: 'admin2' } }

// Each test gets a fresh auth module (its state, the guard and the redirect flag live in the module).
async function startAnonymous() {
  vi.resetModules()
  handlers.expired = []
  handlers.renewError = []
  handlers.signedOut = []
  signinSilent.mockImplementation(loginRequired)
  const auth = await import('./auth.ts')
  await auth.initAuth()
  return auth
}

// Starts signed in: the silent sign-in at start finds a Keycloak session.
async function startSignedIn() {
  vi.resetModules()
  handlers.expired = []
  handlers.renewError = []
  handlers.signedOut = []
  signinSilent.mockResolvedValueOnce(user)
  const auth = await import('./auth.ts')
  await auth.initAuth()
  expect(auth.getAuthState().status).toBe('authenticated')
  signinSilent.mockClear()
  getUser.mockResolvedValue(user) // the tab now has anna stored
  return auth
}

describe('auth', () => {
  beforeEach(() => {
    signinRedirect.mockClear()
    signoutRedirect.mockClear()
    removeUser.mockClear()
    signinSilent.mockReset()
    getUser.mockReset()
    getUser.mockResolvedValue(null)
    window.history.replaceState(null, '', '/#admin/batch')
  })

  it('without a Keycloak session the user is anonymous, and a 401 from the API sends them to the login page', async () => {
    const { getAuthState } = await startAnonymous()
    const { request } = await import('../api/client.ts')
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 401 })))

    expect(getAuthState().status).toBe('anonymous')

    await request('batch', '/jobs')
    await request('batch', '/jobs') // a second 401 does not start a second login

    expect(signinRedirect).toHaveBeenCalledOnce()
    // After the login the app comes back to the page the user was on.
    expect(signinRedirect).toHaveBeenCalledWith({ state: { returnTo: '#admin/batch' } })
    vi.unstubAllGlobals()
  })

  it('recovery asks Keycloak through the hidden iframe (the session cookie), not with the refresh token', async () => {
    const { recoverSession, getAuthState } = await startSignedIn()
    signinSilent.mockResolvedValueOnce({ ...user, access_token: `h2.${payload}.s2` })

    expect(await recoverSession()).toBe(true)

    expect(signinSilent).toHaveBeenCalledWith(expect.objectContaining({ forceIframeAuth: true }))
    expect(getAuthState()).toEqual(expect.objectContaining({ status: 'authenticated', accessToken: `h2.${payload}.s2` }))
    expect(removeUser).not.toHaveBeenCalled()
  })

  it('when recovery fails (no Keycloak session), the user is removed and the login screen is shown', async () => {
    const { recoverSession, getAuthState } = await startSignedIn()
    signinSilent.mockImplementationOnce(loginRequired)

    expect(await recoverSession()).toBe(false)

    expect(removeUser).toHaveBeenCalledOnce()
    expect(getAuthState().status).toBe('anonymous')
    expect(signinRedirect).not.toHaveBeenCalled() // the user decides when to log in
  })

  it('one attempt at a time: expiry, renew error and API calls arriving together share one silent sign-in', async () => {
    const { recoverSession } = await startSignedIn()
    let finish!: (value: unknown) => void
    signinSilent.mockImplementationOnce(() => new Promise((resolve) => (finish = resolve)))

    handlers.expired.forEach((cb) => cb())
    handlers.renewError.forEach((cb) => cb())
    const fromApi = [recoverSession(), recoverSession()]
    await vi.waitFor(() => expect(signinSilent).toHaveBeenCalled()) // it starts after reading the stored user
    finish(user)

    expect(await Promise.all(fromApi)).toEqual([true, true])
    expect(signinSilent).toHaveBeenCalledOnce()

    // Once it has finished, a later expiry starts a new attempt.
    signinSilent.mockResolvedValueOnce(user)
    await recoverSession()
    expect(signinSilent).toHaveBeenCalledTimes(2)
  })

  it('an expired token does not show the login screen at once: it stays signed in while the silent sign-in runs', async () => {
    const { getAuthState } = await startSignedIn()
    let finish!: (value: unknown) => void
    signinSilent.mockImplementationOnce(() => new Promise((resolve) => (finish = resolve)))

    handlers.expired.forEach((cb) => cb())

    expect(getAuthState().status).toBe('authenticated')
    await vi.waitFor(() => expect(signinSilent).toHaveBeenCalledWith(expect.objectContaining({ forceIframeAuth: true })))
    expect(getAuthState().status).toBe('authenticated') // still, while the silent sign-in runs
    finish(user)
  })

  it('a failed automatic renewal (refresh token) falls back to the iframe', async () => {
    const { getAuthState } = await startSignedIn()
    signinSilent.mockResolvedValueOnce(user)

    handlers.renewError.forEach((cb) => cb())
    await vi.waitFor(() => expect(signinSilent).toHaveBeenCalledWith(expect.objectContaining({ forceIframeAuth: true })))

    expect(getAuthState().status).toBe('authenticated')
  })

  it('after logout nothing is recovered silently, so the next login asks for the password', async () => {
    const { recoverSession, logout } = await startSignedIn()

    logout()
    handlers.expired.forEach((cb) => cb())

    expect(await recoverSession()).toBe(false)
    expect(signoutRedirect).toHaveBeenCalledOnce()
    expect(signinSilent).not.toHaveBeenCalled()
  })

  it('recovery never switches to another user: if Keycloak now has someone else signed in, the login screen is shown', async () => {
    const { recoverSession, getAuthState } = await startSignedIn()
    signinSilent.mockResolvedValueOnce(otherUser) // another tab logged out and in as admin2 (shared Keycloak session cookie)

    expect(await recoverSession()).toBe(false)

    expect(removeUser).toHaveBeenCalledOnce()
    expect(getAuthState()).toEqual(expect.objectContaining({ status: 'anonymous', name: null, accessToken: null }))
  })

  it('recovery with the same user keeps the tab signed in', async () => {
    const { recoverSession, getAuthState } = await startSignedIn()
    signinSilent.mockResolvedValueOnce({ ...user, access_token: `h4.${payload}.s4` })

    expect(await recoverSession()).toBe(true)
    expect(getAuthState()).toEqual(expect.objectContaining({ status: 'authenticated', name: 'anna' }))
  })

  it('a sign-out in another tab (reported by the session monitor) signs this tab out too', async () => {
    const { getAuthState } = await startSignedIn()

    handlers.signedOut.forEach((cb) => cb())

    await vi.waitFor(() => expect(getAuthState().status).toBe('anonymous'))
    expect(removeUser).toHaveBeenCalledOnce()
    expect(signinRedirect).not.toHaveBeenCalled() // the login screen, not an automatic login
  })
})
