using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddRepresentativePayeeWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Administration is Sati's root agency capability. Existing administrators
            // predate the new bit, so extend their stored permission set on upgrade.
            migrationBuilder.Sql("""
                UPDATE [Users]
                SET [Permissions] = [Permissions] | 32
                WHERE ([Permissions] & 4) = 4 AND ([Permissions] & 32) = 0;
                """);

            migrationBuilder.CreateTable(
                name: "CheckRequestWorkflowEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CheckRequestId = table.Column<int>(type: "int", nullable: false),
                    Checkpoint = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckRequestWorkflowEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckRequestWorkflowEvents_CheckRequests_CheckRequestId",
                        column: x => x.CheckRequestId,
                        principalTable: "CheckRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepresentativePayeeLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    CheckRequestId = table.Column<int>(type: "int", nullable: true),
                    EntryDate = table.Column<DateTime>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: false),
                    RecordedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepresentativePayeeLedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepresentativePayeeLedgerEntries_CheckRequests_CheckRequestId",
                        column: x => x.CheckRequestId,
                        principalTable: "CheckRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepresentativePayeeLedgerEntries_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckRequestWorkflowEvents_CheckRequestId_Checkpoint",
                table: "CheckRequestWorkflowEvents",
                columns: new[] { "CheckRequestId", "Checkpoint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepresentativePayeeLedgerEntries_CheckRequestId",
                table: "RepresentativePayeeLedgerEntries",
                column: "CheckRequestId",
                unique: true,
                filter: "[CheckRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RepresentativePayeeLedgerEntries_PersonId_EntryDate_Id",
                table: "RepresentativePayeeLedgerEntries",
                columns: new[] { "PersonId", "EntryDate", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckRequestWorkflowEvents");

            migrationBuilder.DropTable(
                name: "RepresentativePayeeLedgerEntries");

            migrationBuilder.Sql("""
                UPDATE [Users]
                SET [Permissions] = [Permissions] & ~32
                WHERE ([Permissions] & 32) = 32;
                """);
        }
    }
}
