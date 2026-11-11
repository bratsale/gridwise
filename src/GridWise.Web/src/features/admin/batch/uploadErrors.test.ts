import { describe, expect, it } from 'vitest'
import type { ApiResponse } from '../../../shared/api/client.ts'
import { uploadFailure } from './uploadErrors.ts'

const rejected = (status: number, title: string): ApiResponse<unknown> =>
  ({ ok: false, status, headers: new Headers(), body: { title, status } })

describe('uploadFailure', () => {
  it.each([
    ['The first line must be the header customer_id,consumption_kwh,tax_group.', 'batch.upload.error.header', { header: 'customer_id,consumption_kwh,tax_group' }],
    ['Line 7 is longer than 200 characters.', 'batch.upload.error.lineTooLong', { line: '7', max: '200' }],
    ['The file has more than 100000 data rows.', 'batch.upload.error.tooManyRows', { max: '100000' }],
    ['The file has a header but no data rows.', 'batch.upload.error.noRows', undefined],
    ['The file is empty.', 'batch.upload.error.empty', undefined],
  ])('"%s" is translated in full, without a detail line', (title, key, params) => {
    expect(uploadFailure(rejected(400, title))).toEqual({ key, params })
  })

  it('a file over 10 MB has its own message', () => {
    expect(uploadFailure(rejected(413, 'The request body is larger than 10 MB.'))).toEqual(expect.objectContaining({ key: 'batch.upload.tooLarge' }))
  })

  it('an unknown 400 says the file could not be uploaded, with the server text as the detail', () => {
    expect(uploadFailure(rejected(400, 'Something new.'))).toEqual({ key: 'batch.upload.failed', params: undefined, detail: 'Something new.' })
  })

  it('other statuses get the general messages (for example 429 from the gateway)', () => {
    expect(uploadFailure(rejected(429, 'Too many requests'))).toEqual(expect.objectContaining({ key: 'error.tooManyRequests', detail: 'Too many requests' }))
  })
})
