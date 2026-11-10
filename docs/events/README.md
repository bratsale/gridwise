# Catalog events

Catalog owns these contracts. Other services get them only as documents (this folder), not as a shared library.
JSON is camelCase. Dates are `yyyy-MM-dd`. Numbers are decimal.
The envelope (event type, version, correlation id) is described in ADR 0005 and in the Envelope section below.

## Envelope (v1)

Every Kafka message value is an envelope around the event data. The Kafka key is the aggregate key of the event (see below).

- Sample: [envelope.v1.json](envelope.v1.json)

| Field | Type | Meaning |
|---|---|---|
| id | guid | Id of the event (the outbox message id). Unique, use it to deduplicate: delivery is at least once |
| type | string | Event type, e.g. `PlanVersionCreated` |
| version | int | Version of the event data, e.g. `1` for `PlanVersionCreated` v1 |
| timestamp | datetime | When the event occurred (UTC, ISO 8601) |
| correlationId | string or null | Correlation id of the request that caused the event |
| data | object | The event data, as described below for the type and version |

## PlanVersionCreated (v1)

- Topic: `catalog.plan-events`
- Key: plan id (`id`)
- Producer: GridWise.Catalog, one event for every new plan version (create, new version, retire)
- Sample: [PlanVersionCreated.v1.json](PlanVersionCreated.v1.json)

| Field | Type | Meaning |
|---|---|---|
| id | guid | Plan id |
| versionNo | int | Version number, starts at 1 |
| name | string | Plan name |
| discount | decimal | Discount rate on the energy sum, 0.05 = 5% |
| status | string | `Active` or `Retired` |
| effectiveFrom | date | First day (UTC) the version applies |
| tiers | array | Progressive tiers in order, empty for a retired version |
| tiers[].threshold | decimal or null | Upper kWh bound of the tier, `null` = no limit (last tier) |
| tiers[].pricePerKwh | decimal | Price per kWh inside this tier |
| changedBy | string or null | Optional. The `sub` (user id) of the admin who made the version; `null` for seed data. Added later in v1 (see below) |

Events written before `changedBy` existed have no such field: [PlanVersionCreated.v1.without-changedBy.json](PlanVersionCreated.v1.without-changedBy.json).

## TaxGroupVersionCreated (v1)

- Topic: `catalog.tax-group-events`
- Key: tax group code (`code`)
- Producer: GridWise.Catalog, one event for every new tax group version (create, new version, retire)
- Sample: [TaxGroupVersionCreated.v1.json](TaxGroupVersionCreated.v1.json)

| Field | Type | Meaning |
|---|---|---|
| code | string | Tax group code, e.g. `household` |
| versionNo | int | Version number, starts at 1 |
| vat | decimal | VAT rate, 0.17 = 17% |
| ecoTax | decimal | Eco tax per kWh |
| status | string | `Active` or `Retired` |
| effectiveFrom | date | First day (UTC) the version applies |
| changedBy | string or null | Optional. The `sub` (user id) of the admin who made the version; `null` for seed data. Added later in v1 (see below) |

Events written before `changedBy` existed have no such field: [TaxGroupVersionCreated.v1.without-changedBy.json](TaxGroupVersionCreated.v1.without-changedBy.json).

## Who made a change: changedBy, and no tokens in events

`changedBy` was added to `PlanVersionCreated` and `TaxGroupVersionCreated` as an optional field, a compatible change, so the version
stays 1. Consumers must not require it: the topics keep every event forever, so events without it are read again on every replay.
The Recommendation consumer's copy of the contract does not even have the field; the contract tests read both samples, with and
without it. It is an audit field: catalog stores the same value in `plan_versions.changed_by` and `tax_group_versions.changed_by`.

Events never carry the user's token, only the user id:

- A token is valid for 5 minutes. An event is processed later (after an outage, from a dead-letter topic) or replayed months later
  from the start of the topic; its token would long have expired and could not be checked any more.
- Consumers do not decide per event whether the user was allowed to do it. That was decided once, by the producer, when the request
  came in; an event is a fact that has already happened.
- Who may read and write a topic is controlled at the broker (in production: authentication and ACLs per service on Kafka), not by
  tokens inside the messages. A token in a message would only be a secret that every reader of the topic could reuse.

