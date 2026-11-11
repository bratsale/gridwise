# Performance: POST /recommendations

## Requirement (NFR)

p95 < 50 ms at 50 recommendations/s with 200 active plans.

## How to run

The compose stack must be running (`docker compose up -d --build --wait`). Then:

```
bash scripts/perf/run.sh                   # SCENARIO=target (default)
SCENARIO=single bash scripts/perf/run.sh
SCENARIO=stress bash scripts/perf/run.sh
```

It seeds 200 "Perf plan NNN" plans through the Catalog API (skipped if they exist), waits until Recommendation has them,
then runs k6 (Docker image `grafana/k6`, host network) against `http://localhost:5102`.
k6 exits with 99 when a threshold is crossed, and so does `run.sh`.

The test calls the recommendation service directly, not through the gateway (`localhost:5100/api/...`), so the gateway's rate limit
(20 calculations per 10 s per user) does not affect the measurement: the NFR is about the service, and 50 requests/s from one
user would be refused by the gateway by design. The service checks the token itself, so `run.sh` gets a customer token from Keycloak
(`scripts/lib/auth.sh`) and passes it to k6 as `TOKEN`; seeding uses an admin token. `TARGET_DURATION=10s` shortens the measured part
of the target scenario, to check quickly that the script works (not for results).

## What is measured

`POST /recommendations` with `consumptionKwh` random 50-1000 and `taxGroup` household or business, with about 200 active plans
(plus the seed and demo plans) in the read model. The service, both databases, Kafka and k6 run on the same machine,
so numbers include that contention. The service also reports its phases in `Server-Timing` (db_read, calc, json, save).

| Scenario | Load | Answers | Thresholds |
|---|---|---|---|
| target | 50 requests/s for 60 s after a 10 s warm-up at 10/s (constant arrival rate, 20-50 VUs) | Is the latency met at the defined load? | p95 < 50 ms, errors < 1%, no dropped iterations (a dropped iteration means the system could not keep up with the rate) |
| single | 1 VU for 30 s | Baseline without contention | p95 < 50 ms, errors < 1% |
| stress | 10 VUs in a closed loop for 60 s after a 10 s warm-up at 2 VUs | Capacity: the maximum throughput. Latency at saturation always grows, so it has no latency threshold | errors < 1% |

## Results

Hardware for all rows: Intel i5-5300U (4 threads), 7.6 GiB, on AC, power profile performance.
The whole stack (service, both databases, Kafka) and k6 run on this one machine.

| Date | Version | Scenario | p50 | p95 | p99 | req/s | Errors | Result |
|---|---|---|---|---|---|---|---|---|
| 2026-11-06 | before (synchronous commit, full breakdown stored) | single (1 VU, 30 s) | 39.0 ms | 69.3 ms | 90.7 ms | 21.6 | 0% | fails |
| 2026-11-06 | before | stress (10 VU, 60 s; called default then) | 89.6 ms | 159 ms | 401 ms | 90 | 0% | fails |
| 2026-11-06 | after (ADR 0007) | single (1 VU, 30 s) | 14.1 ms | 25.2 ms | 32.4 ms | 62.5 | 0% | passes |
| 2026-11-06 | after (ADR 0007) | stress (10 VU, 60 s; called default then) | 54.9 ms | 92.7 ms | 120 ms | 166 | 0% | fails |
| 2026-11-06 | after (ADR 0007) | **target** (50/s, 60 s) | 11.6 ms | 23.5 ms | 36.1 ms | 50 (0 dropped) | 0% | passes |
| 2026-11-06 | after, rerun | single (1 VU, 30 s) | 10.3 ms | 20.5 ms | 27.3 ms | 79.5 | 0% | passes |
| 2026-11-06 | after | stress (10 VU, 60 s, whole run incl. warm-up) | 52.2 ms | 97.4 ms | 128 ms | 158 | 0% | no latency threshold |

Per phase (Server-Timing, avg): single before db-read 4.8, calc 1.4, json 1.1, save 35.3 ms;
single after db-read 4.5, calc 1.2, json 0.2, save 7.0 ms. Default after: db-read 14.9, calc 3.3, json 0.7, save 25.0 ms.

