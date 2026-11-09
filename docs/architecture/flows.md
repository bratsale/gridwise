# Main flows

Sequence diagrams of the main flows. Each flow is split so that every diagram stays small.
Participants are the containers from the [architecture overview](README.md); "Publisher" and "Consumer" are background
services inside the Catalog, Recommendation and Batch applications. Table names are in [data-model.md](data-model.md).

## 1. Getting a recommendation

### 1a. Calculating (synchronous)

The request is answered from the recommendation database alone: the service never calls the catalog.
The result and its event are saved in one transaction.

```mermaid
sequenceDiagram
    actor C as Customer
    participant W as web (nginx + React)
    participant R as recommendation
    participant DB as recommendation-db

    C->>W: Enter kWh and tax group
    W->>R: POST /api/recommendation/recommendations
    R->>DB: Latest tax group version valid today
    R->>DB: Per plan, the latest version valid today (DISTINCT ON plan_id)
    DB-->>R: Versions
    Note over R: GridWise.Pricing ranks the active plans by total, with breakdown
    R->>DB: BEGIN, synchronous_commit off
    R->>DB: INSERT recommendations (items jsonb)
    R->>DB: INSERT outbox_messages (RecommendationCalculated), COMMIT
    R-->>W: 201 Created, recommended plan and all plans (breakdown, versions used)
    W-->>C: Recommended plan and comparison
    alt Unknown or retired tax group, or more than 3 decimals
        R-->>W: 400 validation problem
    else The read model has no active plan yet
        R-->>W: 503 no plans available yet
    end
```

### 1b. Publishing the event (asynchronous)

```mermaid
sequenceDiagram
    participant P as Publisher (in recommendation)
    participant DB as recommendation-db
    participant K as Kafka

    loop every second, or at once while rows are found
        P->>DB: SELECT up to 100 rows WHERE published_at IS NULL, oldest first
        DB-->>P: Outbox rows
        P->>K: Produce to recommendation.events, key = recommendation id (acks=all, idempotent)
        P->>DB: SET published_at
    end
```

If Kafka is unavailable the produce call fails, the publisher logs a warning, waits 5 seconds and tries again. The rows stay in
the outbox and the customer is not affected. If the service stops between the produce call and `published_at`, the row is sent
again later (delivery is at least once).

### 1c. Accepting a plan

```mermaid
sequenceDiagram
    actor C as Customer
    participant W as web (nginx + React)
    participant R as recommendation
    participant DB as recommendation-db

    C->>W: Accept a plan
    W->>R: POST /api/recommendation/recommendations/{id}/accept (planId)
    R->>DB: Read the recommendation and its acceptance
    alt Recommendation not found
        R-->>W: 404
    else Plan is not in the recommendation
        R-->>W: 400 validation problem
    else Not accepted yet
        R->>DB: INSERT acceptances and outbox_messages (PlanAccepted) in one transaction
        R-->>W: 201 Created
    else Same plan already accepted
        R-->>W: 200 OK, the same answer again
    else Another plan already accepted
        R-->>W: 409 Conflict
    end
    Note over R,DB: Two parallel accepts collide on the primary key of acceptances. The loser reads the winner and answers 200 or 409.
    W-->>C: Accepted, or the message for the error
```

The `PlanAccepted` event is sent by the same publisher as in 1b.

## 2. Propagating a plan change

### 2a. Changing the plan (synchronous, catalog only)

```mermaid
sequenceDiagram
    actor A as Administrator
    participant W as web (nginx + React)
    participant CAT as catalog
    participant DB as catalog-db

    A->>W: Edit the plan
    W->>CAT: GET /api/catalog/plans/{id}
    CAT-->>W: Plan, ETag "3"
    A->>W: Save
    W->>CAT: POST /api/catalog/plans/{id}/versions, If-Match "3"
    CAT->>DB: Read the latest version of the plan
    alt Plan is retired, or the latest version is not 3
        CAT-->>W: 409 Conflict
    else Validation fails (tiers, effective date)
        CAT-->>W: 400 validation problem
    else Version 3 is the latest
        CAT->>DB: BEGIN
        CAT->>DB: INSERT plan_versions (4), plan_tiers, outbox_messages (PlanVersionCreated)
        CAT->>DB: COMMIT
        CAT-->>W: 201 Created, version 4
    end
    Note over CAT,DB: If two requests pass the check together, the second INSERT fails on the primary key (plan_id, version_no) and also answers 409.
```