`RecommendationCalculated`, `PlanAccepted` and the batch events carry no user identity: no consumer needs it. The services keep
it in their own tables (`recommendations.created_by`, `jobs.created_by`).

## RecommendationCalculated (v1)

Owned by GridWise.Recommendation; it uses the same envelope as above.

- Topic: `recommendation.events`
- Key: recommendation id (`recommendationId`)
- Producer: GridWise.Recommendation, one event for every calculated recommendation
- Sample: [RecommendationCalculated.v1.json](RecommendationCalculated.v1.json)

| Field | Type | Meaning |
|---|---|---|
| recommendationId | guid | Id of the recommendation |
| consumptionKwh | decimal | Consumption the recommendation was calculated for |
| taxGroupCode | string | Tax group used |
| taxGroupVersionNo | int | Version of the tax group used |
| recommendedPlanId | guid or null | Cheapest plan, `null` if there was none |
| recommendedPlanVersionNo | int or null | Version of the cheapest plan that was priced |
| recommendedTotal | decimal or null | Total price of the cheapest plan |
| calculatedAt | datetime | When it was calculated (UTC, ISO 8601) |

## PlanAccepted (v1)

Owned by GridWise.Recommendation; it uses the same envelope as above.

- Topic: `recommendation.events`
- Key: recommendation id (`recommendationId`)
- Producer: GridWise.Recommendation, once when a plan is accepted for a recommendation
- Sample: [PlanAccepted.v1.json](PlanAccepted.v1.json)

| Field | Type | Meaning |
|---|---|---|
| recommendationId | guid | Id of the recommendation |
| planId | guid | Accepted plan |
| planVersionNo | int | Version of the plan that was priced in the recommendation |
| total | decimal | Total price of that plan in the recommendation |
| acceptedAt | datetime | When it was accepted (UTC, ISO 8601) |

## BatchChunkRequested (v1)

Owned by GridWise.Batch; it uses the same envelope as above.

- Topic: `batch.chunks` (6 partitions, retention unlimited like `recommendation.events`)
- Key: `{jobId}:{chunkNo}`, so the chunks of one job are spread over all partitions and several workers can process the same job (the order of chunks is not guaranteed and does not matter)
- Producer: GridWise.Batch, through its outbox: one event for every chunk of 500 CSV data rows (configurable), at most 20 chunks per job are in flight at a time
- Consumer: the batch workers (not built yet)
- Sample: [BatchChunkRequested.v1.json](BatchChunkRequested.v1.json)

| Field | Type | Meaning |
|---|---|---|
| jobId | guid | Id of the batch job |
| chunkNo | int | Number of the chunk inside the job, starts at 1. (jobId, chunkNo) identifies the chunk: use it to deduplicate results |
| rows | array | The data rows of the chunk, in file order |
| rows[].line | int | Line number in the original file (1-based, the header is line 1, blank lines are skipped but counted) |
| rows[].text | string | The raw CSV line, at most 200 characters (Batch rejects longer lines at upload). Batch does not parse or validate it: the worker does, row by row |

## BatchChunkCompleted (v1)

Owned by GridWise.Recommendation; it uses the same envelope as above. Batch reads it.

- Topic: `batch.results` (3 partitions, retention unlimited like `recommendation.events`)
- Key: job id (`jobId`)
- Producer: the batch workers (GridWise.Recommendation started with `worker`), through the outbox of the recommendation database: one event for every `BatchChunkRequested` they process. The recommendation service publishes the outbox.
- Consumer: GridWise.Batch (group `batch`): in one transaction it marks the chunk Completed, stores the results and errors, adds their counts to the job (`processed_rows`, `failed_rows`) and completes the job when its last chunk is Completed. A result for a chunk that is not Dispatched (a repeat, or an unknown job or chunk) or for a Cancelled job is ignored.
- Sample: [BatchChunkCompleted.v1.json](BatchChunkCompleted.v1.json)

The event is written once for a chunk: a repeated `BatchChunkRequested` (same `jobId` and `chunkNo`) is ignored by the worker, so there is at most one `BatchChunkCompleted` per chunk. Use (`jobId`, `chunkNo`) to deduplicate anyway.

