# GridWise end-to-end tests

Playwright tests that drive the real UI of the running compose stack, logging in through the real Keycloak login page. Chromium only.

- `tests/main-flow.spec.ts`: the **admin** creates a plan (20 % discount, 100 kWh at 0.05, the rest at 0.04); a **customer**, in a
  separate browser context, calculates 250 kWh (household), finds it first and marked "Best", opens its breakdown and accepts it;
  then the admin retires the plan. The plan name is unique per run. The calculation is retried for up to 30 s, because a new plan
  reaches the recommendation service through Kafka. If the test fails before its retire step, an `afterEach` retires the plan
  through the API (with an admin token from the `gridwise-cli` client).
- `tests/batch.spec.ts`: as **admin**, uploads `src/GridWise.Web/public/sample-batch.csv` on the Batch page and waits for Completed
  with 18 processed and 2 failed rows.
- `tests/permissions.spec.ts`: a **customer** sees no Admin tab and `#admin/plans` shows "You don't have permission for this.";
  without a login the app asks to log in.
- `tests/auth.ts`: logging in through Keycloak, and an API token for the tests' own cleanup calls.

## Test users

The dev users of the `gridwise` realm (`keycloak/realm-gridwise.json`):

| User | Password | Role | Used for |
|---|---|---|---|
| `admin` | `admin_dev_pw` | admin | creating and retiring the plan, batch |
| `customer` | `customer_dev_pw` | customer | calculating and accepting, the permission test |

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
| `E2E_ADMIN_USER`, `E2E_ADMIN_PASSWORD` | `admin`, `admin_dev_pw` | The admin user. |
| `E2E_CUSTOMER_USER`, `E2E_CUSTOMER_PASSWORD` | `customer`, `customer_dev_pw` | The customer user. |
