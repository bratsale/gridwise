import { expect, test } from '@playwright/test'
import { apiToken, login } from './auth'

// The admin creates a cheap plan, a customer (in a browser of their own) finds it as the best offer and accepts it,
// and the admin retires it. The plan name is unique per run, so the test does not depend on what is already in the system.

// If the test fails before its own retire step, the plan would stay active and could tie with the next run's plan
// for "best". This safety net retires any plan of this run that is still active (through the API, as admin).
let planName = ''
test.afterEach(async ({ request }) => {
  const headers = { Authorization: `Bearer ${await apiToken(request, 'admin')}` }
  const plans = await (await request.get('/api/catalog/plans', { headers })).json() as { id: string; name: string; status: string }[]
  for (const plan of plans.filter((p) => p.name === planName && p.status === 'Active')) {
    const etag = (await request.get(`/api/catalog/plans/${plan.id}`, { headers })).headers()['etag']
    await request.delete(`/api/catalog/plans/${plan.id}?effectiveFrom=${new Date().toISOString().slice(0, 10)}`, {
      headers: { ...headers, 'If-Match': etag },
    })
  }
})

test('a new plan becomes the best offer, can be accepted, and is retired afterwards', async ({ page, browser }) => {
  planName = `E2E Saver ${Date.now()}`

  // --- Admin: create the plan ---
  await login(page, 'admin')
  await page.getByRole('tab', { name: 'Admin' }).click()
  await page.getByRole('tab', { name: 'Plans' }).click()
  await page.getByRole('button', { name: 'New plan' }).click()

  await page.getByLabel('Name', { exact: true }).fill(planName)
  await page.getByLabel('Discount').fill('20')
  // "Effective from" is already today (UTC).
  // The form starts with one tier, and the last tier has no limit: add the second one first.
  await page.getByRole('button', { name: 'Add tier' }).click()
  await page.getByLabel('Tier 1 up to (kWh)').fill('100')
  await page.getByLabel('Tier 1 price per kWh').fill('0.05')
  await page.getByLabel('Tier 2 price per kWh').fill('0.04')
  await page.getByRole('button', { name: 'Create plan' }).click()

  await expect(page.getByRole('status')).toContainText('Plan created')
  await expect(page.getByRole('row', { name: new RegExp(planName) })).toContainText('Active')

  // --- Customer, in a separate browser context: calculate until the plan shows up (it reaches the recommendation service through Kafka) ---
  const customerContext = await browser.newContext()
  try {
    const customer = await customerContext.newPage()
    await login(customer, 'customer')
    await customer.getByRole('tab', { name: 'Customer' }).click()
    await customer.getByLabel('Consumption').fill('250')
    await customer.getByLabel('Tax group').selectOption('household')

    const planRow = customer.getByRole('row', { name: new RegExp(planName) })
    await expect(async () => {
      await customer.getByRole('button', { name: 'Calculate' }).click()
      await expect(planRow).toBeVisible({ timeout: 2_000 })
    }).toPass({ timeout: 30_000, intervals: [1_000, 2_000, 3_000] })

    // It is the cheapest: first row of the table (after the header) and marked "Best".
    await expect(customer.getByRole('row').nth(1)).toContainText(planName)
    await expect(planRow.getByText('Best', { exact: true })).toBeVisible()

    // Breakdown.
    await planRow.getByRole('button', { name: planName }).click()
    for (const line of ['Energy subtotal', 'Discount', 'Net energy', 'Eco tax', 'VAT base', 'VAT', 'Total']) {
      await expect(customer.getByRole('row', { name: new RegExp(`^${line} -?[\\d.,]+$`) })).toBeVisible()
    }

    // The discount is shown as a deduction on the bill.
    await expect(customer.getByRole('row', { name: /^Discount -[\d.,]+$/ })).toBeVisible()

    // Accept: it is final, so it asks first; then the banner and the row say it was accepted.
    await planRow.getByRole('button', { name: 'Accept' }).click()
    const confirm = customer.getByRole('dialog')
    await expect(confirm).toContainText(`Accept ${planName} for`)
    await confirm.getByRole('button', { name: 'Accept' }).click()
    await expect(customer.getByText(`Accepted: ${planName}`)).toBeVisible()
    await expect(planRow.getByText('Accepted', { exact: true })).toBeVisible()
  } finally {
    await customerContext.close()
  }

  // --- Admin: retire the plan (cleanup) ---
  await page.getByRole('tab', { name: 'Admin' }).click()
  await page.getByRole('tab', { name: 'Plans' }).click()
  await page.getByRole('row', { name: new RegExp(planName) }).getByRole('button', { name: 'Retire' }).click()
  await page.getByRole('button', { name: 'Retire plan' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Retire', exact: true }).click()

  await expect(page.getByRole('status')).toContainText('Plan retired')
  await expect(page.getByRole('row', { name: new RegExp(planName) })).toContainText('Retired')
})
