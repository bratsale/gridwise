using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GridWise.Recommendation.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationCreatedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "created_by",
                table: "recommendations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_by",
                table: "recommendations");
        }
    }
}
