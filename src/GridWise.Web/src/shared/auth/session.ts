// What the requests need from the login, without knowing how the login works: the current access token, a way to renew the
// session silently, and what to do when the API still answers 401. auth.ts fills it in at start; tests set their own.
interface Session {
  accessToken: () => string | null
  /** Renews the session without user interaction; true when there is a valid token again. */
  recover: () => Promise<boolean>
  onUnauthorized: () => void
}

let session: Session = { accessToken: () => null, recover: async () => false, onUnauthorized: () => {} }

export function configureSession(next: Session) {
  session = next
}

/**
 * Sends a request with the current access token. On 401 (the token expired, for example while the tab was asleep) the session
 * is renewed silently and the request sent once more, so the page keeps what is on screen. Only when that is not possible, or
 * the API still answers 401, does the login start.
 */
export async function sendWithToken(send: (token: string | null) => Promise<Response>): Promise<Response> {
  let response = await send(session.accessToken())
  if (response.status !== 401) return response
  if (await session.recover()) response = await send(session.accessToken())
  if (response.status === 401) session.onUnauthorized()
  return response
}
