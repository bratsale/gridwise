import { defineConfig, devices } from '@playwright/test'

// E2E_CHROMIUM_PATH: a system Chromium to use instead of the one Playwright downloads (see README).
const executablePath = process.env.E2E_CHROMIUM_PATH || undefined

export default defineConfig({
  testDir: './tests',
  // The tests share the running stack and the main flow depends on Kafka timing, so they run one after another.
  workers: 1,
  fullyParallel: false,
  timeout: 90_000,
  expect: { timeout: 10_000 },
  reporter: [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5100',
    locale: 'en-US', // the labels the tests look for are the English ones
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], launchOptions: { executablePath, args: executablePath ? ['--no-sandbox'] : [] } },
    },
  ],
})
