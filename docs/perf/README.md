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
