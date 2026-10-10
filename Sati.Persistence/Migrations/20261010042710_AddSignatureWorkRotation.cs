using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureWorkRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignatureWorkRotation",
                columns: table => new
                {
                    WorkKind = table.Column<int>(type: "int", nullable: false),
                    LastAgencyId = table.Column<int>(type: "int", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureWorkRotation", x => x.WorkKind);
                    table.CheckConstraint("CK_SignatureWorkRotation_Scope", "[WorkKind] IN (1,2,3) AND [Revision] > 0 AND ([LastAgencyId] IS NULL OR [LastAgencyId] > 0)");
                });

            migrationBuilder.CreateTable(
                name: "SignatureAgencyWorkRotation",
                columns: table => new
                {
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    WorkKind = table.Column<int>(type: "int", nullable: false),
                    LastItemId = table.Column<long>(type: "bigint", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureAgencyWorkRotation", x => new { x.AgencyId, x.WorkKind });
                    table.CheckConstraint("CK_SignatureAgencyWorkRotation_Scope", "[AgencyId] > 0 AND [WorkKind] IN (1,2,3) AND [Revision] > 0 AND ([LastItemId] IS NULL OR [LastItemId] > 0)");
                    table.ForeignKey(
                        name: "FK_SignatureAgencyWorkRotation_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SignatureAgencyWorkRotation_SignatureWorkRotation_WorkKind",
                        column: x => x.WorkKind,
                        principalTable: "SignatureWorkRotation",
                        principalColumn: "WorkKind",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "SignatureWorkRotation",
                columns: new[] { "WorkKind", "LastAgencyId", "Revision" },
                values: new object[,]
                {
                    { 1, null, 1L },
                    { 2, null, 1L },
                    { 3, null, 1L }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureOutbox_AgencyId_Id",
                table: "SignatureOutbox",
                columns: new[] { "AgencyId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureCompletions_AgencyId_Id",
                table: "SignatureCompletions",
                columns: new[] { "AgencyId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureAgencyWorkRotation_WorkKind",
                table: "SignatureAgencyWorkRotation",
                column: "WorkKind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.SignatureAgencyWorkRotation) OR " +
                "(SELECT COUNT(*) FROM dbo.SignatureWorkRotation WHERE WorkKind IN (1,2,3) AND LastAgencyId IS NULL AND Revision = 1) <> 3 " +
                "THROW 51047, 'Signature rotation was used; review rollback before discarding scheduling position.', 1;");

            migrationBuilder.DropTable(
                name: "SignatureAgencyWorkRotation");

            migrationBuilder.DropTable(
                name: "SignatureWorkRotation");

            migrationBuilder.DropIndex(
                name: "IX_SignatureOutbox_AgencyId_Id",
                table: "SignatureOutbox");

            migrationBuilder.DropIndex(
                name: "IX_SignatureCompletions_AgencyId_Id",
                table: "SignatureCompletions");
        }
    }
}