### After identity, gateway and observability (2026-11-11)

The service now validates the access token on every request and exports traces. Measured with `bash scripts/perf/run.sh` (target scenario)
on a fresh stack (`docker compose down -v`, then `up`), on a quiet machine (browser closed), same hardware as above. 200 plans, two runs.

| Date | Version | Scenario | p50 | p95 | p99 | req/s | Errors | Result |
|---|---|---|---|---|---|---|---|---|
| 2026-11-11 | with token validation and trace export, run 1 | **target** (50/s, 60 s) | 11.23 ms | 28.25 ms | 56.3 ms | 50 (0 dropped) | 0% | passes |
| 2026-11-11 | with token validation and trace export, run 2 | **target** (50/s, 60 s) | 11.15 ms | 25.46 ms | 45.78 ms | 50 (0 dropped) | 0% | passes |

Per phase (Server-Timing, avg): db_read ~4.4, calc ~1.1, json ~0.2, save ~5.8 ms, in line with the "after" numbers above.
The p95 is a few milliseconds higher than on 2026-11-06 (23.5 ms), the median is about the same (11.6 ms then), and stays well below the 50 ms target.

**Note on the runs of 2026-11-10.** Runs made while the full test suite and other containers were running at the same time showed a p95 of
47-541 ms and dropped iterations. The machine was swapping then. On a quiet machine the same code meets the target (above), so those runs
measured the machine's memory pressure, not the service. For a meaningful result, run the test on a fresh stack with nothing else heavy running.

# Performance: batch jobs

## Requirement

A CSV of up to 100,000 customers is accepted at once and processed in the background, and the work scales with the number of workers
(the point of the module). This section measures how fast a 100,000-row file is processed with 1 and with 3 workers, and checks that a
killed worker does not lose or duplicate anything.

## How to run

The compose stack must be running (`docker compose up -d --build --wait`) and no batch job may be Pending or Running.

```
bash scripts/perf/batch-throughput.sh      # 1 and 3 workers, two runs each, about 8 minutes
bash scripts/resilience-batch-worker.sh    # kills a worker in the middle of a job, about 3 minutes
```

Both leave the stack with the default of 2 workers (the throughput script also when it fails).
These scripts call the batch service directly on port 5103, not through the gateway: what is measured is batch and the workers, and
the gateway allows only 5 uploads per minute per user. Batch still checks the token, so they get an admin token from Keycloak once at
the start (`scripts/lib/auth.sh`; tokens of the `gridwise-cli` client live 30 minutes, longer than a run).
`ROWS`, `RUNS`, `WORKERS_LIST` and `TIMEOUT` can be set in the environment of the throughput script.

## Method

- **File:** 100,000 rows made by `scripts/perf/make-batch-csv.sh` (always the same file): household and business about half each,
  consumption 0-1000 kWh with up to 3 decimals, about 1% invalid rows (letters, a negative number, an unknown tax group).
  The result is 99,016 processed and 984 failed rows, 200 chunks of 500 rows. 2.6 MB.
- **Measured:** the time from the `202 Accepted` of `POST http://localhost:5100/api/batch/jobs` (through nginx, like the web page) until
  `GET /jobs/{id}` says `Completed`, polled every second. Rows per second is `totalRows` (100,000, valid and invalid) divided by that time.
- **Per N:** `docker compose up -d --scale recommendation-worker=N --wait`, then 20 seconds for the consumer group to rebalance,
  then two runs one after the other.
- **Snapshot:** 12 seconds into every run the script prints `docker stats`, the lag of the consumer groups `batch-workers`
  (batch.chunks, read by the workers) and `batch` (batch.results, read by the Batch service) and the unsent outbox rows of both services.
- Settings are the defaults: chunks of 500 rows, at most 20 chunks of a job in flight, 6 partitions for `batch.chunks`, 2 active plans in the read model.

## Results before the improvements

Hardware: Intel Core i5-5300U (2 cores, 4 threads, 2.3 GHz), 7.6 GiB RAM. The whole stack (nginx, three services, three PostgreSQL
databases, Kafka, the workers) and the test script run on this one machine. 2026-11-09.

