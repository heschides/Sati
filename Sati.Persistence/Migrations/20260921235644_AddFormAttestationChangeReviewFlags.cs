using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormAttestationChangeReviewFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormAttestationChangeReviewFlags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FlagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    NoteId = table.Column<int>(type: "int", nullable: false),
                    FormId = table.Column<int>(type: "int", nullable: false),
                    ClaimLineId = table.Column<int>(type: "int", nullable: true),
                    NoteActivityDate = table.Column<DateTime>(type: "date", nullable: true),
                    DueDate = table.Column<DateTime>(type: "date", nullable: false),
                    PreviousCompletedOn = table.Column<DateTime>(type: "date", nullable: true),
                    RevisedCompletedOn = table.Column<DateTime>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequiresSupervisorAttention = table.Column<bool>(type: "bit", nullable: false),
                    RequiresBillingAttention = table.Column<bool>(type: "bit", nullable: false),
                    BillingHoldReasons = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAttestationChangeReviewFlags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormAttestationChangeReviewFlags_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FormAttestationChangeReviewFlags_ClaimLines_ClaimLineId",
                        column: x => x.ClaimLineId,
                        principalTable: "ClaimLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FormAttestationChangeReviewFlags_Forms_FormId",
                        column: x => x.FormId,
                        principalTable: "Forms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FormAttestationChangeReviewFlags_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FormAttestationChangeReviewFlags_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormAttestationChangeReviewFlags_AgencyId_CreatedAtUtc",
                table: "FormAttestationChangeReviewFlags",
                columns: new[] { "AgencyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FormAttestationChangeReviewFlags_AgencyId_RequiresBillingAttention_CreatedAtUtc",
                table: "FormAttestationChangeReviewFlags",
                columns: new[] { "AgencyId", "RequiresBillingAttention", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FormAttestationChangeReviewFlags_AgencyId_RequiresSupervisorAttention_CreatedAtUtc",
                table: "FormAttestationChangeReviewFlags",
                columns: new[] { "AgencyId", "RequiresSupervisorAttention", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FormAttestationChangeReviewFlags_ClaimLineId",
                table: "FormAttestationChangeReviewFlags",
                column: "ClaimLineId");

            migrationBuilder.CreateIndex(
                name: "IX_FormAttestationChangeReviewFlags_FlagId",
                table: "FormAttestationChangeReviewFlags",
                column: "FlagId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormAttestationChangeReviewFlags_FormId",
                table: "FormAttestationChangeReviewFlags",
                column: "FormId");

            migrationBuilder.CreateIndex(
                name: "IX_FormAttestationChangeReviewFlags_NoteId",
                table: "FormAttestationChangeReviewFlags",
                column: "NoteId");

            migrationBuilder.CreateIndex(
                name: "IX_FormAttestationChangeReviewFlags_PersonId",
                table: "FormAttestationChangeReviewFlags",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormAttestationChangeReviewFlags");
        }
    }
}
