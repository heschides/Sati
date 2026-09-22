using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkReleaseNotesToExactObligations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EvidenceNoteId",
                table: "ReleaseObligationAttestations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReleaseObligationId",
                table: "Notes",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notes_ReleaseObligationId",
                table: "Notes",
                column: "ReleaseObligationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_ReleaseObligations_ReleaseObligationId",
                table: "Notes",
                column: "ReleaseObligationId",
                principalTable: "ReleaseObligations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notes_ReleaseObligations_ReleaseObligationId",
                table: "Notes");

            migrationBuilder.DropIndex(
                name: "IX_Notes_ReleaseObligationId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "EvidenceNoteId",
                table: "ReleaseObligationAttestations");

            migrationBuilder.DropColumn(
                name: "ReleaseObligationId",
                table: "Notes");
        }
    }
}