| Workers | Run | Seconds | Rows/s |
|---|---|---|---|
| 1 | 1 | 41.2 | 2,424 |
| 1 | 2 | 38.1 | 2,623 |
| 3 | 1 | 47.0 | 2,129 |
| 3 | 2 | 39.8 | 2,510 |

About 2,500 rows per second (5 chunks per second), **with 1 worker and with 3**. Three workers do not make it faster.

## What limited the speed before

The workers were not the limit; the single consumer of results in the Batch service was. What the measurements showed:

| Observation | 1 worker | 3 workers |
|---|---|---|
| Lag of `batch-workers` (chunks waiting for a worker) at 12 s | 0 and 0 | 0 and 1 |
| Lag of `batch` (results waiting for Batch) at 12 s | 17 and 16 | 14 and 14 |
| CPU of a worker at 12 s (`docker stats`, 100% = one thread; one sample, so noisy) | 81% in run 1, 9% in run 2 | run 1: 3%, 80%, 65%; run 2: 0.2%, 8%, 0.6% |
| Time a worker needs per chunk when it has one waiting | median gap between chunks 0.06 s, but 0.58 s at the 90th percentile (it waits for input) | median gap 0.2 s, 90th percentile 1.2-1.5 s |
| Chunks done per second by one worker, over the whole run (a separate run of each, from the worker logs) | 6.5 | 1.4, 2.0 and 1.8 |

- **Workers wait, the consumer queues.** Almost the whole window is waiting in `batch.results` for the Batch consumer (14-17 of the 20
  chunks in flight), while nothing waits for a worker. A worker prices a chunk in about 60 ms and then waits for the next one. Three workers
  each do a third of the work and are idle most of the time.
- **A bigger window does not help.** With `Batch__WindowSize=60` (one extra experiment, configuration only; 1 worker: 37.0 s, 2,705 rows/s;
  3 workers: 41.4 s, 2,418 rows/s) the speed is the same, and the lag of `batch` rises to 46 and 53, again almost the whole window. The window of 20
  is therefore not what limits the speed at this rate; it only decides how many results queue up.
- **One consumer, one partition.** `batch.results` is keyed by the job id, so all results of one job go to one partition and are applied one
  after another by one consumer (one chunk = one transaction that inserts up to 500 `job_results` rows, updates the chunk and the job,
  and commits). At 5 chunks per second this is about 185 ms per chunk, which is where the time goes: the `batch` process runs at 20-80% CPU and
  `batch-db` at about 45%, while `recommendation-db` is nearly idle (1-5%). The 185 ms is the inverse of the measured rate, not a direct
  measurement inside the consumer.
- **Not the publishers.** The delay between writing an outbox row and sending it is 0.6 s on average (Batch) and 0.7 s (Recommendation) in the
  3-worker run with window 60 (200 chunks), which is the 1 s polling interval, not a queue. The unsent outbox rows at the snapshots were 1-4.
- **Not the machine alone.** During a run the host was 22-31% idle plus 7-10% waiting for disk (`vmstat 1` over the whole run: user 42% and system 11% with 1 worker, 55% and 12% with 3). Kafka alone uses about one thread, which is
  a lot for this load and is probably the broker's own overhead on a small machine. This laptop makes every number here lower than a server would give;
  what matters is the shape: more workers, same speed.

**Why 3 workers do not scale linearly.** The workers could: `batch.chunks` has 6 partitions, so up to 6 workers can work at the same time, and the shared
recommendation database is not busy. But the work that all workers feed into is serial, and it is slower than one worker. Adding workers only
helps when the workers are the slowest part (for example with many active plans, where pricing one row takes longer). Here they are not.
Three workers were even slightly slower in the first run (47 s), because of the rebalancing and the extra load on a machine that is already busy.

**What would make it faster:** write the results of a chunk with one COPY instead of Entity Framework inserts (done, see below). Keying `batch.results` by
`{jobId}:{chunkNo}` would not have helped: see "Why keying by chunk would not have helped" below.

## Resilience before: a worker dies in the middle of a job

`scripts/resilience-batch-worker.sh` starts 2 workers, uploads the 100,000-row file, waits until about 20% of the chunks are completed, kills one worker
with `docker kill`, waits for `Completed` (timeout 15 minutes) and then checks, with exit code 1 if anything is wrong:

