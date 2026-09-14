using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyCheckRequestAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledForDate",
                table: "CheckRequests",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TemplateId",
                table: "CheckRequests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CheckRequestTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    GenerateOn = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    NeededByDaysAfterRequest = table.Column<int>(type: "int", nullable: false),
                    PayableTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MailingAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckRequestTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckRequestTemplates_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckRequests_TemplateId_ScheduledForDate",
                table: "CheckRequests",
                columns: new[] { "TemplateId", "ScheduledForDate" },
                unique: true,
                filter: "[TemplateId] IS NOT NULL AND [ScheduledForDate] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CheckRequestTemplates_PersonId",
                table: "CheckRequestTemplates",
                column: "PersonId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CheckRequests_CheckRequestTemplates_TemplateId",
                table: "CheckRequests",
                column: "TemplateId",
                principalTable: "CheckRequestTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CheckRequests_CheckRequestTemplates_TemplateId",
                table: "CheckRequests");

            migrationBuilder.DropTable(
                name: "CheckRequestTemplates");

            migrationBuilder.DropIndex(
                name: "IX_CheckRequests_TemplateId_ScheduledForDate",
                table: "CheckRequests");

            migrationBuilder.DropColumn(
                name: "ScheduledForDate",
                table: "CheckRequests");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "CheckRequests");
        }
    }
}
