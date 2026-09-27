using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalSignatureEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInternalElectronicSignatureEnabled",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ExternalSignatureEvidence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    DocumentArtifactId = table.Column<int>(type: "int", nullable: false),
                    ReleaseObligationId = table.Column<long>(type: "bigint", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SignedOn = table.Column<DateTime>(type: "date", nullable: false),
                    SignerName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SignerCapacity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AttestedByUserId = table.Column<int>(type: "int", nullable: false),
                    AttestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AttestationText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    BlobPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ContentSha256 = table.Column<string>(type: "char(64)", nullable: false),
                    ByteCount = table.Column<long>(type: "bigint", nullable: false),
                    VerificationNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalSignatureEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalSignatureEvidence_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalSignatureEvidence_DocumentArtifacts_DocumentArtifactId",
                        column: x => x.DocumentArtifactId,
                        principalTable: "DocumentArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalSignatureEvidence_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalSignatureEvidence_ReleaseObligations_ReleaseObligationId",
                        column: x => x.ReleaseObligationId,
                        principalTable: "ReleaseObligations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalSignatureEvidence_Users_AttestedByUserId",
                        column: x => x.AttestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalSignatureEvidence_AgencyId_ClientRequestId",
                table: "ExternalSignatureEvidence",
                columns: new[] { "AgencyId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalSignatureEvidence_AttestedByUserId",
                table: "ExternalSignatureEvidence",
                column: "AttestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalSignatureEvidence_DocumentArtifactId",
                table: "ExternalSignatureEvidence",
                column: "DocumentArtifactId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalSignatureEvidence_PersonId",
                table: "ExternalSignatureEvidence",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalSignatureEvidence_ReleaseObligationId",
                table: "ExternalSignatureEvidence",
                column: "ReleaseObligationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalSignatureEvidence");

            migrationBuilder.DropColumn(
                name: "IsInternalElectronicSignatureEnabled",
                table: "Settings");
        }
    }
}
