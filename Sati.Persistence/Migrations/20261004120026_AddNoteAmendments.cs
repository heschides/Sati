using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteAmendments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AmendedNoteVersionId",
                table: "ClaimLines",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AmendedNoteVersionId",
                table: "ClaimCorrections",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CorrectedChargeAmount",
                table: "ClaimCorrections",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CorrectedDateOfService",
                table: "ClaimCorrections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CorrectedUnits",
                table: "ClaimCorrections",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NoteAmendments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    NoteId = table.Column<int>(type: "int", nullable: false),
                    AuthorId = table.Column<int>(type: "int", nullable: false),
                    OriginalNoteRevision = table.Column<int>(type: "int", nullable: false),
                    OriginalSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaseApprovedVersionId = table.Column<long>(type: "bigint", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CurrentVersionId = table.Column<long>(type: "bigint", nullable: false),
                    SubmittedVersionId = table.Column<long>(type: "bigint", nullable: true),
                    ApprovedVersionId = table.Column<long>(type: "bigint", nullable: true),
                    ChangesFinancialFacts = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteAmendments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoteAmendments_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NoteAmendmentVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AmendmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FinancialContentJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RecordedById = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteAmendmentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoteAmendmentVersions_NoteAmendments_AmendmentId",
                        column: x => x.AmendmentId,
                        principalTable: "NoteAmendments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NoteAmendmentEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AmendmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    VersionId = table.Column<long>(type: "bigint", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReviewReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteAmendmentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoteAmendmentEvents_NoteAmendmentVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "NoteAmendmentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NoteAmendmentEvents_NoteAmendments_AmendmentId",
                        column: x => x.AmendmentId,
                        principalTable: "NoteAmendments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NoteAmendmentFinancialReviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    NoteId = table.Column<int>(type: "int", nullable: false),
                    ApprovedVersionId = table.Column<long>(type: "bigint", nullable: false),
                    ReviewedById = table.Column<int>(type: "int", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteAmendmentFinancialReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoteAmendmentFinancialReviews_NoteAmendmentVersions_ApprovedVersionId",
                        column: x => x.ApprovedVersionId,
                        principalTable: "NoteAmendmentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NoteAmendmentFinancialReviews_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimLines_AmendedNoteVersionId",
                table: "ClaimLines",
                column: "AmendedNoteVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimCorrections_AmendedNoteVersionId",
                table: "ClaimCorrections",
                column: "AmendedNoteVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteAmendmentEvents_AgencyId_ActorId_OperationId",
                table: "NoteAmendmentEvents",
                columns: new[] { "AgencyId", "ActorId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NoteAmendmentEvents_AmendmentId",
                table: "NoteAmendmentEvents",
                column: "AmendmentId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteAmendmentEvents_VersionId",
                table: "NoteAmendmentEvents",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteAmendmentFinancialReviews_AgencyId_ReviewedById_OperationId",
                table: "NoteAmendmentFinancialReviews",
                columns: new[] { "AgencyId", "ReviewedById", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NoteAmendmentFinancialReviews_ApprovedVersionId",
                table: "NoteAmendmentFinancialReviews",
                column: "ApprovedVersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NoteAmendmentFinancialReviews_NoteId",
                table: "NoteAmendmentFinancialReviews",
                column: "NoteId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteAmendments_AgencyId_NoteId",
                table: "NoteAmendments",
                columns: new[] { "AgencyId", "NoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_NoteAmendments_NoteId",
                table: "NoteAmendments",
                column: "NoteId",
                unique: true,
                filter: "[Status] IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_NoteAmendmentVersions_AmendmentId_Number",
                table: "NoteAmendmentVersions",
                columns: new[] { "AmendmentId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ClaimCorrections_NoteAmendmentVersions_AmendedNoteVersionId",
                table: "ClaimCorrections",
                column: "AmendedNoteVersionId",
                principalTable: "NoteAmendmentVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClaimLines_NoteAmendmentVersions_AmendedNoteVersionId",
                table: "ClaimLines",
                column: "AmendedNoteVersionId",
                principalTable: "NoteAmendmentVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.NoteAmendments)
                    THROW 51031, 'Note amendment history exists. Preserve the schema and roll forward; destructive rollback is forbidden.', 1;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_ClaimCorrections_NoteAmendmentVersions_AmendedNoteVersionId",
                table: "ClaimCorrections");

            migrationBuilder.DropForeignKey(
                name: "FK_ClaimLines_NoteAmendmentVersions_AmendedNoteVersionId",
                table: "ClaimLines");

            migrationBuilder.DropTable(
                name: "NoteAmendmentEvents");

            migrationBuilder.DropTable(
                name: "NoteAmendmentFinancialReviews");

            migrationBuilder.DropTable(
                name: "NoteAmendmentVersions");

            migrationBuilder.DropTable(
                name: "NoteAmendments");

            migrationBuilder.DropIndex(
                name: "IX_ClaimLines_AmendedNoteVersionId",
                table: "ClaimLines");

            migrationBuilder.DropIndex(
                name: "IX_ClaimCorrections_AmendedNoteVersionId",
                table: "ClaimCorrections");

            migrationBuilder.DropColumn(
                name: "AmendedNoteVersionId",
                table: "ClaimLines");

            migrationBuilder.DropColumn(
                name: "AmendedNoteVersionId",
                table: "ClaimCorrections");

            migrationBuilder.DropColumn(
                name: "CorrectedChargeAmount",
                table: "ClaimCorrections");

            migrationBuilder.DropColumn(
                name: "CorrectedDateOfService",
                table: "ClaimCorrections");

            migrationBuilder.DropColumn(
                name: "CorrectedUnits",
                table: "ClaimCorrections");
        }
    }
}
