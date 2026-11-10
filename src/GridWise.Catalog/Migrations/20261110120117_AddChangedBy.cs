using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GridWise.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class AddChangedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "changed_by",
                table: "tax_group_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "changed_by",
                table: "plan_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "changed_by",
                table: "tax_group_versions");

            migrationBuilder.DropColumn(
                name: "changed_by",
                table: "plan_versions");
        }
    }
}
