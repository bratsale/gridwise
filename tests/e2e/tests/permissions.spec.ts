import { expect, test } from '@playwright/test'
import { login } from './auth'

test('a customer sees no Admin tab, and an admin address shows the missing-permission state', async ({ page }) => {
  await login(page, 'customer')

  await expect(page.getByRole('tab', { name: 'Customer' })).toBeVisible()
  await expect(page.getByRole('tab', { name: 'Admin' })).toHaveCount(0)
  await expect(page.getByText('customer (customer)')).toBeVisible() // name and role in the header

  await page.goto('/#admin/plans')
  await expect(page.getByRole('alert')).toHaveText("You don't have permission for this.")
  await expect(page.getByRole('button', { name: 'New plan' })).toHaveCount(0)
})

test('an address that names no page takes a customer to the customer page, not to a missing-permission state', async ({ page }) => {
  await login(page, 'customer')

  await page.goto('/#nonsense')
  await expect(page).toHaveURL(/#customer$/)
  await expect(page.getByLabel('Consumption')).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
})

test('without a login the app asks to log in', async ({ page }) => {
  await page.goto('/#customer')

  await expect(page.getByText('Log in to use GridWise.')).toBeVisible()
  await expect(page.getByRole('tab')).toHaveCount(0)
})
