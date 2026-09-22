using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupportReleaseAttestationReviewFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "FormId",
                table: "FormAttestationChangeReviewFlags",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<long>(
                name: "ReleaseObligationId",
                table: "FormAttestationChangeReviewFlags",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormAttestationChangeReviewFlags_ReleaseObligationId",
                table: "FormAttestationChangeReviewFlags",
                column: "ReleaseObligationId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormAttestationChangeReviewFlags_OneSource",
                table: "FormAttestationChangeReviewFlags",
                sql: "([FormId] IS NOT NULL AND [ReleaseObligationId] IS NULL) OR ([FormId] IS NULL AND [ReleaseObligationId] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_FormAttestationChangeReviewFlags_ReleaseObligations_ReleaseObligationId",
                table: "FormAttestationChangeReviewFlags",
                column: "ReleaseObligationId",
                principalTable: "ReleaseObligations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FormAttestationChangeReviewFlags_ReleaseObligations_ReleaseObligationId",
                table: "FormAttestationChangeReviewFlags");

            migrationBuilder.DropIndex(
                name: "IX_FormAttestationChangeReviewFlags_ReleaseObligationId",
                table: "FormAttestationChangeReviewFlags");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FormAttestationChangeReviewFlags_OneSource",
                table: "FormAttestationChangeReviewFlags");

            migrationBuilder.DropColumn(
                name: "ReleaseObligationId",
                table: "FormAttestationChangeReviewFlags");

            migrationBuilder.AlterColumn<int>(
                name: "FormId",
                table: "FormAttestationChangeReviewFlags",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
