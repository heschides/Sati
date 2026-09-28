using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnualPcpAndUnbilledNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAnnualPlan",
                table: "Notes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsUnbilled",
                table: "Notes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Before the explicit marker existed, linking a PCP note to an exact
            // Form row was the only annual-PCP representation. Preserve that
            // meaning so existing notes continue to display and behave as annual.
            migrationBuilder.Sql(
                "UPDATE [Notes] SET [IsAnnualPlan] = 1 " +
                "WHERE [FormType] = 4 AND [FormId] IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAnnualPlan",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "IsUnbilled",
                table: "Notes");
        }
    }
}
