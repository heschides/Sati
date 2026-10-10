using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClearinghousePollRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClearinghouseAccountPollRotation",
                columns: table => new
                {
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastFeedKind = table.Column<int>(type: "int", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearinghouseAccountPollRotation", x => new { x.AgencyId, x.AccountId });
                    table.CheckConstraint("CK_ClearinghouseAccountPollRotation_Scope", "[AgencyId] > 0 AND [Revision] > 0 AND ([LastFeedKind] IS NULL OR [LastFeedKind] IN (1,2))");
                    table.ForeignKey(
                        name: "FK_ClearinghouseAccountPollRotation_ClearinghouseAccounts_AgencyId_AccountId",
                        columns: x => new { x.AgencyId, x.AccountId },
                        principalTable: "ClearinghouseAccounts",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClearinghouseAgencyPollRotation",
                columns: table => new
                {
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    LastAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearinghouseAgencyPollRotation", x => x.AgencyId);
                    table.CheckConstraint("CK_ClearinghouseAgencyPollRotation_Scope", "[AgencyId] > 0 AND [Revision] > 0");
                    table.ForeignKey(
                        name: "FK_ClearinghouseAgencyPollRotation_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClearinghousePollRotation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LastAgencyId = table.Column<int>(type: "int", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearinghousePollRotation", x => x.Id);
                    table.CheckConstraint("CK_ClearinghousePollRotation_Scope", "[Id] = 1 AND [Revision] > 0 AND ([LastAgencyId] IS NULL OR [LastAgencyId] > 0)");
                });

            migrationBuilder.InsertData(
                table: "ClearinghousePollRotation",
                columns: new[] { "Id", "LastAgencyId", "Revision" },
                values: new object[] { 1, null, 1L });

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseFeedCheckpoints_AgencyId_AccountId_FeedKind",
                table: "ClearinghouseFeedCheckpoints",
                columns: new[] { "AgencyId", "AccountId", "FeedKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseAccounts_AgencyId_IsEnabled_IsTest_ConnectorKind_Id",
                table: "ClearinghouseAccounts",
                columns: new[] { "AgencyId", "IsEnabled", "IsTest", "ConnectorKind", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.ClearinghouseAgencyPollRotation) OR " +
                "EXISTS (SELECT 1 FROM dbo.ClearinghouseAccountPollRotation) OR " +
                "NOT EXISTS (SELECT 1 FROM dbo.ClearinghousePollRotation WHERE Id = 1 AND LastAgencyId IS NULL AND Revision = 1) " +
                "THROW 51046, 'Polling rotation was used; review rollback before discarding scheduling position.', 1;");

            migrationBuilder.DropTable(
                name: "ClearinghouseAccountPollRotation");

            migrationBuilder.DropTable(
                name: "ClearinghouseAgencyPollRotation");

            migrationBuilder.DropTable(
                name: "ClearinghousePollRotation");

            migrationBuilder.DropIndex(
                name: "IX_ClearinghouseFeedCheckpoints_AgencyId_AccountId_FeedKind",
                table: "ClearinghouseFeedCheckpoints");

            migrationBuilder.DropIndex(
                name: "IX_ClearinghouseAccounts_AgencyId_IsEnabled_IsTest_ConnectorKind_Id",
                table: "ClearinghouseAccounts");
        }
    }
}
