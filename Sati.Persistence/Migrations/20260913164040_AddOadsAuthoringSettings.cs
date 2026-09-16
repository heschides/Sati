using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddOadsAuthoringSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsClassificationAuthoringEnabled",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsComprehensiveAssessmentAuthoringEnabled",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPersonCenteredPlanAuthoringEnabled",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsClassificationAuthoringEnabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "IsComprehensiveAssessmentAuthoringEnabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "IsPersonCenteredPlanAuthoringEnabled",
                table: "Settings");
        }
    }
}
