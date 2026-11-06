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

## Dead-letter topic

`recommendation.dlq` holds messages that GridWise.Recommendation could not process:

- a poison message (not valid JSON, not an envelope, data that cannot be read)
- an unknown version of a known event type
- a message that still failed after 3 attempts

The key and value are copied unchanged. Headers:

| Header | Meaning |
|---|---|
| source-topic, source-partition, source-offset | Where the message came from |
| error | Why it failed (the last error) |
| attempt | Set by the reprocess command: how many times reprocessing has failed so far (absent = never reprocessed) |

Inspect it:

```
docker compose exec kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server kafka:9092 \
  --topic recommendation.dlq --from-beginning --timeout-ms 10000 \
  --property print.key=true --property print.headers=true
```

Reprocess it, after the cause is fixed (for example the consumer is updated to the new version):

```
docker compose run --rm recommendation reprocess-dlq
```

The command handles the messages that are in the topic when it starts, with the same handler as the consumer, then exits.
It prints how many were reprocessed and how many failed again. Exit code 0 means nothing failed again, 1 means some did.
A message that fails again is written to the end of `recommendation.dlq` with `attempt` + 1 and the new error, so it is not handled twice in one run.
Run it again later. Handling is idempotent, so running it twice is safe.

Messages are not moved back to the source topic: that would bring them again to every consumer group of that topic,
and out of order with newer events of the same key. The reprocess command applies them to this service only.

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
