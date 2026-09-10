using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    ConsumerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AgencyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CaseManagerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SupervisorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestDate = table.Column<DateTime>(type: "date", nullable: true),
                    PayableTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MailingAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NeededByDate = table.Column<DateTime>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedByUserId = table.Column<int>(type: "int", nullable: true),
                    PublishedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckRequests_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckRequests_PersonId_RequestDate",
                table: "CheckRequests",
                columns: new[] { "PersonId", "RequestDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckRequests");
        }
    }
}
