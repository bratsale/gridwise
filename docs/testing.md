# Test strategy

This page describes how GridWise is tested today: which kinds of tests exist, what each one is responsible for, which tests
cover the risky parts of the system, what is deliberately left out, and how CI uses the tests. Everything here refers to tests
and scripts that are in the repository.

Counts and durations were measured on the development laptop (Intel i5-5300U, 4 threads, 7.6 GiB) on 2026-11-11, after removing
duplicate tests and merging near-identical ones into parameterized tests. Counts are test cases, as the runners report them. Some
of these runs overlapped with other work, so treat the durations as upper bounds. A CI runner is usually faster.

## The pyramid

| Layer | Where | Count | How to run | Duration | In CI |
|---|---|---|---|---|---|
| Unit (.NET) | `tests/GridWise.Pricing.Tests`, `*.Catalog.Tests`, `*.Recommendation.Tests`, `*.Batch.Tests` | 66 | `dotnet test GridWise.slnx` | a few seconds | yes, job `backend` |
| Unit (web) | `src/GridWise.Web/src/**/*.test.ts(x)` (Vitest, jsdom) | 127 | `npm test` in `src/GridWise.Web` | about 7 s | yes, job `web` |
| Integration | `tests/GridWise.{Catalog,Recommendation,Batch,Gateway}.IntegrationTests` | 232 | `dotnet test GridWise.slnx` (needs Docker) | 16 s to 2 min 48 s per project, about 6.5 min in total | yes, job `backend` |
| End-to-end | `tests/e2e` (Playwright, Chromium) | 5 | `npm test` in `tests/e2e`, against a running stack | about 30 s plus starting the stack | yes, job `e2e` (on `main` only) |
| Load | `scripts/perf/run.sh` (k6), `scripts/perf/batch-throughput.sh` | 3 scenarios, 1 throughput run | see [perf/README.md](perf/README.md) | 70 s per k6 scenario, about 8 min for the batch run | no |
| Resilience and operations | `scripts/*.sh` | 6 scripts | `bash scripts/<name>.sh`, against a running stack | 2 s to about 6 min | only `smoke-test.sh` |

**Unit tests** cover logic that needs nothing but memory:
- the pricing rules in `GridWise.Pricing` (progressive tiers, discount before tax, eco tax and VAT, `Money.Round` at the midpoint);
- the plan validation rules (`CatalogRulesTests`);
- the retry and dead-letter decision of the consumers (`FailureDecisionTests`);
- the dead-letter and outbox metrics;
- the event contracts (see below).

These rules have many cases and must be exact to the cent, so they are cheapest and clearest to test here, close to the code.
In the web app, Vitest covers form validation,
routing, roles, the API client, the i18n dictionaries (that both languages have the same keys) and the hooks with tricky state:
optimistic lists and the progress stream of a batch job.

**Integration tests** are where most of the confidence comes from. Each service runs in-process through `WebApplicationFactory`
against a real PostgreSQL 17 and, where events are involved, a real Kafka 4.4 (`apache/kafka:4.4.0`). Both are started by
Testcontainers. The tests sign their own tokens with a key the service is told to trust (`TestAuth`), so Keycloak is not needed.
`FakeTimeProvider` controls the clock where time matters (retention, effective dates).

The risky behaviour of this system lives at the boundary between code and infrastructure: unique constraints, row locks,
transactions, offsets and commits. Mocks would hide exactly that, so these tests run against the real thing.

The gateway tests are different: they start the gateway on real Kestrel in front of three stub services (`GatewayFixture`,
`StubUpstream`), because what they check is routing, authentication and rate limiting, not the services behind it.

**End-to-end tests** drive the real UI of the complete compose stack, including the real Keycloak login page. There are five tests:
- `main-flow.spec.ts`: an admin creates a plan, a customer in a separate browser finds it as the best offer and accepts it, and the
  admin retires it;
- `batch.spec.ts`: the sample CSV completes with 18 processed and 2 failed rows;
- `permissions.spec.ts`: a customer gets no Admin tab and sees the missing-permission page, an address that names no page takes a
  customer to the customer page, and without a login the app asks to log in.

They are few on purpose. They prove that the parts are wired together (nginx, gateway, Keycloak, Kafka between catalog and
recommendation, the batch pipeline), not the rules, which the layers below already cover. They find elements by role and label,
so a restyle does not break them. See [../tests/e2e/README.md](../tests/e2e/README.md).

**Load tests** check the non-functional requirement (p95 < 50 ms at 50 recommendations/s with 200 plans) and the batch throughput.
**Resilience and operations scripts** check behaviour that needs the whole stack and something going wrong: killing processes,
stopping Kafka, resetting offsets. Both kinds are described in the next sections.

## What is tested where: the risky parts

