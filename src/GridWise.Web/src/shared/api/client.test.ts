import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { configureSession } from '../auth/session.ts'
import { request } from './client.ts'
import { failureMessage } from './failure.ts'

const fetchMock = vi.fn<typeof fetch>()
const answer = (status: number, body?: object) =>
  new Response(body ? JSON.stringify(body) : null, { status, headers: { 'Content-Type': 'application/json' } })
const sentHeaders = () => new Headers(fetchMock.mock.calls[0][1]?.headers as Record<string, string>)

describe('request() and the login', () => {
  const onUnauthorized = vi.fn()

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock)
    fetchMock.mockReset()
    onUnauthorized.mockReset()
  })
  afterEach(() => vi.unstubAllGlobals())

  it('sends the current access token as a bearer token', async () => {
    configureSession({ accessToken: () => 'token-1', onUnauthorized })
    fetchMock.mockResolvedValue(answer(200, []))

    await request('catalog', '/plans')

    expect(sentHeaders().get('Authorization')).toBe('Bearer token-1')
  })

  it('sends no Authorization header without a token', async () => {
    configureSession({ accessToken: () => null, onUnauthorized })
    fetchMock.mockResolvedValue(answer(200, []))

    await request('catalog', '/plans')

    expect(sentHeaders().has('Authorization')).toBe(false)
  })

  it('starts a login on 401', async () => {
    configureSession({ accessToken: () => 'expired', onUnauthorized })
    fetchMock.mockResolvedValue(answer(401, { title: 'Unauthorized', status: 401 }))

    const response = await request('catalog', '/plans')

    expect(response.status).toBe(401)
    expect(onUnauthorized).toHaveBeenCalledOnce()
  })

  it('does not start a login on 403, and the message says the permission is missing', async () => {
    configureSession({ accessToken: () => 'customer-token', onUnauthorized })
    fetchMock.mockResolvedValue(answer(403, { title: 'Forbidden', status: 403 }))

    const response = await request('catalog', '/plans', { method: 'POST', body: {} })

    expect(onUnauthorized).not.toHaveBeenCalled()
    expect(failureMessage(response)).toEqual(expect.objectContaining({ key: 'auth.forbidden' }))
  })
})
