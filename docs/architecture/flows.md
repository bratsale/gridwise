# Main flows

Sequence diagrams of the two main flows. Each flow is split so that every diagram stays small.
Participants are the containers from the [architecture overview](README.md); "Publisher" and "Consumer" are background
services inside the Catalog and Recommendation applications. Table names are in [data-model.md](data-model.md).

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
