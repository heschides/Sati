<#
.SYNOPSIS
    Applies migration 121 to the identity-marked SatiDemo database.
.DESCRIPTION
    Verifies the reviewed EF migration and SQL, the exact migration boundary,
    and any columns already present in the live schema. Run -PreflightOnly,
    then -WhatIfOnly, then apply, and rerun to prove idempotency. The script
    never changes a firewall rule or prints the Azure SQL access token.
#>
[CmdletBinding()]
param(
    [switch]$PreflightOnly,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
if ($PreflightOnly -and $WhatIfOnly) {
    throw 'Choose only one of -PreflightOnly and -WhatIfOnly.'
}

$migrationPath = Join-Path $PSScriptRoot '..\Sati.Persistence\Migrations\20260928140919_AddAnnualPcpAndUnbilledNotes.cs'
$sqlPath = Join-Path $PSScriptRoot 'Apply-AnnualPcpAndUnbilledNotesMigration.generated.sql'

function Get-ReviewedHash {
    param([string]$Path)
    # Git may check out text with CRLF on Windows. Pin content independent of
    # line endings, while still detecting any change to the migration or SQL.
    $contents = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path))
    $contents = $contents.Replace(([string][char]13 + [char]10), ([string][char]10))
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($contents)
        return [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '')
    }
    finally { $sha.Dispose() }
}

if ((Get-ReviewedHash $migrationPath) -ne
    '9EACB25AFCE09CB191DD1651BCE223C132C68CBBF73C0CE560266D36E05DC5BD' -or
    (Get-ReviewedHash $sqlPath) -ne
    '89D82DDC4038F5EB550B9F6B38F73830AE6529A44270932DD70257F2D0333B37') {
    throw 'The reviewed migration or EF SQL has changed. Regenerate, review, and repin before proceeding.'
}

$token = & az account get-access-token --resource 'https://database.windows.net/' --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
    throw 'Azure SQL access token is unavailable. Sign in to the expected Azure account.'
}
$connection = New-Object System.Data.SqlClient.SqlConnection(
    'Server=sati-demo-satilogica-central.database.windows.net;Database=SatiDemo;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;')
$connection.AccessToken = $token.Trim()
$token = $null

function Invoke-Sql {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [System.Data.SqlClient.SqlTransaction]$Transaction,
        [string]$Sql,
        [switch]$Scalar
    )
    $command = $Connection.CreateCommand()
    $command.CommandTimeout = 180
    if ($Transaction) { $command.Transaction = $Transaction }
    $command.CommandText = $Sql
    try {
        if ($Scalar) { return $command.ExecuteScalar() }
        $command.ExecuteNonQuery() | Out-Null
    }
    finally { $command.Dispose() }
}

$identitySql = @'
SET NOCOUNT ON;
IF DB_NAME() <> N'SatiDemo'
    THROW 53100, 'Connected database is not SatiDemo.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 53101, 'Demo identity table is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity
               WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 53102, 'Database identity is not Demo.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL OR
   OBJECT_ID(N'dbo.Notes', N'U') IS NULL
    THROW 53103, 'A prerequisite Sati table is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
               WHERE MigrationId = N'20260927232039_SupportDurableOneOffReleases')
    THROW 53104, 'The preceding migration is not recorded.', 1;
SELECT COUNT(*) FROM dbo.__EFMigrationsHistory
WHERE MigrationId = N'20260928140919_AddAnnualPcpAndUnbilledNotes';
'@

$columnShapeSql = @'
SET NOCOUNT ON;
IF EXISTS (
    SELECT 1 FROM (VALUES (N'IsAnnualPlan'), (N'IsUnbilled')) AS expected(ColumnName)
    JOIN sys.columns AS c ON c.object_id = OBJECT_ID(N'dbo.Notes')
                         AND c.name = expected.ColumnName
    LEFT JOIN sys.types AS t ON t.user_type_id = c.user_type_id
    LEFT JOIN sys.default_constraints AS d ON d.object_id = c.default_object_id
    WHERE t.name <> N'bit' OR c.max_length <> 1 OR c.is_nullable <> 0 OR
          d.object_id IS NULL OR
          LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(d.definition,
              N'(', N''), N')', N''), N'[', N''), N']', N''), N' ', N''))
              NOT IN (N'0', N'cast0asbit', N'convertbit,0'))
    THROW 53105, 'An existing note column has the wrong type, nullability, or zero default.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.columns AS c JOIN sys.types AS t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.Notes') AND c.name = N'FormType'
      AND t.name = N'int' AND c.is_nullable = 1) OR
   NOT EXISTS (
    SELECT 1 FROM sys.columns AS c JOIN sys.types AS t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.Notes') AND c.name = N'FormId'
      AND t.name = N'int' AND c.is_nullable = 1)
    THROW 53106, 'The predecessor Notes form-link columns have an unexpected shape.', 1;
