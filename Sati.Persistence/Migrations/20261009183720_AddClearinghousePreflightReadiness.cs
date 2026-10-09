using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClearinghousePreflightReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClearinghouseDispatchReadiness",
                columns: table => new
                {
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Disposition = table.Column<int>(type: "int", nullable: false),
                    FailureCount = table.Column<int>(type: "int", nullable: false),
                    RecoveryCycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NextEligibleAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastFailureAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SafeFailureCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ValidatedAccountRevision = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearinghouseDispatchReadiness", x => new { x.AgencyId, x.AccountId });
                    table.CheckConstraint("CK_ClearinghouseDispatchReadiness_State", "[AgencyId] > 0 AND [Revision] > 0 AND [ValidatedAccountRevision] >= 0 AND (([Disposition] = 1 AND [FailureCount] = 0 AND [RecoveryCycleId] = '00000000-0000-0000-0000-000000000000' AND [NextEligibleAtUtc] IS NULL AND [LastFailureAtUtc] IS NULL AND [SafeFailureCode] IS NULL) OR ([Disposition] = 2 AND [FailureCount] BETWEEN 1 AND 4 AND [RecoveryCycleId] <> '00000000-0000-0000-0000-000000000000' AND [NextEligibleAtUtc] IS NOT NULL AND [LastFailureAtUtc] IS NOT NULL AND [NextEligibleAtUtc] > [LastFailureAtUtc] AND [SafeFailureCode] IS NOT NULL AND [SafeFailureCode] = 'account_key_unavailable') OR ([Disposition] = 3 AND [FailureCount] = 5 AND [RecoveryCycleId] <> '00000000-0000-0000-0000-000000000000' AND [NextEligibleAtUtc] IS NULL AND [LastFailureAtUtc] IS NOT NULL AND [SafeFailureCode] IS NOT NULL AND [SafeFailureCode] = 'account_key_unavailable'))");
                    table.ForeignKey(
                        name: "FK_ClearinghouseDispatchReadiness_ClearinghouseAccounts_AgencyId_AccountId",
                        columns: x => new { x.AgencyId, x.AccountId },
                        principalTable: "ClearinghouseAccounts",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseDispatches_State_RequestedAtUtc_Id",
                table: "ClearinghouseDispatches",
                columns: new[] { "State", "RequestedAtUtc", "Id" })
                .Annotation("SqlServer:Include", new[] { "AgencyId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseDispatchReadiness_Disposition_NextEligibleAtUtc",
                table: "ClearinghouseDispatchReadiness",
                columns: new[] { "Disposition", "NextEligibleAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.ClearinghouseDispatchReadiness WHERE Disposition <> 1) " +
                "THROW 51044, 'Pause dispatch and reconcile unresolved account readiness before rollback.', 1;");
            migrationBuilder.DropTable(
                name: "ClearinghouseDispatchReadiness");

            migrationBuilder.DropIndex(
                name: "IX_ClearinghouseDispatches_State_RequestedAtUtc_Id",
                table: "ClearinghouseDispatches");
        }
    }
}
