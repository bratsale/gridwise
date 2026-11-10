import { expect, type APIRequestContext, type Page } from '@playwright/test'

// The dev users of the gridwise realm (keycloak/realm-gridwise.json). Overridable for other environments.
export const users = {
  admin: { username: process.env.E2E_ADMIN_USER ?? 'admin', password: process.env.E2E_ADMIN_PASSWORD ?? 'admin_dev_pw' },
  customer: { username: process.env.E2E_CUSTOMER_USER ?? 'customer', password: process.env.E2E_CUSTOMER_PASSWORD ?? 'customer_dev_pw' },
}
export type Role = keyof typeof users

// Logs in through the real Keycloak page: the app's login button, Keycloak's form, and back to the app.
export async function login(page: Page, role: Role) {
  await page.goto('/')
  await page.getByRole('button', { name: 'Log in' }).first().click()
  await page.getByLabel('Username or email').fill(users[role].username)
  await page.getByLabel('Password', { exact: true }).fill(users[role].password)
  await page.getByRole('button', { name: 'Sign In' }).click()
  await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible()
}

// An access token for API calls made by the test itself (cleanup), through the gridwise-cli client.
export async function apiToken(request: APIRequestContext, role: Role): Promise<string> {
  const response = await request.post('/auth/realms/gridwise/protocol/openid-connect/token', {
    form: { grant_type: 'password', client_id: 'gridwise-cli', username: users[role].username, password: users[role].password },
  })
  expect(response.ok(), `token for ${role}: HTTP ${response.status()}`).toBe(true)
  return (await response.json()).access_token as string
}
