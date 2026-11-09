# GridWise end-to-end tests

Playwright tests that drive the real UI of the running compose stack. Chromium only.

- `tests/main-flow.spec.ts`: admin creates a plan (20 % discount, 100 kWh at 0.05, the rest at 0.04), the customer calculates 250 kWh
  (household) and finds it first and marked "Best", opens its breakdown and accepts it, then the admin retires the plan.
  The plan name is unique per run. The calculation is retried for up to 30 s, because a new plan reaches the recommendation service through Kafka.
  If the test fails before its retire step, an `afterEach` retires the plan through the API so it cannot tie with the next run's plan.
- `tests/batch.spec.ts`: uploads `src/GridWise.Web/public/sample-batch.csv` on the Batch page and waits for Completed with 18 processed and 2 failed rows.

## Prerequisites

- Node.js 22 or newer.
- The stack running (from the repository root): `docker compose up -d --build --wait`. The UI is then at http://localhost:5100.
- A browser, either:
  - the bundled one: `npx playwright install --with-deps chromium` (used in CI), or
  - a system Chromium, for machines where the bundled one does not run (for example Arch Linux): set `E2E_CHROMIUM_PATH=/usr/bin/chromium`.

## Run locally

```bash
cd tests/e2e
npm install
E2E_CHROMIUM_PATH=/usr/bin/chromium npm test   # leave the variable out to use the bundled browser
E2E_CHROMIUM_PATH=/usr/bin/chromium npm run test:headed   # with a visible browser window
```

On failure, screenshots and traces are written to `test-results/`. Open a trace with `npx playwright show-trace <path>/trace.zip`.

## Environment variables

| Variable | Default | Meaning |
|---|---|---|
| `E2E_BASE_URL` | `http://localhost:5100` | Address of the web container (it also proxies `/api/...`). |
| `E2E_CHROMIUM_PATH` | not set | Path to a system Chromium. When set, it is started with `--no-sandbox`. |
