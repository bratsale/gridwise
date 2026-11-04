# Catalog events

Catalog owns these contracts. Other services get them only as documents (this folder), not as a shared library.
JSON is camelCase. Dates are `yyyy-MM-dd`. Numbers are decimal.
The envelope (event type, version, correlation id) is described in ADR 0005.

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

## Evolution rules

- Within a version only optional fields may be added.
- Consumers ignore fields they do not know.
- A breaking change (remove, rename, change type or meaning) is a new event version (`V2`), published alongside the old one for a transition period. The old version is removed only after all consumers have moved.
