using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupportDurableOneOffReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentArtifacts_OneLivePerCycle",
                table: "DocumentArtifacts");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Providers",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "ReleaseObligationId",
                table: "ExternalSignatureEvidence",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "OneOffRecipientJson",
                table: "DocumentArtifacts",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OneOffReleaseId",
                table: "DocumentArtifacts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromotedProviderId",
                table: "DocumentArtifacts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentArtifacts_OneLivePerCycle",
                table: "DocumentArtifacts",
                columns: new[] { "PersonId", "Kind", "CycleStart" },
                unique: true,
                filter: "[ReleaseObligationId] IS NULL AND [OneOffReleaseId] IS NULL AND [SupersededByArtifactId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentArtifacts_OneLivePerOneOffRelease",
                table: "DocumentArtifacts",
                columns: new[] { "OneOffReleaseId", "Kind" },
                unique: true,
                filter: "[OneOffReleaseId] IS NOT NULL AND [SupersededByArtifactId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentArtifacts_PromotedProviderId",
                table: "DocumentArtifacts",
                column: "PromotedProviderId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentArtifacts_Providers_PromotedProviderId",
                table: "DocumentArtifacts",
                column: "PromotedProviderId",
                principalTable: "Providers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentArtifacts_Providers_PromotedProviderId",
                table: "DocumentArtifacts");

            migrationBuilder.DropIndex(
                name: "IX_DocumentArtifacts_OneLivePerCycle",
                table: "DocumentArtifacts");

            migrationBuilder.DropIndex(
                name: "IX_DocumentArtifacts_OneLivePerOneOffRelease",
                table: "DocumentArtifacts");

            migrationBuilder.DropIndex(
                name: "IX_DocumentArtifacts_PromotedProviderId",
                table: "DocumentArtifacts");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "OneOffRecipientJson",
                table: "DocumentArtifacts");

            migrationBuilder.DropColumn(
                name: "OneOffReleaseId",
                table: "DocumentArtifacts");

            migrationBuilder.DropColumn(
                name: "PromotedProviderId",
                table: "DocumentArtifacts");

            migrationBuilder.AlterColumn<long>(
                name: "ReleaseObligationId",
                table: "ExternalSignatureEvidence",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentArtifacts_OneLivePerCycle",
                table: "DocumentArtifacts",
                columns: new[] { "PersonId", "Kind", "CycleStart" },
                unique: true,
                filter: "[ReleaseObligationId] IS NULL AND [SupersededByArtifactId] IS NULL");
        }
    }
}