| Check | Result of the run on 2026-11-09 |
|---|---|
| `processedRows + failedRows = totalRows` | 99,016 + 984 = 100,000 |
| `completedChunks = totalChunks` | 200 = 200 |
| rows in `job_results` = `processedRows`, rows in `job_errors` = `failedRows` (batch-db) | 99,016 and 984 |
| rows in `processed_chunks` for the job = `totalChunks` (recommendation-db) | 200 |
| one `BatchChunkCompleted` outbox row per chunk | 200 |

The worker was killed 29 s after the upload, at 43 of 200 chunks. The job completed after 92 s instead of about 40 s, because the partitions of the killed
worker are assigned to the other one only after the consumer group notices that it is gone (the session timeout, 45 s by default): the longest pause without
progress was 36 s. A second run gave the same result (killed after 10 s at 42 of 200 chunks, completed after 77 s, longest pause 36 s, all checks passed). Nothing was lost and nothing was counted twice: a chunk that the dead worker had not committed was delivered again and processed once,
and a chunk it had committed but not acknowledged was recognized by `processed_chunks` and only acknowledged. The script brings the killed worker back
at the end (`docker compose up -d recommendation-worker`). The sequence is drawn in [../architecture/flows.md](../architecture/flows.md) (3c).

## After: faster result writes and faster failover

Two changes, measured with the same scripts on the same machine (2026-11-09, a few hours later).

1. **Results written with binary COPY.** `BatchResultHandler` used to add the `job_results` and `job_errors` rows with Entity Framework
   (`AddRange`, which sends about 500 INSERT statements per chunk in batches). It now uses Npgsql binary `COPY` (`BeginBinaryImportAsync`) on the same connection
   and in the same transaction: one round trip for all the rows of a chunk. Nothing else changed: the job row lock, the cancelled check, the
   `UPDATE chunks ... WHERE status = 'Dispatched'` that deduplicates, the counters and the completion of the job are still in that one transaction, and a
   failure while writing (for example a line that was stored before) still rolls all of it back. Two new tests check that and the exact storage of quotes,
   line breaks and non-ASCII text.
2. **Failover of a dead worker.** The `batch-workers` consumer now uses `SessionTimeoutMs` 10000 and `HeartbeatIntervalMs` 3000
   (settings `Kafka:SessionTimeoutMs` and `Kafka:HeartbeatIntervalMs`), instead of the 45 s default. The trade-off: a worker that stalls for more than
   10 seconds without heartbeats is taken for dead and its partitions are given to the others (a rebalance). The chunk it was working on is then delivered
   again, which `processed_chunks` makes harmless, but it costs work and a pause. Heartbeats come from a background thread, so a slow chunk alone does not trigger it.

### Handler time

The consumer now logs the time of every `BatchResultHandler` call (Debug level, `Result ... in N ms`). 100,000-row job, 2 workers, three jobs each; the EF command
log was switched off for these runs so that logging does not slow the consumer.

| | Mean per chunk | Median | 95th percentile | Handler busy / job time | Job time (200 chunks) |
|---|---|---|---|---|---|
| Before (EF inserts) | 142-190 ms, **166 ms** over the 3 runs | 128-173 ms | 229-357 ms | 28.3 / 30.9 s, 33.2 / 35.3 s, 38.0 / 40.3 s (92-94% busy) | 30.9, 35.3, 40.3 s |
| After (COPY) | 84-101 ms, **93 ms** over the 3 runs | 68-86 ms | 168-196 ms | 16.9 / 23.4 s, 19.0 / 25.7 s, 20.2 / 24.7 s (72-82% busy) | 23.4, 25.7, 24.7 s |

The handler is 44% faster and the job about 30% shorter. The consumer was busy 92-94% of the job before and 72-82% after, so it is no longer completely
saturated, but it is still the busiest single stage.

### Throughput

Same script and file as before (`bash scripts/perf/batch-throughput.sh`).

