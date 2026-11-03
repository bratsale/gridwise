using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GridWise.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class SeedSampleData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO plans (id, created_at) VALUES
                    ('5eed0000-0000-0000-0000-000000000001', '2026-01-01T00:00:00Z'),
                    ('5eed0000-0000-0000-0000-000000000002', '2026-01-01T00:00:00Z');
                """);

            migrationBuilder.Sql("""
                INSERT INTO plan_versions (plan_id, version_no, name, discount, status, effective_from, created_at) VALUES
                    ('5eed0000-0000-0000-0000-000000000001', 1, 'Standard', 0.05, 'Active', '2026-01-01', '2026-01-01T00:00:00Z'),
                    ('5eed0000-0000-0000-0000-000000000002', 1, 'Premium',  0.10, 'Active', '2026-01-01', '2026-01-01T00:00:00Z');
                """);

            migrationBuilder.Sql("""
                INSERT INTO plan_tiers (plan_id, version_no, tier_no, threshold, price_per_kwh) VALUES
                    ('5eed0000-0000-0000-0000-000000000001', 1, 1, 100,  0.10),
                    ('5eed0000-0000-0000-0000-000000000001', 1, 2, 300,  0.08),
                    ('5eed0000-0000-0000-0000-000000000001', 1, 3, 500,  0.07),
                    ('5eed0000-0000-0000-0000-000000000001', 1, 4, NULL, 0.06),
                    ('5eed0000-0000-0000-0000-000000000002', 1, 1, 200,  0.09),
                    ('5eed0000-0000-0000-0000-000000000002', 1, 2, 400,  0.07),
                    ('5eed0000-0000-0000-0000-000000000002', 1, 3, 600,  0.05),
                    ('5eed0000-0000-0000-0000-000000000002', 1, 4, NULL, 0.04);
                """);

            migrationBuilder.Sql("""
                INSERT INTO tax_groups (code, created_at) VALUES
                    ('household', '2026-01-01T00:00:00Z'),
                    ('business',  '2026-01-01T00:00:00Z');
                """);

            migrationBuilder.Sql("""
                INSERT INTO tax_group_versions (code, version_no, vat, eco_tax, status, effective_from, created_at) VALUES
                    ('household', 1, 0.17, 0.005, 'Active', '2026-01-01', '2026-01-01T00:00:00Z'),
                    ('business',  1, 0.20, 0.010, 'Active', '2026-01-01', '2026-01-01T00:00:00Z');
                """);

            migrationBuilder.Sql("""
                INSERT INTO outbox_messages
                    (id, aggregate_type, aggregate_id, event_type, event_version, payload, correlation_id, occurred_at, published_at) VALUES
                    ('5eed0000-0000-0000-0001-000000000001', 'plan', '5eed0000-0000-0000-0000-000000000001',
                     'PlanVersionCreated', 1,
                     '{"id":"5eed0000-0000-0000-0000-000000000001","versionNo":1,"name":"Standard","discount":0.05,"status":"Active","effectiveFrom":"2026-01-01","tiers":[{"threshold":100,"pricePerKwh":0.10},{"threshold":300,"pricePerKwh":0.08},{"threshold":500,"pricePerKwh":0.07},{"threshold":null,"pricePerKwh":0.06}]}',
                     'seed', '2026-01-01T00:00:00Z', NULL),
                    ('5eed0000-0000-0000-0001-000000000002', 'plan', '5eed0000-0000-0000-0000-000000000002',
                     'PlanVersionCreated', 1,
                     '{"id":"5eed0000-0000-0000-0000-000000000002","versionNo":1,"name":"Premium","discount":0.10,"status":"Active","effectiveFrom":"2026-01-01","tiers":[{"threshold":200,"pricePerKwh":0.09},{"threshold":400,"pricePerKwh":0.07},{"threshold":600,"pricePerKwh":0.05},{"threshold":null,"pricePerKwh":0.04}]}',
                     'seed', '2026-01-01T00:00:00Z', NULL),
                    ('5eed0000-0000-0000-0001-000000000003', 'tax-group', 'household',
                     'TaxGroupVersionCreated', 1,
                     '{"code":"household","versionNo":1,"vat":0.17,"ecoTax":0.005,"status":"Active","effectiveFrom":"2026-01-01"}',
                     'seed', '2026-01-01T00:00:00Z', NULL),
                    ('5eed0000-0000-0000-0001-000000000004', 'tax-group', 'business',
                     'TaxGroupVersionCreated', 1,
                     '{"code":"business","versionNo":1,"vat":0.20,"ecoTax":0.010,"status":"Active","effectiveFrom":"2026-01-01"}',
                     'seed', '2026-01-01T00:00:00Z', NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM outbox_messages WHERE id IN (
                    '5eed0000-0000-0000-0001-000000000001',
                    '5eed0000-0000-0000-0001-000000000002',
                    '5eed0000-0000-0000-0001-000000000003',
                    '5eed0000-0000-0000-0001-000000000004');
                """);

            migrationBuilder.Sql("""
                DELETE FROM plan_tiers WHERE plan_id IN (
                    '5eed0000-0000-0000-0000-000000000001',
                    '5eed0000-0000-0000-0000-000000000002');
                """);

            migrationBuilder.Sql("""
                DELETE FROM plan_versions WHERE plan_id IN (
                    '5eed0000-0000-0000-0000-000000000001',
                    '5eed0000-0000-0000-0000-000000000002');
                """);

            migrationBuilder.Sql("""
                DELETE FROM plans WHERE id IN (
                    '5eed0000-0000-0000-0000-000000000001',
                    '5eed0000-0000-0000-0000-000000000002');
                """);

            migrationBuilder.Sql("DELETE FROM tax_group_versions WHERE code IN ('household', 'business');");

            migrationBuilder.Sql("DELETE FROM tax_groups WHERE code IN ('household', 'business');");
        }
    }
}
