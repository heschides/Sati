using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssessmentReviewCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssessmentSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    AssessmentId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    AuthorUserId = table.Column<int>(type: "int", nullable: false),
                    AssessmentVersion = table.Column<int>(type: "int", nullable: false),
                    CycleNumber = table.Column<int>(type: "int", nullable: false),
                    DocumentRevision = table.Column<int>(type: "int", nullable: false),
                    FormId = table.Column<int>(type: "int", nullable: false),
                    TargetEffectiveDate = table.Column<DateTime>(type: "date", nullable: false),
                    DueDate = table.Column<DateTime>(type: "date", nullable: false),
                    RulesVersion = table.Column<int>(type: "int", nullable: false),
                    ContentSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DocumentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConsumerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentSubmissions_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessmentSubmissions_ComprehensiveAssessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "ComprehensiveAssessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessmentSubmissions_Forms_FormId",
                        column: x => x.FormId,
                        principalTable: "Forms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessmentSubmissions_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessmentSubmissions_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentReviewEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    AssessmentId = table.Column<int>(type: "int", nullable: false),
                    SubmissionId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Blocking = table.Column<bool>(type: "bit", nullable: false),
                    FlagId = table.Column<long>(type: "bigint", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssessmentRevision = table.Column<int>(type: "int", nullable: false),
                    ArtifactId = table.Column<int>(type: "int", nullable: true),
                    CompletedOn = table.Column<DateTime>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentReviewEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentReviewEvents_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessmentReviewEvents_AssessmentReviewEvents_FlagId",
                        column: x => x.FlagId,
                        principalTable: "AssessmentReviewEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessmentReviewEvents_AssessmentSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "AssessmentSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessmentReviewEvents_ComprehensiveAssessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "ComprehensiveAssessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessmentReviewEvents_DocumentArtifacts_ArtifactId",
                        column: x => x.ArtifactId,
                        principalTable: "DocumentArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessmentReviewEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentReviewEvents_ActorUserId",
                table: "AssessmentReviewEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentReviewEvents_AgencyId",
                table: "AssessmentReviewEvents",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentReviewEvents_ArtifactId",
                table: "AssessmentReviewEvents",
                column: "ArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentReviewEvents_AssessmentId_AssessmentRevision",
                table: "AssessmentReviewEvents",
                columns: new[] { "AssessmentId", "AssessmentRevision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentReviewEvents_FlagId",
                table: "AssessmentReviewEvents",
                column: "FlagId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentReviewEvents_SubmissionId",
                table: "AssessmentReviewEvents",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentSubmissions_AgencyId_SubmittedAtUtc",
                table: "AssessmentSubmissions",
                columns: new[] { "AgencyId", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentSubmissions_AssessmentId_CycleNumber",
                table: "AssessmentSubmissions",
                columns: new[] { "AssessmentId", "CycleNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentSubmissions_AuthorUserId",
                table: "AssessmentSubmissions",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentSubmissions_FormId",
                table: "AssessmentSubmissions",
                column: "FormId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentSubmissions_PersonId",
                table: "AssessmentSubmissions",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.AssessmentSubmissions) OR EXISTS (SELECT 1 FROM dbo.AssessmentReviewEvents)
                    THROW 53814, 'Assessment review evidence exists; refusing a lossy downgrade.', 1;
                """);
            migrationBuilder.DropTable(
                name: "AssessmentReviewEvents");

            migrationBuilder.DropTable(
                name: "AssessmentSubmissions");
        }
    }
}
