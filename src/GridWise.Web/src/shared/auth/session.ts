// What request() needs from the login, without knowing how the login works: the current access token, and what to do when the
// API answers 401. auth.ts fills it in at start; tests set their own.
interface Session {
  accessToken: () => string | null
  onUnauthorized: () => void
}

let session: Session = { accessToken: () => null, onUnauthorized: () => {} }

export function configureSession(next: Session) {
  session = next
}

export const currentAccessToken = () => session.accessToken()
export const reportUnauthorized = () => session.onUnauthorized()
