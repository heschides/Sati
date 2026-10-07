using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordsGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecordsGovernanceStates",
                columns: table => new
                {
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordsGovernanceStates", x => x.AgencyId);
                    table.ForeignKey(
                        name: "FK_RecordsGovernanceStates_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecordsHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    RecordClass = table.Column<int>(type: "int", nullable: true),
                    PersonId = table.Column<int>(type: "int", nullable: true),
                    RecordId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    IsReleased = table.Column<bool>(type: "bit", nullable: false),
                    PlacedById = table.Column<int>(type: "int", nullable: false),
                    ReleaseRequestedById = table.Column<int>(type: "int", nullable: true),
                    ReleaseRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LegacyHoldId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordsHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecordsHolds_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecordsHolds_Users_PlacedById",
                        column: x => x.PlacedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecordsRetentionPolicies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    RecordClass = table.Column<int>(type: "int", nullable: false),
                    RetentionDays = table.Column<int>(type: "int", nullable: true),
                    AuthorId = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordsRetentionPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecordsRetentionPolicies_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecordsRetentionPolicies_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecordsHoldEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HoldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CaseReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IssuedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordsHoldEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecordsHoldEvents_RecordsHolds_HoldId",
                        column: x => x.HoldId,
                        principalTable: "RecordsHolds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecordsHoldEvents_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecordsRetentionPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    PolicyId = table.Column<long>(type: "bigint", nullable: false),
                    GovernanceRevision = table.Column<int>(type: "int", nullable: false),
                    PreparedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreviewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CandidatesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Checkpoint = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Completed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordsRetentionPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecordsRetentionPlans_RecordsRetentionPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "RecordsRetentionPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecordsRetentionBatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Checkpoint = table.Column<int>(type: "int", nullable: false),
                    DeletedCount = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreservationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Completed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordsRetentionBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecordsRetentionBatches_RecordsRetentionPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "RecordsRetentionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecordsRetentionBatches_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecordsHoldEvents_ActorId",
                table: "RecordsHoldEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordsHoldEvents_AgencyId_OperationId",
                table: "RecordsHoldEvents",
                columns: new[] { "AgencyId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordsHoldEvents_HoldId_Revision",
                table: "RecordsHoldEvents",
                columns: new[] { "HoldId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordsHolds_AgencyId_IsReleased",
                table: "RecordsHolds",
                columns: new[] { "AgencyId", "IsReleased" });

            migrationBuilder.CreateIndex(
                name: "IX_RecordsHolds_AgencyId_LegacyHoldId",
                table: "RecordsHolds",
                columns: new[] { "AgencyId", "LegacyHoldId" },
                unique: true,
                filter: "[LegacyHoldId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecordsHolds_PlacedById",
                table: "RecordsHolds",
                column: "PlacedById");

            migrationBuilder.CreateIndex(
                name: "IX_RecordsRetentionBatches_ActorId",
                table: "RecordsRetentionBatches",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordsRetentionBatches_AgencyId_OperationId",
                table: "RecordsRetentionBatches",
                columns: new[] { "AgencyId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordsRetentionBatches_PlanId_Checkpoint",
                table: "RecordsRetentionBatches",
                columns: new[] { "PlanId", "Checkpoint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordsRetentionPlans_PolicyId",
                table: "RecordsRetentionPlans",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordsRetentionPolicies_AgencyId_OperationId",
                table: "RecordsRetentionPolicies",
                columns: new[] { "AgencyId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordsRetentionPolicies_AgencyId_RecordClass_Version",
                table: "RecordsRetentionPolicies",
                columns: new[] { "AgencyId", "RecordClass", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordsRetentionPolicies_AuthorId",
                table: "RecordsRetentionPolicies",
                column: "AuthorId");
            // Preserve every active legacy person hold and its original placement evidence.
            // Legacy rows (including already released history) remain unchanged.
            migrationBuilder.Sql("""
                INSERT dbo.RecordsGovernanceStates(AgencyId, Revision)
                SELECT DISTINCT AgencyId, 1 FROM dbo.LegalHolds WHERE IsReleased=0;
                INSERT dbo.RecordsHolds(Id, AgencyId, Revision, Scope, RecordClass, PersonId, RecordId,
                    IsReleased, PlacedById, ReleaseRequestedById, ReleaseRequestId, LegacyHoldId)
                SELECT NEWID(), AgencyId, 1, 1, NULL, PersonId, NULL, 0, PlacedByUserId, NULL, NULL, Id
                    FROM dbo.LegalHolds WHERE IsReleased=0;
                INSERT dbo.RecordsHoldEvents(HoldId, AgencyId, OperationId, RequestHash, Revision, Action,
                    ActorId, RecordedAtUtc, Reason, CaseReference, IssuedBy)
                SELECT h.Id, h.AgencyId, NEWID(), REPLICATE(N'0',64), 1, 0, l.PlacedByUserId,
                    l.PlacedAtUtc, l.Reason, l.CaseReference, l.IssuedBy
                    FROM dbo.RecordsHolds h JOIN dbo.LegalHolds l ON l.Id=h.LegacyHoldId AND l.AgencyId=h.AgencyId;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.RecordsHolds) OR EXISTS (SELECT 1 FROM dbo.RecordsHoldEvents)
                    OR EXISTS (SELECT 1 FROM dbo.RecordsRetentionPolicies) OR EXISTS (SELECT 1 FROM dbo.RecordsRetentionPlans)
                    OR EXISTS (SELECT 1 FROM dbo.RecordsRetentionBatches)
                    THROW 53804, 'Governance evidence exists; rollback requires a reviewed preservation plan.', 1;
                """);
            migrationBuilder.DropTable(
                name: "RecordsGovernanceStates");

            migrationBuilder.DropTable(
                name: "RecordsHoldEvents");

            migrationBuilder.DropTable(
                name: "RecordsRetentionBatches");

            migrationBuilder.DropTable(
                name: "RecordsHolds");

            migrationBuilder.DropTable(
                name: "RecordsRetentionPlans");

            migrationBuilder.DropTable(
                name: "RecordsRetentionPolicies");
        }
    }
}
