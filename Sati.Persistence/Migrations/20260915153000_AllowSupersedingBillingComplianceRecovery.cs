using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

/// <summary>
/// A recovery decision is immutable and covers only the blocker evidence known
/// when it was recorded. A later effective-dated policy or corrected historical
/// fact may therefore require a second decision for the same note.
/// </summary>
[DbContext(typeof(SatiContext))]
[Migration("20260915153000_AllowSupersedingBillingComplianceRecovery")]
public partial class AllowSupersedingBillingComplianceRecovery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BillingComplianceRecoveryNotes_NoteId",
            table: "BillingComplianceRecoveryNotes");

        migrationBuilder.CreateIndex(
            name: "IX_BillingComplianceRecoveryNotes_NoteId",
            table: "BillingComplianceRecoveryNotes",
            column: "NoteId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BillingComplianceRecoveryNotes_NoteId",
            table: "BillingComplianceRecoveryNotes");

        migrationBuilder.CreateIndex(
            name: "IX_BillingComplianceRecoveryNotes_NoteId",
            table: "BillingComplianceRecoveryNotes",
            column: "NoteId",
            unique: true);
    }
}
