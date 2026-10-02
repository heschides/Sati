using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsumerSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsumerScheduleEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Date = table.Column<DateTime>(type: "date", nullable: true),
                    EffectiveStart = table.Column<DateTime>(type: "date", nullable: true),
                    EffectiveEnd = table.Column<DateTime>(type: "date", nullable: true),
                    Weekdays = table.Column<int>(type: "int", nullable: false),
                    StartMinute = table.Column<int>(type: "int", nullable: true),
                    EndMinute = table.Column<int>(type: "int", nullable: true),
                    RideStatus = table.Column<int>(type: "int", nullable: false),
                    OutboundPickupMinute = table.Column<int>(type: "int", nullable: true),
                    ReturnPickupMinute = table.Column<int>(type: "int", nullable: true),
                    RideReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumerScheduleEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsumerScheduleEntries_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerScheduleEntries_PersonId_Kind_Date",
                table: "ConsumerScheduleEntries",
                columns: new[] { "PersonId", "Kind", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsumerScheduleEntries");
        }
    }
}
