using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClearinghouseDispatchRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClearinghouseAgencyDispatchRotation",
                columns: table => new
                {
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    LastAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearinghouseAgencyDispatchRotation", x => x.AgencyId);
                    table.CheckConstraint("CK_ClearinghouseAgencyDispatchRotation_Scope", "[AgencyId] > 0 AND [Revision] > 0");
                    table.ForeignKey(
                        name: "FK_ClearinghouseAgencyDispatchRotation_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClearinghouseDispatchRotation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LastAgencyId = table.Column<int>(type: "int", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearinghouseDispatchRotation", x => x.Id);
                    table.CheckConstraint("CK_ClearinghouseDispatchRotation_Scope", "[Id] = 1 AND [Revision] > 0 AND ([LastAgencyId] IS NULL OR [LastAgencyId] > 0)");
                });

            migrationBuilder.InsertData(
                table: "ClearinghouseDispatchRotation",
                columns: new[] { "Id", "LastAgencyId", "Revision" },
                values: new object[] { 1, null, 1L });

            migrationBuilder.CreateIndex(
                name: "IX_ClearinghouseDispatches_QueuedLane",
                table: "ClearinghouseDispatches",
                columns: new[] { "AgencyId", "AccountId", "RequestedAtUtc", "Id" },
                filter: "[State] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.ClearinghouseAgencyDispatchRotation) OR " +
                "EXISTS (SELECT 1 FROM dbo.ClearinghouseDispatchRotation WHERE Revision <> 1 OR LastAgencyId IS NOT NULL) " +
                "THROW 51045, 'Pause dispatch and review retained scheduling position before rollback.', 1;");
            migrationBuilder.DropTable(
                name: "ClearinghouseAgencyDispatchRotation");

            migrationBuilder.DropTable(
                name: "ClearinghouseDispatchRotation");

            migrationBuilder.DropIndex(
                name: "IX_ClearinghouseDispatches_QueuedLane",
                table: "ClearinghouseDispatches");

        }
    }
}
