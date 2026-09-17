<#
.SYNOPSIS
    Applies and verifies the counted-service-days migration in hosted Demo.

.DESCRIPTION
    This runner is deliberately narrower than dotnet ef database update. It refuses any
    database except identity-marked SatiDemo, guards every statement on the real schema,
    verifies that anything already present has the expected definition rather than merely
    the expected name, and is transactional and rerunnable. Use -WhatIfOnly first, then run
    normally twice.

    The migration creates one table, dbo.ServiceDayInclusions, and its unique index. It
    alters no existing table and reads no consumer data.

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    ./scripts/Apply-ServiceDayInclusionsMigration.ps1 -AccessToken $token -WhatIfOnly
    ./scripts/Apply-ServiceDayInclusionsMigration.ps1 -AccessToken $token
    ./scripts/Apply-ServiceDayInclusionsMigration.ps1 -AccessToken $token
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AccessToken,

    [string]$SqlServer = 'sati-demo-satilogica-central.database.windows.net',

    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
$migrationId = '20260917203349_AddServiceDayInclusions'
$connectionString = "Server=$SqlServer;Database=SatiDemo;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;"
$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$connection.AccessToken = $AccessToken
$connection.Open()

try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 180
    $command.Parameters.AddWithValue('@migrationId', $migrationId) | Out-Null
    $command.Parameters.AddWithValue('@rollBackOnly', [bool]$WhatIfOnly) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'SatiDemo'
    THROW 52200, 'The connected database is not SatiDemo.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 52201, 'dbo.SatiDatabaseIdentity is missing.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 52202, 'The database identity marker is not Demo.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 52203, 'dbo.Users is missing; this is not the expected Sati schema.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 52204, 'dbo.__EFMigrationsHistory is missing.', 1;
IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId)
   AND OBJECT_ID(N'dbo.ServiceDayInclusions', N'U') IS NULL
    THROW 52205, 'Migration history says counted service days are applied, but the table is missing.', 1;

DECLARE @tableCreated bit = 0;
DECLARE @indexCreated bit = 0;
DECLARE @historyWritten bit = 0;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ServiceDayInclusions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ServiceDayInclusions
    (
        Id int IDENTITY(1,1) NOT NULL,
        UserId int NOT NULL,
        [Date] date NOT NULL,
        IsIncluded bit NOT NULL,
        CONSTRAINT PK_ServiceDayInclusions PRIMARY KEY (Id),
        CONSTRAINT FK_ServiceDayInclusions_Users_UserId FOREIGN KEY (UserId)
            REFERENCES dbo.Users (Id) ON DELETE CASCADE
    );
    SET @tableCreated = 1;
END;

-- Present is not enough: a table of the same name with different columns would make the
-- API write rows the desktop cannot read.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.ServiceDayInclusions')
      AND c.name = N'UserId' AND t.name = N'int' AND c.is_nullable = 0)
    THROW 52206, 'ServiceDayInclusions.UserId has an unexpected definition.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.ServiceDayInclusions')
      AND c.name = N'Date' AND t.name = N'date' AND c.is_nullable = 0)
    THROW 52207, 'ServiceDayInclusions.Date has an unexpected definition.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.ServiceDayInclusions')
      AND c.name = N'IsIncluded' AND t.name = N'bit' AND c.is_nullable = 0)
    THROW 52208, 'ServiceDayInclusions.IsIncluded has an unexpected definition.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.ServiceDayInclusions')
      AND referenced_object_id = OBJECT_ID(N'dbo.Users')
      AND delete_referential_action = 1)
    THROW 52209, 'ServiceDayInclusions is missing its cascading Users foreign key.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ServiceDayInclusions')
      AND name = N'IX_ServiceDayInclusions_UserId_Date')
BEGIN
    CREATE UNIQUE INDEX IX_ServiceDayInclusions_UserId_Date
        ON dbo.ServiceDayInclusions (UserId, [Date]);
    SET @indexCreated = 1;
END;

-- The uniqueness is the point: it is what stops two windows leaving one day with two
-- contradictory answers.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ServiceDayInclusions')
      AND name = N'IX_ServiceDayInclusions_UserId_Date'
      AND is_unique = 1)
    THROW 52210, 'IX_ServiceDayInclusions_UserId_Date exists but is not unique.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId)
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (@migrationId, N'10.0.5');
    SET @historyWritten = 1;
END;

DECLARE @migrationCount bigint = (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory);
DECLARE @rowCount bigint = (SELECT COUNT_BIG(*) FROM dbo.ServiceDayInclusions);
IF @rollBackOnly = 1
    ROLLBACK TRANSACTION;
ELSE
    COMMIT TRANSACTION;

SELECT DB_NAME() AS DatabaseName,
       (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
       @tableCreated AS TableCreated,
       @indexCreated AS IndexCreated,
       @historyWritten AS HistoryWritten,
       @migrationCount AS MigrationCount,
       @rowCount AS InclusionRowCount,
       @rollBackOnly AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The counted-service-days migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName      = $reader.GetString(0)
        EnvironmentName   = $reader.GetString(1)
        TableCreated      = $reader.GetBoolean(2)
        IndexCreated      = $reader.GetBoolean(3)
        HistoryWritten    = $reader.GetBoolean(4)
        MigrationCount    = $reader.GetInt64(5)
        InclusionRowCount = $reader.GetInt64(6)
        RolledBack        = $reader.GetBoolean(7)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
