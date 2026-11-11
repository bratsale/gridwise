import type { ApiResponse } from '../../../shared/api/client.ts'
import { failureMessage } from '../../../shared/api/failure.ts'
import { msg, type Message } from '../../../shared/i18n/message.ts'

// The batch service checks an upload and answers 400 with one of these titles (CsvInspector, JobEndpoints). They are translated
// in full here, so no English detail is needed. An unknown text still shows, as the detail of a general message.
const known: [RegExp, (m: RegExpMatchArray) => Message][] = [
  [/^The first line must be the header (.+)\.$/, (m) => msg('batch.upload.error.header', { header: m[1] })],
  [/^Line (\d+) is longer than (\d+) characters\.$/, (m) => msg('batch.upload.error.lineTooLong', { line: m[1], max: m[2] })],
  [/^The file has more than (\d+) data rows\.$/, (m) => msg('batch.upload.error.tooManyRows', { max: m[1] })],
  [/^The file has a header but no data rows\.$/, () => msg('batch.upload.error.noRows')],
  [/^The file is empty\.$/, () => msg('batch.upload.error.empty')],
]

export function uploadFailure(response: ApiResponse<unknown>): Message {
  if (response.status === 413) return msg('batch.upload.tooLarge')
  const title = response.ok ? undefined : response.body?.title
  if (response.status === 400 && title) {
    for (const [pattern, message] of known) {
      const match = title.match(pattern)
      if (match) return message(match)
    }
  }
  return failureMessage(response, msg('batch.upload.failed'))
}
