import { InMemoryWebStorage, WebStorageStateStore, type UserManagerSettings } from 'oidc-client-ts'

// Keycloak is served by the same nginx under /auth, so the app, the login page and the silent renewal share one origin.
// Tokens are kept in memory only (InMemoryWebStorage): after a reload the session is restored silently from Keycloak's SSO
// session (a hidden iframe with prompt=none). Only the short-lived login state (PKCE verifier, return address) goes to
// sessionStorage, because it has to survive the redirect to Keycloak and back.
export function oidcSettings(origin = window.location.origin): UserManagerSettings {
  return {
    authority: `${origin}/auth/realms/gridwise`,
    client_id: 'gridwise-web',
    redirect_uri: `${origin}/`,
    silent_redirect_uri: `${origin}/silent-renew.html`,
    post_logout_redirect_uri: `${origin}/`,
    response_type: 'code', // Authorization Code; oidc-client-ts always adds PKCE (S256)
    scope: 'openid profile',
    userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
    stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
    automaticSilentRenew: true,
    accessTokenExpiringNotificationTimeInSeconds: 60, // renew a minute before the 5-minute token expires
    // Watch Keycloak's session (its check_session_iframe, served under /auth like the rest of Keycloak, so same origin): the tabs of
    // one browser share the session cookie, so a logout in one tab, or a login there as someone else, signs the others out too.
    monitorSession: true,
    loadUserInfo: false,
  }
}
