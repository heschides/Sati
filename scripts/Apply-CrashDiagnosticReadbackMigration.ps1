<#
.SYNOPSIS
    Applies and verifies the crash-diagnostic readback migration in hosted Demo.

.DESCRIPTION
    This runner is deliberately narrower than dotnet ef database update. It refuses any
    database except identity-marked SatiDemo, checks the real schema and migration history,
    and is transactional and rerunnable. Use -WhatIfOnly first, then run normally twice.

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    ./scripts/Apply-CrashDiagnosticReadbackMigration.ps1 -AccessToken $token -WhatIfOnly
    ./scripts/Apply-CrashDiagnosticReadbackMigration.ps1 -AccessToken $token
    ./scripts/Apply-CrashDiagnosticReadbackMigration.ps1 -AccessToken $token
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AccessToken,

    [string]$SqlServer = 'sati-demo-satilogica-central.database.windows.net',

    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
$migrationId = '20260910102153_AddCrashDiagnosticReadback'
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
    THROW 52100, 'The connected database is not SatiDemo.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 52101, 'dbo.SatiDatabaseIdentity is missing.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 52102, 'The database identity marker is not Demo.', 1;
IF OBJECT_ID(N'dbo.IncidentGroups', N'U') IS NULL
    THROW 52103, 'dbo.IncidentGroups is missing; this is not the expected Sati schema.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 52104, 'dbo.__EFMigrationsHistory is missing.', 1;
IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId)
   AND COL_LENGTH(N'dbo.IncidentGroups', N'LastCrashDiagnosticJson') IS NULL
    THROW 52105, 'Migration history says crash diagnostic readback is applied, but the column is missing.', 1;

DECLARE @columnAdded bit = 0;
DECLARE @historyWritten bit = 0;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.IncidentGroups', N'LastCrashDiagnosticJson') IS NULL
BEGIN
    ALTER TABLE dbo.IncidentGroups
        ADD LastCrashDiagnosticJson nvarchar(2000) NULL;
    SET @columnAdded = 1;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.IncidentGroups')
      AND c.name = N'LastCrashDiagnosticJson'
      AND t.name = N'nvarchar'
      AND c.max_length = 4000
      AND c.is_nullable = 1
      AND c.is_computed = 0
      AND c.is_identity = 0)
    THROW 52106, 'IncidentGroups.LastCrashDiagnosticJson has an unexpected definition.', 1;

IF EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.IncidentGroups')
      AND c.name = N'LastCrashDiagnosticJson')
    THROW 52107, 'IncidentGroups.LastCrashDiagnosticJson unexpectedly has a default constraint.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId)
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (@migrationId, N'10.0.5');
    SET @historyWritten = 1;
END;

DECLARE @migrationCount bigint = (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory);
IF @rollBackOnly = 1
    ROLLBACK TRANSACTION;
ELSE
    COMMIT TRANSACTION;

SELECT DB_NAME() AS DatabaseName,
       (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
       @columnAdded AS ColumnAdded,
       @historyWritten AS HistoryWritten,
       @migrationCount AS MigrationCount,
       @rollBackOnly AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The crash-diagnostic migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName    = $reader.GetString(0)
        EnvironmentName = $reader.GetString(1)
        ColumnAdded     = $reader.GetBoolean(2)
        HistoryWritten  = $reader.GetBoolean(3)
        MigrationCount  = $reader.GetInt64(4)
        RolledBack      = $reader.GetBoolean(5)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