- **Idempotent consumers and duplicates.**
  - Recommendation's catalog consumer: `CatalogEventHandlerTests` (applied, then reported as duplicate), and in
    `CatalogConsumerTests` against Kafka: `Same_message_twice_gives_one_row` and `Version_2_before_version_1_gives_both_rows`.
  - Batch results: `The_same_result_twice_changes_nothing`.
  - Batch chunks in the worker: `Same_chunk_twice_gives_one_processed_row_and_one_outbox_row`.
- **Outbox.**
  - Each service has an `OutboxPublisherTests` against Kafka (published once; in order for recommendation events). `TracingTests`
    check that the trace context is stored with the outbox row and continued by the consumer. `RetentionCleanupTests` check that
    only published rows older than 7 days are deleted.
  - Losing Kafka for a while is shown by `scripts/demo-kafka-outage.sh`: stop Kafka, change data, start Kafka, and nothing is lost
    or sent twice.
- **Optimistic concurrency (If-Match, and unique violations as 23505).**
  - A stale or missing `If-Match` is tested in `PlanVersionTests` and `TaxGroupTests`.
  - Real races are tested with parallel requests: `Parallel_new_versions_with_same_if_match_one_wins` (unique key on `plan_versions`),
    `Parallel_creates_with_same_code_one_wins` and `Parallel_accepts_of_the_same_plan_create_one_acceptance`. Each relies on
    PostgreSQL's unique violation (SQLSTATE 23505) being turned into 409 or into the existing result.
- **Idempotency-Key** on `POST /plans`: `PostPlanTests`:
  - `Same_key_and_body_twice_returns_same_plan`;
  - `Same_key_with_different_body_is_rejected`;
  - `Missing_idempotency_key_is_rejected`;
  - `Parallel_requests_with_same_key_create_one_plan`.

  `RetentionCleanupTests` checks that keys expire after 24 hours.
- **Batch chunk dedup and cancel.**
  - Dedup: `Running_the_step_again_creates_no_duplicates` in the dispatcher, and
    `A_chunk_that_was_never_dispatched_is_not_completed_by_a_result`.
  - Cancel: `CancelTests` covers pending, running (later results are ignored and the counters freeze), twice, completed, failed
    and unknown jobs, and `The_dispatch_transaction_checks_the_status_again_under_the_lock`.
  - A worker killed in the middle of a job: `scripts/resilience-batch-worker.sh` checks that every row is counted exactly once.
- **Dead letters and reprocessing.** `FailureDecisionTests` (unit), `ResultsConsumerTests` and `CatalogConsumerTests` (poison
  messages and unknown versions go to the DLQ and are committed; a database outage is retried without commit), `DlqRoutingTests`
  and both `DlqReprocessorTests` (a fixed message is processed, a broken one goes back to the end of the topic, an unknown source
  topic is never skipped).
- **Auth.** Every service has `AuthorizationTests`:
  - no token is 401;
  - a customer changing the catalog or using batch is 403;
  - the acting user is stored with the data (`changedBy`, the uploading admin);
  - ownership: `A_customer_reads_and_accepts_only_their_own_another_customer_gets_404`.

  The gateway's `AuthenticationTests` cover invalid and expired tokens, the query token that only the SSE route accepts, and that
  the token never appears in logs or spans. `permissions.spec.ts` checks the same rules through the UI.
- **Rate limits.** `RateLimitingTests` in the gateway: the 21st recommendation in a window is 429 with `Retry-After`, users have
  separate limits, the 6th upload in a minute is 429, health is never limited, and anonymous clients are limited by address behind
  the trusted proxy.
- **Kafka authentication and ACLs.** Not in the .NET tests: the Testcontainers Kafka has no SASL. `scripts/kafka-acl-check.sh`
  checks, against the running stack, that every user can do what it needs and nothing more. It runs one Kafka tool per check in a
  throwaway container and does not change anything.
- **Rebuilding the catalog copy from topics.** `scripts/replay-recommendation-db.sh` empties Recommendation's copy, resets the
  consumer group to the earliest offset and checks that the copy matches catalog-db again. On 2026-11-11 the rebuild took 10 s
  and the whole script 21 s.

## Requirement or risk to test

