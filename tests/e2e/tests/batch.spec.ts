import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test } from '@playwright/test'

const sampleFile = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../src/GridWise.Web/public/sample-batch.csv')

// The sample file has 20 rows: 18 are valid, 2 are not (a consumption of "abc" and an unknown tax group).
test('the sample file is processed by a batch job: 18 rows processed, 2 failed', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('tab', { name: 'Admin' }).click()
  await page.getByRole('tab', { name: 'Batch' }).click()

  // The file input is hidden behind the "Choose file" button; setting its file is what choosing one does.
  await page.getByLabel('Choose file').setInputFiles(sampleFile)

  // The detail of the new job opens by itself and follows the progress. Its header (heading + status) is the only
  // place with the file name as a heading; older jobs of the same file are only links in the list.
  const header = page.getByRole('heading', { name: 'sample-batch.csv' }).locator('..')
  await expect(header.getByText('Completed', { exact: true })).toBeVisible({ timeout: 60_000 })
  await expect(page.getByText('18 rows processed, 2 failed, 20 in the file')).toBeVisible()
})
