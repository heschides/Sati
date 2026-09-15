using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingCompliancePolicyReviewFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingCompliancePolicyReviewFlags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FlagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    PolicyVersionId = table.Column<long>(type: "bigint", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    NoteId = table.Column<int>(type: "int", nullable: false),
                    ClaimLineId = table.Column<int>(type: "int", nullable: true),
                    RecordKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ServiceDate = table.Column<DateTime>(type: "date", nullable: false),
                    ChangeKind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PreviousBlockingObligationIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NewBlockingObligationIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCompliancePolicyReviewFlags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingCompliancePolicyReviewFlags_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingCompliancePolicyReviewFlags_BillingCompliancePolicyVersions_PolicyVersionId",
                        column: x => x.PolicyVersionId,
                        principalTable: "BillingCompliancePolicyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingCompliancePolicyReviewFlags_ClaimLines_ClaimLineId",
                        column: x => x.ClaimLineId,
                        principalTable: "ClaimLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingCompliancePolicyReviewFlags_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingCompliancePolicyReviewFlags_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCompliancePolicyReviewFlags_AgencyId_CreatedAtUtc",
                table: "BillingCompliancePolicyReviewFlags",
                columns: new[] { "AgencyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCompliancePolicyReviewFlags_ClaimLineId",
                table: "BillingCompliancePolicyReviewFlags",
                column: "ClaimLineId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCompliancePolicyReviewFlags_FlagId",
                table: "BillingCompliancePolicyReviewFlags",
                column: "FlagId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCompliancePolicyReviewFlags_NoteId",
                table: "BillingCompliancePolicyReviewFlags",
                column: "NoteId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCompliancePolicyReviewFlags_PersonId",
                table: "BillingCompliancePolicyReviewFlags",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCompliancePolicyReviewFlags_PolicyVersionId_RecordKey",
                table: "BillingCompliancePolicyReviewFlags",
                columns: new[] { "PolicyVersionId", "RecordKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingCompliancePolicyReviewFlags");
        }
    }
}
