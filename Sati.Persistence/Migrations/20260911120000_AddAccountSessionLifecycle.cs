using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    public partial class AddAccountSessionLifecycle : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing accounts stay enabled. Missing-version credentials are rejected
            // by the application, so all pre-upgrade sessions must sign in again.
            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled", table: "Users", type: "bit",
                nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<long>(
                name: "SecurityVersion", table: "Users", type: "bigint",
                nullable: false, defaultValue: 1L);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back removes revocation state. This is not a safe live downgrade:
            // take the application offline and review account access before any rollback.
            migrationBuilder.DropColumn(name: "SecurityVersion", table: "Users");
            migrationBuilder.DropColumn(name: "IsEnabled", table: "Users");
        }
    }
}