### 2b. Reaching the read model (asynchronous)

```mermaid
sequenceDiagram
    participant P as Publisher (in catalog)
    participant CDB as catalog-db
    participant K as Kafka
    participant CO as Consumer (in recommendation)
    participant RDB as recommendation-db

    P->>CDB: SELECT rows WHERE published_at IS NULL, oldest first
    P->>K: Produce to catalog.plan-events, key = plan id
    P->>CDB: SET published_at
    K-->>CO: Message (group "recommendation")
    CO->>RDB: INSERT plan_versions ... ON CONFLICT DO NOTHING
    CO->>K: Commit offset (only after the write)
    Note over RDB: Version 4 is now in the copy. The next POST /recommendations uses it once its effective date is today or earlier.
```

### 2c. When something goes wrong

```mermaid
sequenceDiagram
    participant P as Publisher (in catalog)
    participant CDB as catalog-db
    participant K as Kafka
    participant CO as Consumer (in recommendation)
    participant RDB as recommendation-db

    alt Kafka is down
        P->>K: Produce
        K--xP: Error
        Note over P,CDB: Warning in the log, wait 5 s, try again. The rows wait in the outbox and catalog keeps accepting changes.
        P->>K: Produce again, once Kafka is back
        P->>CDB: SET published_at
        Note over RDB: The read model catches up in the order of the outbox.
    else Poison message (invalid JSON, unreadable data, unsupported version)
        K-->>CO: Message
        CO->>K: Produce to recommendation.dlq (key and value copied, headers with the source and the error)
        CO->>K: Commit offset
        Note over CO: Fix the cause, then run "reprocess-dlq". Other messages are not blocked.
    else recommendation-db is down
        K-->>CO: Message
        CO->>RDB: INSERT
        RDB--xCO: Error
        Note over CO: Wait 5 s and try again, offset not committed, nothing is skipped.
    end
```

A message that fails for another reason is tried 3 times before it goes to the dead-letter topic.
A message of an unknown event type is skipped and its offset is committed.

## 3. Batch job

The administrator uploads a CSV of customers (for example 100,000 rows). The answer comes at once, the work happens in the background,
and the page follows the progress. The design is in [ADR 0011](../decisions/0011-batch-processing.md).

### 3a. Upload and dispatch

The upload checks only what is instant. The job is split into chunks and sent to the workers a few at a time: at most 20 chunks of a job
are Dispatched (not yet Completed) at once.

```mermaid
sequenceDiagram
    actor A as Administrator
    participant W as web (nginx + React)
    participant B as batch
    participant DB as batch-db
    participant D as Dispatcher
    participant P as Outbox publisher
    participant K as Kafka

    A->>W: Choose a CSV file
    W->>B: POST /api/batch/jobs (multipart)
    Note over B: Checks the header, 1 to 100,000 rows,<br/>lines of at most 200 characters, 10 MB.<br/>Row contents are not validated here
    B->>DB: BEGIN, INSERT jobs (Pending) and job_files, COMMIT
    B-->>W: 202 Accepted, Location /jobs/{id}
    loop every second
        D->>DB: Pending jobs?
    end
    D->>DB: BEGIN, lock the job row, still Pending?
    D->>DB: INSERT chunks (500 rows each, all Pending), job Running, COMMIT
    loop while fewer than 20 chunks are Dispatched
        D->>DB: BEGIN, lock the job row, still Running?<br/>chunk Pending to Dispatched,<br/>INSERT outbox_messages (BatchChunkRequested with the raw lines), COMMIT
    end
    P->>DB: Unsent outbox rows
    P->>K: batch.chunks, key = jobId:chunkNo
    P->>DB: Set published_at
```

### 3b. Worker and results

A worker prices a chunk and writes the answer to its own outbox, in one transaction; the recommendation service publishes it and
Batch stores it. When a result frees a window slot, the dispatcher of 3a sends the next chunk.

