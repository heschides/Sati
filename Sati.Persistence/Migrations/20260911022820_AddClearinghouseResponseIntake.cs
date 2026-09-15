using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddClearinghouseResponseIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ResponseId",
                table: "RemittanceDeposits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EdiGenerationId",
                table: "RemittanceClaimOutcomes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResponseId",
                table: "RemittanceClaimOutcomes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ControlNumber",
                table: "EdiGenerations",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EdiGenerationId",
                table: "BillingSubmissionEvents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResponseId",
                table: "BillingSubmissionEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClearinghouseResponseReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    IsTest = table.Column<bool>(type: "bit", nullable: false),
                    ParserVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RawSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SemanticSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdentitySha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PaymentIdentitySha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Ciphertext = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Nonce = table.Column<byte[]>(type: "varbinary(12)", maxLength: 12, nullable: false),
                    Tag = table.Column<byte[]>(type: "varbinary(16)", maxLength: 16, nullable: false),
                    WrappedDataKey = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    KeyId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StageRecorded = table.Column<int>(type: "int", nullable: true),
                    ClaimOutcomesRecorded = table.Column<int>(type: "int", nullable: false),
                    DepositRecorded = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearinghouseResponseReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearinghouseResponseReceipts_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClearinghouseResponseReceipts_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClearinghouseResponseMatches",
                columns: table => new
                {
                    ResponseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EdiGenerationId = table.Column<long>(type: "bigint", nullable: false),
                    ClaimReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    BillingPeriodId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearinghouseResponseMatches", x => new { x.ResponseId, x.EdiGenerationId, x.ClaimReference });
                    table.ForeignKey(
                        name: "FK_ClearinghouseResponseMatches_BillingPeriods_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "BillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClearinghouseResponseMatches_ClearinghouseResponseReceipts_ResponseId",
                        column: x => x.ResponseId,
                        principalTable: "ClearinghouseResponseReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClearinghouseResponseMatches_EdiGenerations_EdiGenerationId",
                        column: x => x.EdiGenerationId,
                        principalTable: "EdiGenerations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });


            migrationBuilder.CreateIndex(
                name: "IX_RemittanceDeposits_ResponseId",
                table: "RemittanceDeposits",
                column: "ResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_RemittanceClaimOutcomes_EdiGenerationId",
                table: "RemittanceClaimOutcomes",
                column: "EdiGenerationId");

            migrationBuilder.CreateIndex(
                name: "IX_RemittanceClaimOutcomes_ResponseId",
                table: "RemittanceClaimOutcomes",
                column: "ResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_EdiGenerations_AgencyId_IsTest_ControlNumber",
                table: "EdiGenerations",
                columns: new[] { "AgencyId", "IsTest", "ControlNumber" },
                unique: true,
                filter: "[ControlNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BillingSubmissionEvents_EdiGenerationId",
                table: "BillingSubmissionEvents",
                column: "EdiGenerationId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingSubmissionEvents_ResponseId",
                table: "BillingSubmissionEvents",
                column: "ResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseResponseMatches_BillingPeriodId",
                table: "ClearinghouseResponseMatches",
                column: "BillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseResponseMatches_EdiGenerationId",
                table: "ClearinghouseResponseMatches",
                column: "EdiGenerationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseResponseReceipts_ActorUserId",
                table: "ClearinghouseResponseReceipts",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseResponseReceipts_AgencyId_IsTest_IdentitySha256",
                table: "ClearinghouseResponseReceipts",
                columns: new[] { "AgencyId", "IsTest", "IdentitySha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseResponseReceipts_AgencyId_IsTest_PaymentIdentitySha256",
                table: "ClearinghouseResponseReceipts",
                columns: new[] { "AgencyId", "IsTest", "PaymentIdentitySha256" },
                unique: true,
                filter: "[PaymentIdentitySha256] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseResponseReceipts_AgencyId_IsTest_SemanticSha256",
                table: "ClearinghouseResponseReceipts",
                columns: new[] { "AgencyId", "IsTest", "SemanticSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseResponseReceipts_AgencyId_RawSha256",
                table: "ClearinghouseResponseReceipts",
                columns: new[] { "AgencyId", "RawSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseResponseReceipts_AgencyId_ReceivedAtUtc",
                table: "ClearinghouseResponseReceipts",
                columns: new[] { "AgencyId", "ReceivedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_BillingSubmissionEvents_ClearinghouseResponseReceipts_ResponseId",
                table: "BillingSubmissionEvents",
                column: "ResponseId",
                principalTable: "ClearinghouseResponseReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BillingSubmissionEvents_EdiGenerations_EdiGenerationId",
                table: "BillingSubmissionEvents",
                column: "EdiGenerationId",
                principalTable: "EdiGenerations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RemittanceClaimOutcomes_ClearinghouseResponseReceipts_ResponseId",
                table: "RemittanceClaimOutcomes",
                column: "ResponseId",
                principalTable: "ClearinghouseResponseReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RemittanceClaimOutcomes_EdiGenerations_EdiGenerationId",
                table: "RemittanceClaimOutcomes",
                column: "EdiGenerationId",
                principalTable: "EdiGenerations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RemittanceDeposits_ClearinghouseResponseReceipts_ResponseId",
                table: "RemittanceDeposits",
                column: "ResponseId",
                principalTable: "ClearinghouseResponseReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
                migrationBuilder.Sql("""
                    IF EXISTS (SELECT 1 FROM dbo.ClearinghouseResponseReceipts)
                       OR EXISTS (SELECT 1 FROM dbo.ClearinghouseResponseMatches)
                        THROW 51000, 'Retained clearinghouse evidence prevents rollback. Preserve the records and use a forward correction.', 1;
                    """);
            migrationBuilder.DropForeignKey(
                name: "FK_BillingSubmissionEvents_ClearinghouseResponseReceipts_ResponseId",
                table: "BillingSubmissionEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_BillingSubmissionEvents_EdiGenerations_EdiGenerationId",
                table: "BillingSubmissionEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_RemittanceClaimOutcomes_ClearinghouseResponseReceipts_ResponseId",
                table: "RemittanceClaimOutcomes");

            migrationBuilder.DropForeignKey(
                name: "FK_RemittanceClaimOutcomes_EdiGenerations_EdiGenerationId",
                table: "RemittanceClaimOutcomes");

            migrationBuilder.DropForeignKey(
                name: "FK_RemittanceDeposits_ClearinghouseResponseReceipts_ResponseId",
                table: "RemittanceDeposits");

            migrationBuilder.DropTable(
                name: "ClearinghouseResponseMatches");

            migrationBuilder.DropTable(
                name: "ClearinghouseResponseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_RemittanceDeposits_ResponseId",
                table: "RemittanceDeposits");

            migrationBuilder.DropIndex(
                name: "IX_RemittanceClaimOutcomes_EdiGenerationId",
                table: "RemittanceClaimOutcomes");

            migrationBuilder.DropIndex(
                name: "IX_RemittanceClaimOutcomes_ResponseId",
                table: "RemittanceClaimOutcomes");

            migrationBuilder.DropIndex(
                name: "IX_EdiGenerations_AgencyId_IsTest_ControlNumber",
                table: "EdiGenerations");

            migrationBuilder.DropIndex(
                name: "IX_BillingSubmissionEvents_EdiGenerationId",
                table: "BillingSubmissionEvents");

            migrationBuilder.DropIndex(
                name: "IX_BillingSubmissionEvents_ResponseId",
                table: "BillingSubmissionEvents");

            migrationBuilder.DropColumn(
                name: "ResponseId",
                table: "RemittanceDeposits");

            migrationBuilder.DropColumn(
                name: "EdiGenerationId",
                table: "RemittanceClaimOutcomes");

            migrationBuilder.DropColumn(
                name: "ResponseId",
                table: "RemittanceClaimOutcomes");

            migrationBuilder.DropColumn(
                name: "ControlNumber",
                table: "EdiGenerations");

            migrationBuilder.DropColumn(
                name: "EdiGenerationId",
                table: "BillingSubmissionEvents");

            migrationBuilder.DropColumn(
                name: "ResponseId",
                table: "BillingSubmissionEvents");

        }
    }
}
