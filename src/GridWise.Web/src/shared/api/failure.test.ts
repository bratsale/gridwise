import { describe, expect, it } from 'vitest'
import { msg } from '../i18n/message.ts'
import type { ApiResponse } from './client.ts'
import { failureMessage } from './failure.ts'

const failed = (status: number, title?: string): ApiResponse<unknown> =>
  ({ ok: false, status, headers: new Headers(), body: title ? { title, status } : undefined })

describe('failureMessage', () => {
  it.each([
    [409, 'error.conflict'],
    [413, 'error.tooLarge'],
    [415, 'error.unsupportedType'],
    [429, 'error.tooManyRequests'],
    [502, 'error.unavailable'],
    [503, 'error.unavailable'],
    [504, 'error.unavailable'],
  ])('HTTP %i has its own translated message, the server title only as the detail', (status, key) => {
    expect(failureMessage(failed(status, 'Server says so.'), msg('plans.loadError')))
      .toEqual({ key, params: undefined, detail: 'Server says so.' })
  })

  it.each([400, 500])('HTTP %i uses the message of the place it happened, else a general one', (status) => {
    expect(failureMessage(failed(status, 'An error occurred while processing your request.'), msg('plans.loadError')))
      .toEqual({ key: 'plans.loadError', params: undefined, detail: 'An error occurred while processing your request.' })
    expect(failureMessage(failed(status))).toEqual(
      expect.objectContaining({ key: status === 400 ? 'error.badRequest' : 'error.server', detail: undefined }),
    )
  })

  it('never makes the server title the main message', () => {
    const message = failureMessage(failed(418, 'I am a teapot'))
    expect(message).toEqual({ key: 'error.unexpected', params: { status: 418 }, detail: 'I am a teapot' })
  })

  it('keeps the own texts for no connection, 401 and 403', () => {
    expect(failureMessage(failed(0))).toEqual(expect.objectContaining({ key: 'error.unreachable' }))
    expect(failureMessage(failed(401, 'Unauthorized'))).toEqual(expect.objectContaining({ key: 'auth.required' }))
    expect(failureMessage(failed(403, 'Forbidden'))).toEqual(expect.objectContaining({ key: 'auth.forbidden' }))
  })
})