'@

$beforeSql = @'
SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 120 OR
   (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC)
       <> N'20260927232039_SupportDurableOneOffReleases'
    THROW 53107, 'Demo is not at the reviewed 120-migration predecessor boundary.', 1;
'@

$afterSql = @'
SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 121 OR
   (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC)
       <> N'20260928140919_AddAnnualPcpAndUnbilledNotes' OR
   (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20260928140919_AddAnnualPcpAndUnbilledNotes') <> 1
    THROW 53108, 'Demo did not reach the reviewed 121-migration boundary.', 1;
IF COL_LENGTH(N'dbo.Notes', N'IsAnnualPlan') IS NULL OR
   COL_LENGTH(N'dbo.Notes', N'IsUnbilled') IS NULL
    THROW 53109, 'An annual-PCP or unbilled note column is missing.', 1;
IF EXISTS (SELECT 1 FROM dbo.Notes
           WHERE FormType = 4 AND FormId IS NOT NULL AND IsAnnualPlan = 0)
    THROW 53110, 'An existing linked PCP note was not marked annual.', 1;
'@

try {
    $connection.Open()
    $applied = [int](Invoke-Sql -Connection $connection -Sql $identitySql -Scalar)
    if ($applied -notin @(0, 1)) {
        throw 'The annual-PCP migration history row is duplicated.'
    }
    Invoke-Sql -Connection $connection -Sql $columnShapeSql
    if ($applied -eq 0) {
        Invoke-Sql -Connection $connection -Sql $beforeSql
        Write-Host 'Identity-checked SatiDemo is at the 120-migration predecessor boundary.'
    }
    else {
        Invoke-Sql -Connection $connection -Sql $afterSql
        Write-Host 'Identity-checked SatiDemo already has migration 121 and the expected schema.'
    }
    if ($PreflightOnly) { return }
    if ($applied -eq 1) {
        Write-Host 'No changes required. Idempotency check passed.'
        return
    }

    $transaction = $connection.BeginTransaction()
    try {
        $inside = [int](Invoke-Sql -Connection $connection -Transaction $transaction -Sql $identitySql -Scalar)
        if ($inside -ne 0) { throw 'Migration history changed after preflight.' }
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $beforeSql
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $columnShapeSql

        Invoke-Sql -Connection $connection -Transaction $transaction -Sql @'
IF COL_LENGTH(N'dbo.Notes', N'IsAnnualPlan') IS NULL
    EXEC(N'ALTER TABLE dbo.Notes ADD IsAnnualPlan bit NOT NULL DEFAULT CAST(0 AS bit)');
'@
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql @'
IF COL_LENGTH(N'dbo.Notes', N'IsUnbilled') IS NULL
    EXEC(N'ALTER TABLE dbo.Notes ADD IsUnbilled bit NOT NULL DEFAULT CAST(0 AS bit)');
'@
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $columnShapeSql
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql @'
UPDATE dbo.Notes SET IsAnnualPlan = 1 WHERE FormType = 4 AND FormId IS NOT NULL;
'@
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql @'
INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
VALUES (N'20260928140919_AddAnnualPcpAndUnbilledNotes', N'10.0.5');
'@
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $afterSql
        if ($WhatIfOnly) {
            $transaction.Rollback()
            Invoke-Sql -Connection $connection -Sql $beforeSql
            Write-Host 'Rollback rehearsal passed; the 120-migration boundary remains intact.'
        }
        else {
            $transaction.Commit()
            Invoke-Sql -Connection $connection -Sql $afterSql
            Write-Host 'Applied and verified annual-PCP migration 121 in SatiDemo.'
        }
    }
    catch {
        try { $transaction.Rollback() } catch { }
        throw
    }
    finally { $transaction.Dispose() }
}
finally { $connection.Dispose() }
