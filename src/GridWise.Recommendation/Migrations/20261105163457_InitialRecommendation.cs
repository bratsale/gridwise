using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GridWise.Recommendation.Migrations
{
    /// <inheritdoc />
    public partial class InitialRecommendation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_type = table.Column<string>(type: "text", nullable: false),
                    aggregate_id = table.Column<string>(type: "text", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    event_version = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan_versions",
                columns: table => new
                {
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    tiers = table.Column<string>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_versions", x => new { x.plan_id, x.version_no });
                });

            migrationBuilder.CreateTable(
                name: "recommendations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumption_kwh = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    tax_group_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tax_group_version_no = table.Column<int>(type: "integer", nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    items = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_group_versions",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    vat = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    eco_tax = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_group_versions", x => new { x.code, x.version_no });
                });

            migrationBuilder.CreateTable(
                name: "acceptances",
                columns: table => new
                {
                    recommendation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_version_no = table.Column<int>(type: "integer", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acceptances", x => x.recommendation_id);
                    table.ForeignKey(
                        name: "fk_acceptances_plan_recommendations_recommendation_id",
                        column: x => x.recommendation_id,
                        principalTable: "recommendations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "published_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acceptances");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "plan_versions");

            migrationBuilder.DropTable(
                name: "tax_group_versions");

            migrationBuilder.DropTable(
                name: "recommendations");
        }
    }
}