| Workers | Run | Seconds before | Rows/s before | Seconds after | Rows/s after |
|---|---|---|---|---|---|
| 1 | 1 | 41.2 | 2,424 | 36.7 | 2,724 |
| 1 | 2 | 38.1 | 2,623 | 32.2 | 3,105 |
| 3 | 1 | 47.0 | 2,129 | 35.6 | 2,812 |
| 3 | 2 | 39.8 | 2,510 | 29.2 | 3,429 |
| 1 | mean | 39.7 | 2,524 | 34.5 | 2,915 (+15%) |
| 3 | mean | 43.4 | 2,320 | 32.4 | 3,120 (+34%) |

The speed-up is real but smaller than the handler's 44%, and the runs vary by about +-15% on this machine. Two things to know when reading the table:
the script's snapshot (`docker stats`, two JVM tools for the consumer lag) takes several seconds of CPU in the middle of every run, in both the before and the after
runs, and the clean 2-worker runs above (no snapshot) took 23-26 s, i.e. about 4,000 rows/s. And one worker is still as fast as three.

With a window of 60 instead of 20 (`Batch__WindowSize=60`, two runs each, configuration only): 1 worker 26.5 and 30.0 s, 3 workers 30.9 and 26.8 s,
i.e. 3,200-3,800 rows/s, about 15% better than with 20. Before the change the window made no difference.

### Where the bottleneck is now

- **The queue in front of the Batch consumer is gone.** The lag of the `batch` group at the 12 s snapshot was 8, 0, 0 and 0 messages with the default window
  (it was 14-17), and 0, 21, 13 and 0 with a window of 60 (it was 46-53). The lag of `batch-workers` was 0-2.
- **The stack as a whole is now close to the machine's limit.** Sampled over whole runs (`vmstat`, `docker stats`): the host was 14-18% idle and 4-9% in I/O wait,
  with user 50-60% and system 16%. Kafka alone used 60-72% of a thread (peaks of 143-161%), the Batch service 25-62% (up to 109%), `batch-db` 26-30%,
  `recommendation-db` 10-12%. With one worker the worker used only 13% on average. So the workers are still not the limit; the three databases, Kafka and
  the Batch service share two physical cores.
- **The serial stages are still the most loaded part.** All of them are in the Batch service and run one after the other: the consumer applies a result in
  about 93 ms (busy 72-82% of the job), the dispatcher reads and parses the 2.6 MB file again at every step, and each outbox publisher sends one message at a time
  with a commit for each. The polling intervals (1 s) add to the round trip of a chunk, which is why a bigger window still helps a little. I did not measure
  inside the consumer which part of the 93 ms is left (the row lock, the two COPYs, the counters, the commit), so I cannot say what the next step is.
- **Why more workers still do not help.** One worker already prices a chunk faster than the chain in front of and behind it can deliver chunks (it used 13% of a
  thread), so extra workers have nothing to take over. They help when pricing is the slow part, for example with many plans per row, or on a machine with more cores.

### Why keying `batch.results` by chunk would not have helped

The idea was to key results by `{jobId}:{chunkNo}` instead of the job id, so that they spread over the 3 partitions of `batch.results` and are applied in parallel.
It would not have helped for three reasons: (1) there is one Batch service with one results consumer, which takes messages one by one whatever the number of partitions,
so spreading them over partitions only changes which partition the one consumer reads from; (2) even with several consumers, every result of a job locks the same
job row (`SELECT ... FOR UPDATE`, which keeps the counters, the cancel and the completion correct), so the results of one job would still be applied one after another;
(3) the time is spent inside the handler (166 ms per chunk before), not in waiting for a partition, which is why making the handler faster did help.

### Resilience after

`bash scripts/resilience-batch-worker.sh`, two runs, both passed all six checks (the same counts as before: 99,016 + 984 = 100,000 rows, 200 of 200 chunks, 200 rows in `processed_chunks`).

| | Worker killed at | Job completed after | Longest pause without progress | Checks |
|---|---|---|---|---|
| Before (session timeout 45 s) | 29 s (43/200) and 10 s (42/200) | 92 s and 77 s | 36 s | passed |
| After (session timeout 10 s) | 7 s (40/200) and 12 s (45/200) | 35 s and 36 s | 3 s and 4 s | passed |

A killed worker now costs seconds instead of the better part of a minute.
