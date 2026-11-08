# Data model

Each service has its own PostgreSQL database and nobody else reads or writes it
([ADR 0008](../decisions/0008-service-boundaries.md)). Tables and columns come from the EF Core configurations and the migrations
(`src/GridWise.*/Data/Configurations`, `src/GridWise.*/Migrations`); column names are snake_case.
`numeric(p,s)` precisions are in the column comments. Back to [architecture](README.md).

## Catalog database (`catalog`)

The source of truth for plans and tax groups. Identity and versions are separate: `plans` and `tax_groups` hold only the key,
everything that changes is in an immutable version row.

```mermaid
erDiagram
    plans ||--o{ plan_versions : "has versions"
    plan_versions ||--o{ plan_tiers : "has tiers"
    tax_groups ||--o{ tax_group_versions : "has versions"

    plans {
        uuid id PK
        timestamptz created_at
    }
    plan_versions {
        uuid plan_id PK, FK
        int version_no PK
        varchar_100 name
        numeric discount "numeric(5,4), check 0..1"
        varchar_20 status "Active or Retired"
        date effective_from
        timestamptz created_at
    }
    plan_tiers {
        uuid plan_id PK, FK
        int version_no PK, FK
        int tier_no PK
        numeric threshold "numeric(12,3), null for the last tier"
        numeric price_per_kwh "numeric(12,4), check >= 0"
    }
    tax_groups {
        varchar_50 code PK
        timestamptz created_at
    }
    tax_group_versions {
        varchar_50 code PK, FK
        int version_no PK
        numeric vat "numeric(5,4), check 0..1"
        numeric eco_tax "numeric(12,4), check >= 0"
        varchar_20 status "Active or Retired"
        date effective_from
        timestamptz created_at
    }
    idempotency_keys {
        varchar_100 key PK
        varchar_100 endpoint PK
        varchar_64 request_hash
        int response_status
        jsonb response_body
        timestamptz created_at
    }
    outbox_messages {
        uuid id PK
        text aggregate_type "plan or tax-group"
        text aggregate_id
        text event_type
        int event_version
        jsonb payload
        text correlation_id "nullable"
        timestamptz occurred_at
        timestamptz published_at "nullable"
    }
```

| Table | Purpose and notable constraints |
|---|---|
| `plans` | Identity of a plan (a uuid). Nothing else about the plan lives here. |
| `plan_versions` | One row per version of a plan: name, discount, status and the date it takes effect. The primary key `(plan_id, version_no)` is also the optimistic concurrency guard: two editors that both create version N+1 collide on it and the second gets 409. Versions are immutable (the code only inserts them, and the foreign keys use `ON DELETE RESTRICT`). A retirement is a new version with status `Retired` and no tiers. `status` is stored as text (`Active` / `Retired`). Check constraint `ck_plan_versions_discount`: discount between 0 and 1 (a fraction, 0.05 = 5 %). |
| `plan_tiers` | The pricing tiers of one plan version. Primary key `(plan_id, version_no, tier_no)`; the foreign key to `plan_versions` uses `ON DELETE RESTRICT`. `threshold` is the upper limit in kWh and is `NULL` for the last tier; the rule "exactly one tier without a limit, and it is the last" is checked by the domain rules, not by the database. Check constraint `ck_plan_tiers_price_per_kwh`: price >= 0. |
| `tax_groups` | Identity of a tax group (`code`, such as `household`). |
| `tax_group_versions` | Versions of a tax group: VAT rate and eco tax per kWh. Same rules as plan versions: primary key `(code, version_no)` is the concurrency guard, rows are immutable, the foreign key restricts deletes. Check constraints `ck_tax_group_versions_vat` (0..1) and `ck_tax_group_versions_eco_tax` (>= 0). |
| `idempotency_keys` | Makes a repeated plan creation return the stored answer. Primary key `(key, endpoint)`. `request_hash` (SHA-256 as 64 characters) detects the same key sent with a different body (rejected); the first response is kept in `response_status` and `response_body` (jsonb). Rows older than 24 hours are deleted hourly. |
| `outbox_messages` | The transactional outbox: written in the same transaction as the data change, sent to Kafka afterwards. `id` becomes the event id in the envelope. `aggregate_id` becomes the Kafka key and `aggregate_type` picks the topic. `payload` is the event data (jsonb). Filtered index `ix_outbox_messages_occurred_at` on `occurred_at` `WHERE published_at IS NULL` keeps the publisher's query small however many sent rows there are. Sent rows older than 7 days are deleted hourly. |

