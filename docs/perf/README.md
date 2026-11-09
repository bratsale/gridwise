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

## Results

Hardware: Intel Core i5-5300U (2 cores, 4 threads, 2.3 GHz), 7.6 GiB RAM. The whole stack (nginx, three services, three PostgreSQL
databases, Kafka, the workers) and the test script run on this one machine. 2026-11-09.

| Workers | Run | Seconds | Rows/s |
|---|---|---|---|
| 1 | 1 | 41.2 | 2,424 |
| 1 | 2 | 38.1 | 2,623 |
| 3 | 1 | 47.0 | 2,129 |
| 3 | 2 | 39.8 | 2,510 |

About 2,500 rows per second (5 chunks per second), **with 1 worker and with 3**. Three workers do not make it faster.

## What limits the speed

The workers are not the limit; the single consumer of results in the Batch service is. What the measurements show:

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

**What would make it faster** (not done, this step changes no behaviour): write the results of a chunk with one multi-row statement instead of
Entity Framework inserts, or key `batch.results` by `{jobId}:{chunkNo}` so that several Batch consumers can apply results in parallel (the job row lock
would then be the next limit, and the counters could be summed at the end instead).

## Resilience: a worker dies in the middle of a job

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
