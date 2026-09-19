using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddEftDepositsAndClaimCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayerClaimControlNumber",
                table: "RemittanceClaimOutcomes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCorrection",
                table: "EdiGenerations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ClaimAcknowledgementOutcomes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    BillingPeriodId = table.Column<int>(type: "int", nullable: false),
                    EdiGenerationId = table.Column<long>(type: "bigint", nullable: false),
                    ResponseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Disposition = table.Column<int>(type: "int", nullable: false),
                    CategoryCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StatusCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsSynthetic = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimAcknowledgementOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimAcknowledgementOutcomes_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClaimAcknowledgementOutcomes_BillingPeriods_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "BillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClaimAcknowledgementOutcomes_ClearinghouseResponseReceipts_ResponseId",
                        column: x => x.ResponseId,
                        principalTable: "ClearinghouseResponseReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClaimAcknowledgementOutcomes_EdiGenerations_EdiGenerationId",
                        column: x => x.EdiGenerationId,
                        principalTable: "EdiGenerations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimCorrections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    BillingPeriodId = table.Column<int>(type: "int", nullable: false),
                    ClaimLineId = table.Column<int>(type: "int", nullable: false),
                    NoteId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    PayerClaimControlNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClaimSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClientMaineCareId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RenderingProviderNpi = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DiagnosisCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PlaceOfService = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CorrectsEdiGenerationId = table.Column<long>(type: "bigint", nullable: true),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimCorrections_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClaimCorrections_BillingPeriods_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "BillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClaimCorrections_ClaimLines_ClaimLineId",
                        column: x => x.ClaimLineId,
                        principalTable: "ClaimLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClaimCorrections_EdiGenerations_CorrectsEdiGenerationId",
                        column: x => x.CorrectsEdiGenerationId,
                        principalTable: "EdiGenerations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClaimCorrections_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EftDepositRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    RemittanceDepositId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DepositDate = table.Column<DateTime>(type: "date", nullable: false),
                    BankTraceNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SupersedesRecordId = table.Column<long>(type: "bigint", nullable: true),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsSynthetic = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EftDepositRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EftDepositRecords_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EftDepositRecords_EftDepositRecords_SupersedesRecordId",
                        column: x => x.SupersedesRecordId,
                        principalTable: "EftDepositRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EftDepositRecords_RemittanceDeposits_RemittanceDepositId",
                        column: x => x.RemittanceDepositId,
                        principalTable: "RemittanceDeposits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EftDepositRecords_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimCorrectionSubmissions",
                columns: table => new
                {
                    ClaimCorrectionId = table.Column<long>(type: "bigint", nullable: false),
                    EdiGenerationId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimCorrectionSubmissions", x => new { x.ClaimCorrectionId, x.EdiGenerationId });
                    table.ForeignKey(
                        name: "FK_ClaimCorrectionSubmissions_ClaimCorrections_ClaimCorrectionId",
                        column: x => x.ClaimCorrectionId,
                        principalTable: "ClaimCorrections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClaimCorrectionSubmissions_EdiGenerations_EdiGenerationId",
                        column: x => x.EdiGenerationId,
                        principalTable: "EdiGenerations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimAcknowledgementOutcomes_AgencyId_EdiGenerationId_ClaimReference",
                table: "ClaimAcknowledgementOutcomes",
                columns: new[] { "AgencyId", "EdiGenerationId", "ClaimReference" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimAcknowledgementOutcomes_BillingPeriodId",
                table: "ClaimAcknowledgementOutcomes",
                column: "BillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimAcknowledgementOutcomes_EdiGenerationId",
                table: "ClaimAcknowledgementOutcomes",
                column: "EdiGenerationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimAcknowledgementOutcomes_ResponseId",
                table: "ClaimAcknowledgementOutcomes",
                column: "ResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimCorrections_AgencyId_BillingPeriodId",
                table: "ClaimCorrections",
                columns: new[] { "AgencyId", "BillingPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimCorrections_BillingPeriodId",
                table: "ClaimCorrections",
                column: "BillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimCorrections_ClaimLineId",
                table: "ClaimCorrections",
                column: "ClaimLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimCorrections_CorrectsEdiGenerationId",
                table: "ClaimCorrections",
                column: "CorrectsEdiGenerationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimCorrections_RequestedByUserId",
                table: "ClaimCorrections",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimCorrectionSubmissions_EdiGenerationId",
                table: "ClaimCorrectionSubmissions",
                column: "EdiGenerationId");

            migrationBuilder.CreateIndex(
                name: "IX_EftDepositRecords_AgencyId_RemittanceDepositId",
                table: "EftDepositRecords",
                columns: new[] { "AgencyId", "RemittanceDepositId" });

            migrationBuilder.CreateIndex(
                name: "IX_EftDepositRecords_RecordedByUserId",
                table: "EftDepositRecords",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EftDepositRecords_RemittanceDepositId",
                table: "EftDepositRecords",
                column: "RemittanceDepositId");

            migrationBuilder.CreateIndex(
                name: "IX_EftDepositRecords_SupersedesRecordId",
                table: "EftDepositRecords",
                column: "SupersedesRecordId",
                unique: true,
                filter: "[SupersedesRecordId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimAcknowledgementOutcomes");

            migrationBuilder.DropTable(
                name: "ClaimCorrectionSubmissions");

            migrationBuilder.DropTable(
                name: "EftDepositRecords");

            migrationBuilder.DropTable(
                name: "ClaimCorrections");

            migrationBuilder.DropColumn(
                name: "PayerClaimControlNumber",
                table: "RemittanceClaimOutcomes");

            migrationBuilder.DropColumn(
                name: "IsCorrection",
                table: "EdiGenerations");
        }
    }
}
