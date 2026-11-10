// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'

// oidc-client-ts replaced by a fake: no Keycloak session (silent sign-in fails), and signinRedirect is recorded.
const signinRedirect = vi.fn(async () => {})
const signinSilent = vi.fn(async () => {
  throw new Error('login_required')
})
vi.mock('oidc-client-ts', () => {
  class InMemoryWebStorage {}
  class WebStorageStateStore {}
  class UserManager {
    events = { addUserLoaded: vi.fn(), addUserUnloaded: vi.fn(), addAccessTokenExpired: vi.fn() }
    signinRedirect = signinRedirect
    signinSilent = signinSilent
    signinRedirectCallback = vi.fn()
  }
  return { UserManager, InMemoryWebStorage, WebStorageStateStore }
})

describe('auth', () => {
  beforeEach(() => {
    signinRedirect.mockClear()
    window.history.replaceState(null, '', '/#admin/batch')
  })

  it('without a Keycloak session the user is anonymous, and a 401 from the API sends them to the login page', async () => {
    const { initAuth, getAuthState } = await import('./auth.ts')
    const { request } = await import('../api/client.ts')
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 401 })))

    await initAuth()
    expect(getAuthState().status).toBe('anonymous')

    await request('batch', '/jobs')
    await request('batch', '/jobs') // a second 401 does not start a second login

    expect(signinRedirect).toHaveBeenCalledOnce()
    // After the login the app comes back to the page the user was on.
    expect(signinRedirect).toHaveBeenCalledWith({ state: { returnTo: '#admin/batch' } })
    vi.unstubAllGlobals()
  })
})
