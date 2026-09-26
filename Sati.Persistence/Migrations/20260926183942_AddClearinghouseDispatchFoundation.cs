using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClearinghouseDispatchFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ActorUserId",
                table: "ClearinghouseResponseReceipts",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                table: "ClearinghouseResponseReceipts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectorKind",
                table: "ClearinghouseResponseReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConnectorVersion",
                table: "ClearinghouseResponseReceipts",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "ClearinghouseResponseReceipts",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalArtifactId",
                table: "ClearinghouseResponseReceipts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FeedKind",
                table: "ClearinghouseResponseReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "ClearinghouseResponseReceipts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EdiGenerations_AgencyId_Id",
                table: "EdiGenerations",
                columns: new[] { "AgencyId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ClearinghouseResponseReceipts_AgencyId_Id",
                table: "ClearinghouseResponseReceipts",
                columns: new[] { "AgencyId", "Id" });

            migrationBuilder.CreateTable(
                name: "ClearinghouseAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    ConnectorKind = table.Column<int>(type: "int", nullable: false),
                    IsTest = table.Column<bool>(type: "bit", nullable: false),
                    ExternalAccountNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ClaimNamespace = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    SecretReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TradingPartnerProfileVersion = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearinghouseAccounts", x => x.Id);
                    table.UniqueConstraint("AK_ClearinghouseAccounts_AgencyId_Id", x => new { x.AgencyId, x.Id });
                    table.CheckConstraint("CK_ClearinghouseAccounts_ClaimMdProfile", "[ConnectorKind] <> 2 OR ([ClaimNamespace] IS NOT NULL AND [TradingPartnerProfileVersion] > 0)");
                    table.ForeignKey(
                        name: "FK_ClearinghouseAccounts_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClearinghouseDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EdiGenerationId = table.Column<long>(type: "bigint", nullable: false),
                    RequestingUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    TradingPartnerProfileVersion = table.Column<int>(type: "int", nullable: false),
                    ExternalFileId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AcceptedClaimCount = table.Column<int>(type: "int", nullable: true),
                    RejectedClaimCount = table.Column<int>(type: "int", nullable: true),
                    SafeErrorCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearinghouseDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearinghouseDispatches_ClearinghouseAccounts_AgencyId_AccountId",
                        columns: x => new { x.AgencyId, x.AccountId },
                        principalTable: "ClearinghouseAccounts",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClearinghouseDispatches_EdiGenerations_AgencyId_EdiGenerationId",
                        columns: x => new { x.AgencyId, x.EdiGenerationId },
                        principalTable: "EdiGenerations",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClearinghouseDispatches_Users_RequestingUserId",
                        column: x => x.RequestingUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClearinghouseFeedCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeedKind = table.Column<int>(type: "int", nullable: false),
                    Cursor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearinghouseFeedCheckpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearinghouseFeedCheckpoints_ClearinghouseAccounts_AgencyId_AccountId",
                        columns: x => new { x.AgencyId, x.AccountId },
                        principalTable: "ClearinghouseAccounts",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClearinghouseFeedCheckpoints_ClearinghouseResponseReceipts_AgencyId_LastReceiptId",
                        columns: x => new { x.AgencyId, x.LastReceiptId },
                        principalTable: "ClearinghouseResponseReceipts",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClearinghouseDispatchAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DispatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ContentSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    VendorCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ResponseSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ResponseCiphertext = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ResponseNonce = table.Column<byte[]>(type: "varbinary(12)", maxLength: 12, nullable: true),
                    ResponseTag = table.Column<byte[]>(type: "varbinary(16)", maxLength: 16, nullable: true),
                    ResponseWrappedDataKey = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ResponseKeyId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearinghouseDispatchAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearinghouseDispatchAttempts_ClearinghouseDispatches_DispatchId",
                        column: x => x.DispatchId,
                        principalTable: "ClearinghouseDispatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseResponseReceipts_AccountId_FeedKind_ExternalArtifactId",
                table: "ClearinghouseResponseReceipts",
                columns: new[] { "AccountId", "FeedKind", "ExternalArtifactId" },
                unique: true,
                filter: "[AccountId] IS NOT NULL AND [FeedKind] IS NOT NULL AND [ExternalArtifactId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseResponseReceipts_AgencyId_AccountId",
                table: "ClearinghouseResponseReceipts",
                columns: new[] { "AgencyId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseAccounts_AgencyId_ConnectorKind_IsTest",
                table: "ClearinghouseAccounts",
                columns: new[] { "AgencyId", "ConnectorKind", "IsTest" },
                unique: true,
                filter: "[IsEnabled] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseAccounts_ClaimNamespace",
                table: "ClearinghouseAccounts",
                column: "ClaimNamespace",
                unique: true,
                filter: "[ClaimNamespace] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseDispatchAttempts_DispatchId_AttemptNumber",
                table: "ClearinghouseDispatchAttempts",
                columns: new[] { "DispatchId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseDispatches_AgencyId_AccountId",
                table: "ClearinghouseDispatches",
                columns: new[] { "AgencyId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseDispatches_AgencyId_EdiGenerationId",
                table: "ClearinghouseDispatches",
                columns: new[] { "AgencyId", "EdiGenerationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseDispatches_RequestingUserId",
                table: "ClearinghouseDispatches",
                column: "RequestingUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseDispatches_AgencyId_State_RequestedAtUtc",
                table: "ClearinghouseDispatches",
                columns: new[] { "AgencyId", "State", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseDispatches_EdiGenerationId",
                table: "ClearinghouseDispatches",
                column: "EdiGenerationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseFeedCheckpoints_AccountId_FeedKind",
                table: "ClearinghouseFeedCheckpoints",
                columns: new[] { "AccountId", "FeedKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseFeedCheckpoints_AgencyId_AccountId",
                table: "ClearinghouseFeedCheckpoints",
                columns: new[] { "AgencyId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseFeedCheckpoints_AgencyId_LastReceiptId",
                table: "ClearinghouseFeedCheckpoints",
                columns: new[] { "AgencyId", "LastReceiptId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ClearinghouseResponseReceipts_ClearinghouseAccounts_AgencyId_AccountId",
                table: "ClearinghouseResponseReceipts",
                columns: new[] { "AgencyId", "AccountId" },
                principalTable: "ClearinghouseAccounts",
                principalColumns: new[] { "AgencyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
                migrationBuilder.Sql("""
                    IF EXISTS (SELECT 1 FROM dbo.ClearinghouseAccounts)
                       OR EXISTS (SELECT 1 FROM dbo.ClearinghouseDispatches)
                       OR EXISTS (SELECT 1 FROM dbo.ClearinghouseDispatchAttempts)
                       OR EXISTS (SELECT 1 FROM dbo.ClearinghouseFeedCheckpoints)
                       OR EXISTS (SELECT 1 FROM dbo.ClearinghouseResponseReceipts
                           WHERE ActorUserId IS NULL OR Source <> 0 OR AccountId IS NOT NULL
                              OR ConnectorKind IS NOT NULL OR FeedKind IS NOT NULL
                              OR ExternalArtifactId IS NOT NULL OR ContentType IS NOT NULL
                              OR ConnectorVersion IS NOT NULL)
                        THROW 51001, 'Clearinghouse account, dispatch, or provenance evidence prevents rollback. Preserve it and use a forward correction.', 1;
                    """);
            migrationBuilder.DropForeignKey(
                name: "FK_ClearinghouseResponseReceipts_ClearinghouseAccounts_AgencyId_AccountId",
                table: "ClearinghouseResponseReceipts");

            migrationBuilder.DropTable(
                name: "ClearinghouseDispatchAttempts");

            migrationBuilder.DropTable(
                name: "ClearinghouseFeedCheckpoints");

            migrationBuilder.DropTable(
                name: "ClearinghouseDispatches");

            migrationBuilder.DropTable(
                name: "ClearinghouseAccounts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EdiGenerations_AgencyId_Id",
                table: "EdiGenerations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ClearinghouseResponseReceipts_AgencyId_Id",
                table: "ClearinghouseResponseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_ClearinghouseResponseReceipts_AccountId_FeedKind_ExternalArtifactId",
                table: "ClearinghouseResponseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_ClearinghouseResponseReceipts_AgencyId_AccountId",
                table: "ClearinghouseResponseReceipts");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "ClearinghouseResponseReceipts");

            migrationBuilder.DropColumn(
                name: "ConnectorKind",
                table: "ClearinghouseResponseReceipts");

            migrationBuilder.DropColumn(
                name: "ConnectorVersion",
                table: "ClearinghouseResponseReceipts");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "ClearinghouseResponseReceipts");

            migrationBuilder.DropColumn(
                name: "ExternalArtifactId",
                table: "ClearinghouseResponseReceipts");

            migrationBuilder.DropColumn(
                name: "FeedKind",
                table: "ClearinghouseResponseReceipts");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ClearinghouseResponseReceipts");

            migrationBuilder.AlterColumn<int>(
                name: "ActorUserId",
                table: "ClearinghouseResponseReceipts",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

        }
    }
}