The migration `SeedSampleData` inserts the sample plans and tax groups (and their outbox rows) with the schema.

## Recommendation database (`recommendation`)

Holds a copy of the catalog versions (the read model), the calculated recommendations and the acceptances.
The copy is written only by the catalog consumer and is never changed afterwards.

```mermaid
erDiagram
    recommendations ||--o| acceptances : "may be accepted once"
    recommendations }o..o{ plan_versions : "used versions (no foreign key)"
    recommendations }o..|| tax_group_versions : "used version (no foreign key)"

    plan_versions {
        uuid plan_id PK
        int version_no PK
        varchar_100 name
        numeric discount "numeric(5,4)"
        varchar_20 status "Active or Retired"
        date effective_from
        jsonb tiers "list of threshold and price per kWh"
        timestamptz received_at
    }
    tax_group_versions {
        varchar_50 code PK
        int version_no PK
        numeric vat "numeric(5,4)"
        numeric eco_tax "numeric(12,4)"
        varchar_20 status "Active or Retired"
        date effective_from
        timestamptz received_at
    }
    recommendations {
        uuid id PK
        numeric consumption_kwh "numeric(12,3)"
        varchar_50 tax_group_code
        int tax_group_version_no
        timestamptz calculated_at
        jsonb items "ranked plans: plan id, version, name, total"
    }
    acceptances {
        uuid recommendation_id PK, FK
        uuid plan_id
        int plan_version_no
        timestamptz accepted_at
    }
    outbox_messages {
        uuid id PK
        text aggregate_type "recommendation"
        text aggregate_id
        text event_type
        int event_version
        jsonb payload
        text correlation_id "nullable"
        timestamptz occurred_at
        timestamptz published_at "nullable"
    }
```

| Table | Purpose and notable constraints |
|---|---|
| `plan_versions` | Copy of every plan version received from `catalog.plan-events`, including future and retired versions. Same primary key as in the catalog, `(plan_id, version_no)`: the consumer inserts with `ON CONFLICT DO NOTHING`, so a duplicate or out-of-order event cannot change or corrupt the copy. The tiers are one `jsonb` column because the copy is always read whole and never changed. `received_at` is when this service stored it. There are no foreign keys: this table has no parent. The version used for a request is the one with the latest `effective_from` up to today, per plan. |
| `tax_group_versions` | The same for tax groups, filled from `catalog.tax-group-events`. Primary key `(code, version_no)`. |
| `recommendations` | One row per calculation. It records the consumption, the tax group code and version used, and `items` (jsonb): the ranked list of plan id, plan version, name and total, which gives the plan versions used. The per-tier breakdown is not stored; it is recalculated on read from the stored versions ([ADR 0007](../decisions/0007-recommendation-durability-and-storage.md)). A row is never updated. The links to the plan and tax group versions are logical (`tax_group_code` + `tax_group_version_no`, plan ids inside `items`), not foreign keys. |
| `acceptances` | At most one per recommendation: the primary key is `recommendation_id`, which is also a foreign key to `recommendations` (`ON DELETE RESTRICT`). Two parallel accepts collide on the key; the loser reads the winner's row and gets 200 (same plan) or 409 (another plan). It stores the accepted plan and its version number. |
| `outbox_messages` | The same outbox structure as in the catalog, for `RecommendationCalculated` and `PlanAccepted`, with the same filtered index `ix_outbox_messages_occurred_at` (`WHERE published_at IS NULL`). A recommendation or acceptance row is saved in the same transaction as its outbox row. Recommendations are saved with `synchronous_commit` off (ADR 0007): if the database crashes the row and its event are lost together, so the data stays consistent. |
