using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackReleaseAttestationRevocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RevocationReason",
                table: "ReleaseObligationAttestations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAtUtc",
                table: "ReleaseObligationAttestations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevokedByUserId",
                table: "ReleaseObligationAttestations",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevocationReason",
                table: "ReleaseObligationAttestations");

            migrationBuilder.DropColumn(
                name: "RevokedAtUtc",
                table: "ReleaseObligationAttestations");

            migrationBuilder.DropColumn(
                name: "RevokedByUserId",
                table: "ReleaseObligationAttestations");
        }
    }
}
