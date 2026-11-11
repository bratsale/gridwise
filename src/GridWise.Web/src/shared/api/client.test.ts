import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { configureSession } from '../auth/session.ts'
import { request } from './client.ts'
import { failureMessage } from './failure.ts'

const fetchMock = vi.fn<typeof fetch>()
const answer = (status: number, body?: object) =>
  new Response(body ? JSON.stringify(body) : null, { status, headers: { 'Content-Type': 'application/json' } })
const sentHeaders = (call = 0) => new Headers(fetchMock.mock.calls[call][1]?.headers as Record<string, string>)
const noRecovery = async () => false

describe('request() and the login', () => {
  const onUnauthorized = vi.fn()

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock)
    fetchMock.mockReset()
    onUnauthorized.mockReset()
  })
  afterEach(() => vi.unstubAllGlobals())

  it('sends the current access token as a bearer token', async () => {
    configureSession({ accessToken: () => 'token-1', recover: noRecovery, onUnauthorized })
    fetchMock.mockResolvedValue(answer(200, []))

    await request('catalog', '/plans')

    expect(sentHeaders().get('Authorization')).toBe('Bearer token-1')
  })

  it('sends no Authorization header without a token', async () => {
    configureSession({ accessToken: () => null, recover: noRecovery, onUnauthorized })
    fetchMock.mockResolvedValue(answer(200, []))

    await request('catalog', '/plans')

    expect(sentHeaders().has('Authorization')).toBe(false)
  })

  it('on 401 renews the session silently and sends the request once more with the new token, without a login', async () => {
    let token = 'expired'
    const recover = vi.fn(async () => {
      token = 'renewed'
      return true
    })
    configureSession({ accessToken: () => token, recover, onUnauthorized })
    fetchMock.mockResolvedValueOnce(answer(401)).mockResolvedValueOnce(answer(200, [{ id: 1 }]))

    const response = await request('catalog', '/plans')

    expect(response).toEqual(expect.objectContaining({ ok: true, status: 200, body: [{ id: 1 }] }))
    expect(recover).toHaveBeenCalledOnce()
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(sentHeaders(1).get('Authorization')).toBe('Bearer renewed')
    expect(onUnauthorized).not.toHaveBeenCalled()
  })

  it('tries once only: a second 401 after a renewal starts a login', async () => {
    configureSession({ accessToken: () => 'token', recover: async () => true, onUnauthorized })
    fetchMock.mockResolvedValue(answer(401))

    const response = await request('catalog', '/plans')

    expect(response.status).toBe(401)
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(onUnauthorized).toHaveBeenCalledOnce()
  })

  it('starts a login on 401 when the session cannot be renewed', async () => {
    configureSession({ accessToken: () => 'expired', recover: noRecovery, onUnauthorized })
    fetchMock.mockResolvedValue(answer(401, { title: 'Unauthorized', status: 401 }))

    const response = await request('catalog', '/plans')

    expect(response.status).toBe(401)
    expect(fetchMock).toHaveBeenCalledOnce() // not sent again
    expect(onUnauthorized).toHaveBeenCalledOnce()
  })

  it('does not start a login on 403, and the message says the permission is missing', async () => {
    configureSession({ accessToken: () => 'customer-token', recover: noRecovery, onUnauthorized })
    fetchMock.mockResolvedValue(answer(403, { title: 'Forbidden', status: 403 }))

    const response = await request('catalog', '/plans', { method: 'POST', body: {} })

    expect(onUnauthorized).not.toHaveBeenCalled()
    expect(failureMessage(response)).toEqual(expect.objectContaining({ key: 'auth.forbidden' }))
  })
})
