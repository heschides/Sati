using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddTestConsumerMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTestData",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Every record in the isolated Demo environment is synthetic by design.
            // Existing Production/local records remain false because their purpose
            // cannot be inferred safely from a name or date.
            migrationBuilder.Sql("""
                IF DB_NAME() = N'SatiDemo'
                   AND OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NOT NULL
                BEGIN
                    -- Keep the optional-table reference inside dynamic SQL. SQL
                    -- Server resolves object names for the whole batch before it
                    -- evaluates the OBJECT_ID guard, so a direct reference makes
                    -- clean-database migrations fail when this operational table
                    -- has not been provisioned yet.
                    EXEC sp_executesql N'
                        IF EXISTS
                        (
                            SELECT 1
                            FROM dbo.SatiDatabaseIdentity
                            WHERE Id = 1 AND EnvironmentName = N''Demo''
                        )
                            UPDATE dbo.People SET IsTestData = 1;';
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTestData",
                table: "People");
        }
    }
}