| Requirement / risk | Test or script |
|---|---|
| Prices exact to the cent, progressive tiers, discount before tax | `PriceCalculatorTests`, `EnergyCostTests`, `MoneyTests` |
| Cheapest plan recommended; only the version valid on the date is used | `RecommendationTests.Cheapest_plan_is_recommended_with_full_breakdown`, `Future_version_is_not_used`, `Plan_whose_valid_version_is_retired_is_not_offered` |
| A stored result does not change when plans change later | `Get_returns_the_original_result_after_a_newer_plan_version_arrives`, `Newer_plan_version_does_not_change_the_accepted_version` |
| Batch prices exactly like the API | `BatchChunkProcessorTests.Totals_equal_the_recommendation_api_for_the_same_input` |
| CSV limits (100,000 rows, 10 MB, line length, header) | `JobUploadTests`, `RoutingTests.An_upload_over_11_MB_is_refused_by_the_gateway` |
| CSV downloads cannot inject formulas | `JobDownloadTests.Text_cells_that_look_like_formulas_get_a_leading_quote_but_numbers_do_not` |
| Producer and consumer agree on event formats | `EventContractTests` (one per service), `CatalogEventCompatibilityTests` |
| Duplicate or reordered events | `Same_message_twice_gives_one_row`, `Version_2_before_version_1_gives_both_rows`, `The_same_result_twice_changes_nothing` |
| Concurrent writes | `Parallel_new_versions_with_same_if_match_one_wins`, `Parallel_creates_with_same_code_one_wins`, `Parallel_accepts_of_the_same_plan_create_one_acceptance`, `Parallel_requests_with_same_key_create_one_plan` |
| A worker dies during a job | `scripts/resilience-batch-worker.sh` |
| Kafka unavailable for a while | `scripts/demo-kafka-outage.sh`, `ResultsConsumerTests.Database_outage_is_retried_without_commit_and_not_dead_lettered` (database side) |
| Topics are a complete source of truth | `scripts/replay-recommendation-db.sh` |
| Least privilege on Kafka | `scripts/kafka-acl-check.sh` |
| p95 < 50 ms at 50/s with 200 plans | `scripts/perf/run.sh` (target scenario), results in [perf/README.md](perf/README.md) |
| A deployment works end to end | `scripts/smoke-test.sh` (in every deployment), Playwright suite |

## Deliberately not tested

- **No consumer-driven contract tests (Pact or similar).** Events are versioned JSON with golden files in `docs/events/`. The
  producer's tests check that it serializes exactly to them; each consumer's tests check that it reads them, including older
  variants (`*.without-changedBy.json`) and unknown fields. For three services in one repository, a shared file that both sides
  must pass gives the same protection without a broker. The web client's types are generated from the services' OpenAPI documents
  (`npm run gen:api`), which is also not checked in CI.
- **No chaos or resilience tests in CI.** Killing workers, stopping Kafka and resetting offsets take minutes, change the state of
  the stack and depend on timing. They are run by hand before changes in that area, and their results are recorded in
  [perf/README.md](perf/README.md).
- **No performance tests in CI.** A shared runner gives noisy numbers, and on the laptop the same code measured a p95 of 25 ms on
  a quiet machine and up to 541 ms while other work ran. A threshold in CI would fail at random.
- **No Kafka ACL test in CI.** The integration tests use Kafka without SASL to stay fast and simple. The ACLs are configuration
  in `compose.yaml`, checked by `kafka-acl-check.sh` against the real broker.
- **No visual regression tests, and no browsers other than Chromium.** The UI is small and its behaviour is covered by Vitest and
  Playwright through roles and labels.
- **Keycloak itself** (login page, token issuing) is trusted. It appears only in the e2e tests.

## How CI uses the tests

`.github/workflows/ci.yml` runs on every push and pull request:

1. **`backend`** builds the solution and runs `dotnet test GridWise.slnx`: all unit and integration tests, with Testcontainers
   on the runner's Docker. The TRX results are uploaded as an artifact.
2. **`web`** runs lint, the production build and Vitest.
3. **`images`** (only after both pass, and only on a push to `main`) builds the five images and pushes them to ghcr.io, tagged
   with the commit SHA.
4. **`e2e`** starts the compose stack from exactly those images and runs the Playwright suite. On failure it uploads the
   Playwright report and prints the container logs, and it always removes the stack and its volumes.
5. **`deploy-dev`** (only after `e2e` passes) runs the simulated deployment, which ends with `scripts/smoke-test.sh`.

So a failing unit or integration test stops a branch before images are built. A failing e2e or smoke test stops the automatic
deployment to dev. Whether a failing check blocks merging a pull request depends on the branch protection rules in GitHub, which
are not part of the repository.

Deployments to qa and prod (`.github/workflows/promote.yml`) are manual and do not run tests again. Instead they refuse a SHA unless
the `CI/CD` run of the push to `main` for that exact commit has finished with conclusion `success`, which means every job passed,
including `e2e` and `deploy-dev`. They fail with a clear message, and a link to the run, when there is no such run, when it is
still running, or when it failed or was cancelled. Together with the check that the images exist, this runs before anything is
deployed; the deployment ends with the smoke test. So no commit reaches qa or prod without having passed the whole suite, the end-to-end tests included.
