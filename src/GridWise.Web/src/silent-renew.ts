import { UserManager } from 'oidc-client-ts'
import { oidcSettings } from './shared/auth/oidcSettings.ts'

// Loaded in the hidden iframe of a silent sign-in or renewal: hands Keycloak's answer to the app in the parent window.
void new UserManager(oidcSettings()).signinSilentCallback()
