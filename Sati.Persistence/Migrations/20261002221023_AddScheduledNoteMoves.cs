using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledNoteMoves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledNoteMoves",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoteId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    FromDate = table.Column<DateTime>(type: "date", nullable: false),
                    ToDate = table.Column<DateTime>(type: "date", nullable: false),
                    ScheduledMinutes = table.Column<int>(type: "int", nullable: true),
                    ScheduledUnits = table.Column<int>(type: "int", nullable: true),
                    NoteRevision = table.Column<int>(type: "int", nullable: false),
                    MovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledNoteMoves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledNoteMoves_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledNoteMoves_NoteId_NoteRevision",
                table: "ScheduledNoteMoves",
                columns: new[] { "NoteId", "NoteRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledNoteMoves_UserId_FromDate",
                table: "ScheduledNoteMoves",
                columns: new[] { "UserId", "FromDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledNoteMoves");
        }
    }
}
