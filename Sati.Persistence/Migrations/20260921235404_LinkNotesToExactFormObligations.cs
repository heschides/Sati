using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class LinkNotesToExactFormObligations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FormDateCorrectionReason",
                table: "Notes",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FormId",
                table: "Notes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notes_FormId",
                table: "Notes",
                column: "FormId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_Forms_FormId",
                table: "Notes",
                column: "FormId",
                principalTable: "Forms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notes_Forms_FormId",
                table: "Notes");

            migrationBuilder.DropIndex(
                name: "IX_Notes_FormId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "FormDateCorrectionReason",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "FormId",
                table: "Notes");
        }
    }
}