| Field | Type | Meaning |
|---|---|---|
| jobId | guid | Id of the batch job |
| chunkNo | int | Number of the chunk inside the job |
| results | array | One entry for every valid row, in file order |
| results[].line | int | Line number in the original file |
| results[].customerId | string | Customer id from the row (trimmed, at most 100 characters) |
| results[].planId | guid | Cheapest plan for the row |
| results[].planVersionNo | int | Version of that plan that was priced (the one valid on the day of processing) |
| results[].planName | string | Name of the plan |
| results[].total | decimal | Total price of the plan for the row's consumption (same as `POST /recommendations`) |
| results[].taxGroupVersionNo | int | Version of the tax group that was used |
| errors | array | One entry for every invalid row, in file order. Invalid rows do not stop the chunk |
| errors[].line | int | Line number in the original file |
| errors[].error | string | Why the row was rejected |

A row is valid when it has exactly 3 comma-separated fields (no quoting, each trimmed) and:
`customer_id` is not empty and at most 100 characters; `consumption_kwh` is a decimal (invariant culture, `.` as separator),
not negative, with at most 3 decimal places; `tax_group` is the code of a tax group that is known and not retired today.
`results` and `errors` together contain every row of the chunk exactly once.

If the worker has no active plans yet, it writes nothing and retries the chunk later.

## Dead-letter topic

`recommendation.dlq` holds messages that GridWise.Recommendation could not process, from the catalog topics (read by the consumer) and from `batch.chunks` (read by the batch workers):

- a poison message (not valid JSON, not an envelope, data that cannot be read)
- an unknown version of a known event type
- a message that still failed after 3 attempts

The key and value are copied unchanged. Headers:

| Header | Meaning |
|---|---|
| source-topic, source-partition, source-offset | Where the message came from. `source-topic` also decides who handles the message when it is reprocessed |
| error | Why it failed (the last error) |
| attempt | Set by the reprocess command: how many times reprocessing has failed so far (absent = never reprocessed) |

Inspect it (Kafka needs a login: the tools run inside the kafka container as `kafka-admin`, with the client properties
file `/tmp/admin-client.properties` that `kafka/start.sh` writes there):

```
docker compose exec kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server kafka:9092 --command-config /tmp/admin-client.properties \
  --topic recommendation.dlq --from-beginning --timeout-ms 10000 \
  --property print.key=true --property print.headers=true
```

Reprocess it, after the cause is fixed (for example the consumer is updated to the new version):

```
docker compose run --rm recommendation reprocess-dlq
```

The command handles the messages that are in the topic when it starts, then exits. Each message goes to the handler of its `source-topic`,
the same code as the live consumer:

| source-topic | Handled by |
|---|---|
| `catalog.plan-events`, `catalog.tax-group-events` (any `catalog.*`) | the catalog event handler (copy of plan and tax group versions) |
| `batch.chunks` | the batch chunk processor: one transaction with the `processed_chunks` check (a chunk already processed is a duplicate), the pricing and the `BatchChunkCompleted` outbox row |
| anything else, or no `source-topic` header | not handled: it fails again (see below), it is never skipped silently |

A chunk is reprocessed with today's plans and tax groups, like a chunk processed late by a worker.
It prints how many were reprocessed and how many failed again. Exit code 0 means nothing failed again, 1 means some did.
A message that fails again is written to the end of `recommendation.dlq` with `attempt` + 1 and the new error, so it is not handled twice in one run.
Run it again later. Handling is idempotent, so running it twice is safe.

Messages are not moved back to the source topic: that would bring them again to every consumer group of that topic,
and out of order with newer events of the same key. The reprocess command applies them to this service only.

## Dead-letter topic of Batch

`batch.dlq` holds messages that GridWise.Batch could not process. Today that is `batch.results` (the consumer is the only reader in Batch):

- a poison message (not valid JSON, not an envelope, data that cannot be read)
- an unknown version of `BatchChunkCompleted`
- a message that still failed after 3 attempts

