using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayerBillingConfigurationVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayerBillingConfigurationVersions",
                columns: table => new
                {
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    ProfileKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveOn = table.Column<DateTime>(type: "date", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayerBillingConfigurationVersions", x => x.VersionId);
                    table.ForeignKey(
                        name: "FK_PayerBillingConfigurationVersions_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayerBillingConfigurationVersions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayerBillingConfigurationVersions_AgencyId_ProfileKey_EffectiveOn",
                table: "PayerBillingConfigurationVersions",
                columns: new[] { "AgencyId", "ProfileKey", "EffectiveOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayerBillingConfigurationVersions_AgencyId_ProfileKey_Revision",
                table: "PayerBillingConfigurationVersions",
                columns: new[] { "AgencyId", "ProfileKey", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayerBillingConfigurationVersions_CreatedByUserId",
                table: "PayerBillingConfigurationVersions",
                column: "CreatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.PayerBillingConfigurationVersions)
                    OR EXISTS (SELECT 1 FROM dbo.ClaimLines
                        WHERE TRY_CONVERT(int, JSON_VALUE(CASE WHEN ISJSON(ClaimSnapshotJson)=1
                            THEN ClaimSnapshotJson ELSE N'{}' END, '$.Version')) >= 2)
                    OR EXISTS (SELECT 1 FROM dbo.ClaimCorrections
                        WHERE TRY_CONVERT(int, JSON_VALUE(CASE WHEN ISJSON(ClaimSnapshotJson)=1
                            THEN ClaimSnapshotJson ELSE N'{}' END, '$.Version')) >= 2)
                    THROW 51032, 'Payer configuration history exists. Restore the matching application; do not downgrade or delete financial provenance.', 1;
                """);
            migrationBuilder.DropTable(
                name: "PayerBillingConfigurationVersions");
        }
    }
}