```mermaid
sequenceDiagram
    participant K as Kafka
    participant WK as recommendation-worker
    participant RDB as recommendation-db
    participant RP as recommendation (outbox publisher)
    participant C as batch (results consumer)
    participant DB as batch-db

    K->>WK: BatchChunkRequested (batch.chunks, group batch-workers)
    WK->>RDB: BEGIN, INSERT processed_chunks ON CONFLICT DO NOTHING
    alt the chunk was already processed
        WK->>K: Commit offset, nothing else written
    else first time
        WK->>RDB: Active plans (once), tax group per code
        Note over WK: Parses and validates each line, prices it with GridWise.Pricing.<br/>An invalid line becomes an error, it never stops the chunk
        WK->>RDB: INSERT outbox_messages (BatchChunkCompleted), COMMIT
        WK->>K: Commit offset
    end
    RP->>RDB: Unsent outbox rows
    RP->>K: batch.results, key = job id
    K->>C: BatchChunkCompleted (group batch)
    C->>DB: BEGIN, lock the job row
    C->>DB: UPDATE chunks Dispatched to Completed
    alt job Cancelled, or no row updated (repeat or unknown chunk)
        C->>K: Commit offset, nothing written
    else applied
        C->>DB: INSERT job_results and job_errors, add to processed_rows and failed_rows
        C->>DB: Every chunk Completed? Job Completed with finished_at. COMMIT
        C->>K: Commit offset
    end
```

The page reads the progress from `GET /api/batch/jobs/{id}/events` (server-sent events): the current state at once and a new event
whenever the database changes (it is checked every 500 ms), a heartbeat comment every 15 seconds, and a final event when the job is
Completed, Cancelled or Failed. Every event carries the full state, so a client that reconnects simply continues from the current state.

### 3c. Cancelling and failures

Cancelling a job. The job row lock is the same one the dispatcher and the results consumer take, so a cancel cannot interleave with them.

```mermaid
sequenceDiagram
    actor A as Administrator
    participant W as web (nginx + React)
    participant B as batch
    participant DB as batch-db
    participant D as Dispatcher
    participant C as Results consumer

    A->>W: Cancel job, confirm
    W->>B: POST /api/batch/jobs/{id}/cancel
    B->>DB: BEGIN, SELECT ... FOR UPDATE (lock the job row)
    alt Pending or Running
        B->>DB: status Cancelled, finished_at, COMMIT
        B-->>W: 200 with the job
    else already Cancelled
        B-->>W: 200 (nothing changes)
    else Completed or Failed
        B-->>W: 409 Conflict
    end
    D->>DB: BEGIN, lock the job row, status is not Running
    Note over D: Stops: nothing more is dispatched,<br/>Pending chunks stay Pending
    C->>DB: Result of a chunk that was already running: lock, job is Cancelled
    Note over C: Writes nothing, commits the offset.<br/>The counters stay as they were
```

An unknown job gets 404. The results and errors written before the cancel stay and can be downloaded as CSV (partial results).

Failures. Nothing is lost and nothing is counted twice, because a chunk's rows exist only if its transaction commits and
`processed_chunks` has one row per chunk. This is checked by [`scripts/resilience-batch-worker.sh`](../../scripts/resilience-batch-worker.sh),
which kills a worker during a 100,000-row job (see [../perf/README.md](../perf/README.md)).

```mermaid
sequenceDiagram
    participant K as Kafka
    participant W1 as worker 1
    participant W2 as worker 2
    participant RDB as recommendation-db

    K->>W1: BatchChunkRequested (chunk 7)
    W1->>RDB: BEGIN, INSERT processed_chunks
    Note over W1: The container is killed before COMMIT.<br/>The transaction is rolled back, the offset is not committed
    Note over K: After the session timeout the partition<br/>is assigned to worker 2
    K->>W2: BatchChunkRequested (chunk 7 again)
    W2->>RDB: BEGIN, INSERT processed_chunks (first time), result outbox row, COMMIT
    W2->>K: Commit offset
    Note over W1,W2: If the worker had died after COMMIT but before the offset,<br/>the insert would find the row, write nothing and only commit the offset

    K->>W2: BatchChunkRequested (chunk 8), no active plans yet
    W2->>RDB: BEGIN, INSERT processed_chunks, load active plans: none
    Note over W2: Throws: ROLLBACK, nothing written, offset not committed.<br/>Waits 5 s and tries the same message again,<br/>until the catalog consumer has stored plans
```

A message that cannot be read, or that still fails after 3 attempts, goes to `recommendation.dlq` (a chunk) or `batch.dlq`
(a result); a database outage is waited out without counting an attempt. `reprocess-dlq` of each service reads its dead-letter topic
again after the cause is fixed (see [../events/README.md](../events/README.md)).