A database outage is not a failure of the message: the same message is retried every 5 seconds without committing the offset.
An event of an unknown type is skipped. The key and value are copied unchanged, and the headers are the same as for `recommendation.dlq`
(`source-topic`, `source-partition`, `source-offset`, `error`, and `attempt` after a reprocess).

Inspect it:

```
docker compose exec kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server kafka:9092 --command-config /tmp/admin-client.properties \
  --topic batch.dlq --from-beginning --timeout-ms 10000 \
  --property print.key=true --property print.headers=true
```

Reprocess it, after the cause is fixed:

```
docker compose run --rm batch reprocess-dlq
```

It works like the Recommendation command: it handles the messages that are in `batch.dlq` when it starts, with the same handler as the consumer
(a message goes to the handler of its `source-topic`; today only `batch.results`), prints how many were reprocessed and how many failed again,
and exits with 0 or 1. A message that fails again, or has an unknown or missing `source-topic`, is written to the end of `batch.dlq` with `attempt` + 1.
Applying a result is idempotent, so running it twice is safe.

## Monitoring the pipeline

Three numbers tell whether events flow from Catalog to Recommendation:

| Number | Meaning | Healthy |
|---|---|---|
| Outbox backlog (per service) | Rows written to the outbox but not yet sent to Kafka (`published_at IS NULL`), and the age of the oldest one | 0, or a few rows younger than a few seconds |
| Consumer lag (group `recommendation`) | Messages in the source topics that Recommendation has not processed yet | 0, or small and falling |
| DLQ size | Messages in `recommendation.dlq` | 0 |

Read them all with `scripts/pipeline-status.sh` (needs the compose stack running). The raw commands:

```
# outbox backlog (use recommendation-db / recommendation for the other service)
docker compose exec -T catalog-db psql -U catalog -d catalog -c \
  "SELECT count(*), min(occurred_at) FROM outbox_messages WHERE published_at IS NULL"

# consumer lag: the LAG column per partition
docker compose exec kafka /opt/kafka/bin/kafka-consumer-groups.sh \
  --bootstrap-server localhost:9092 --command-config /tmp/admin-client.properties --describe --group recommendation

# DLQ size: end offsets minus start offsets (--time -1 = end, -2 = start), summed over partitions
docker compose exec kafka /opt/kafka/bin/kafka-get-offsets.sh \
  --bootstrap-server localhost:9092 --command-config /tmp/admin-client.properties --topic recommendation.dlq --time -1
```

"Events are late" checklist:

1. Catalog outbox backlog growing: the Catalog publisher or Kafka is the problem. Check the Catalog log for `Outbox publishing failed` and whether Kafka is healthy. Nothing is lost, the rows are sent when Kafka is back.
2. Outbox backlog is 0 but consumer lag is growing: the Recommendation consumer or its database is the problem. Check the Recommendation log and `recommendation-db`.
3. DLQ growing: poison messages. Read the `error` header (see Dead-letter topic), fix the cause, then reprocess.
4. Recommendation outbox backlog growing: same as 1, for `recommendation.events`.

## Evolution rules

### Compatible changes (stay in v1)

- Add an optional field. Example: `PlanVersionCreated` gets `description`. Old consumers ignore it.
- Add a new event type. Example: `PlanRetired` on `catalog.plan-events`. Consumers that do not know it skip it.

### Breaking changes (new version)

- Rename or remove a field, change its type or its meaning.
- Example: `discount` changes from a rate (`0.05`) to a percentage (`5`). The name and type are the same but the meaning is not, so old consumers would calculate wrong prices.

### Rollout of a breaking change

1. The producer publishes `PlanVersionCreated` with `version: 2` in the envelope, on the same topic.
2. For a transition period it publishes both v1 and v2 for every change. If all consumers already support v2, it can publish only v2.
3. Consumers dispatch on `version`.
4. v1 is retired when no consumer reads it any more.

### Consumer rules

- Ignore unknown fields.
- Unknown event type: skip it and mark it as processed.
- Unknown version: send it to the dead-letter topic. Reprocess it after the consumer is updated.

### Why the version is in the envelope, not in the topic name

All versions of a plan go through the same topic with the same key, so they stay in one partition and arrive in the order they happened. With a topic per version (`catalog.plan-events.v2`) the order across versions would be lost.
