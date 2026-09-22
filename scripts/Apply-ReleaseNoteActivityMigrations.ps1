<#
.SYNOPSIS
    Applies the four September 22 note/release migrations to identity-marked SatiDemo.
.DESCRIPTION
    Checks the database identity, exact migration boundary, and schema before writing.
    Uses the EF-generated SQL with its pinned SHA-256 in one transaction. Run
    -PreflightOnly, then -WhatIfOnly, then apply and rerun to prove idempotency.
    This script never changes a firewall rule or prints the Azure access token.
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

$sqlPath = Join-Path $PSScriptRoot 'Apply-ReleaseNoteActivityMigrations.generated.sql'
$expectedHash = '3A1DC2E36B1F201DD505304C42AF497D98CA9ABCFCB713808B5386AA1056E9DC'
if ((Get-FileHash -LiteralPath $sqlPath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'The reviewed EF migration SQL has changed. Regenerate and review it before proceeding.'
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
    param([System.Data.SqlClient.SqlConnection]$Connection,
          [System.Data.SqlClient.SqlTransaction]$Transaction,
          [string]$Sql,
          [switch]$Scalar)
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
    THROW 52500, 'Connected database is not SatiDemo.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 52501, 'Demo identity table is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity
               WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 52502, 'Database identity is not Demo.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL OR
   OBJECT_ID(N'dbo.Notes', N'U') IS NULL OR
   OBJECT_ID(N'dbo.ReleaseObligations', N'U') IS NULL OR
   OBJECT_ID(N'dbo.ReleaseObligationAttestations', N'U') IS NULL OR
   OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags', N'U') IS NULL
    THROW 52503, 'A prerequisite table is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
               WHERE MigrationId = N'20260921235644_AddFormAttestationChangeReviewFlags')
    THROW 52504, 'The preceding migration is not recorded.', 1;
SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId IN
    (N'20260922154932_AddMultiActivityNotes',
     N'20260922161704_LinkReleaseNotesToExactObligations',
     N'20260922162222_TrackReleaseAttestationRevocation',
     N'20260922191918_SupportReleaseAttestationReviewFlags');
'@

$beforeShapeSql = @'
SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 112 OR
   (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC)
       <> N'20260921235644_AddFormAttestationChangeReviewFlags'
    THROW 52505, 'Demo migration history is not at the reviewed 112-row boundary.', 1;
IF COL_LENGTH(N'dbo.Notes', N'Activities') IS NOT NULL OR
   COL_LENGTH(N'dbo.Notes', N'ReleaseObligationId') IS NOT NULL OR
   COL_LENGTH(N'dbo.ReleaseObligationAttestations', N'EvidenceNoteId') IS NOT NULL OR
   COL_LENGTH(N'dbo.ReleaseObligationAttestations', N'RevocationReason') IS NOT NULL OR
   COL_LENGTH(N'dbo.ReleaseObligationAttestations', N'RevokedAtUtc') IS NOT NULL OR
   COL_LENGTH(N'dbo.ReleaseObligationAttestations', N'RevokedByUserId') IS NOT NULL OR
   COL_LENGTH(N'dbo.FormAttestationChangeReviewFlags', N'ReleaseObligationId') IS NOT NULL
    THROW 52506, 'One of the new columns exists without its complete migration history.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags')
               AND name = N'FormId' AND is_nullable = 0)
    THROW 52507, 'The existing review flag FormId has an unexpected definition.', 1;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name IN
    (N'FK_Notes_ReleaseObligations_ReleaseObligationId',
     N'FK_FormAttestationChangeReviewFlags_ReleaseObligations_ReleaseObligationId')) OR
   EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = N'CK_FormAttestationChangeReviewFlags_OneSource')
    THROW 52508, 'A new relationship or check exists without its migration history.', 1;
'@

$afterShapeSql = @'
SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 116 OR
   (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC)
       <> N'20260922191918_SupportReleaseAttestationReviewFlags'
    THROW 52509, 'Demo migration history did not reach the expected 116-row boundary.', 1;
IF EXISTS (SELECT 1 FROM (VALUES
    (N'Notes', N'Activities', N'int', CONVERT(bit,1)),
    (N'Notes', N'ReleaseObligationId', N'bigint', CONVERT(bit,1)),
    (N'ReleaseObligationAttestations', N'EvidenceNoteId', N'int', CONVERT(bit,1)),
    (N'ReleaseObligationAttestations', N'RevocationReason', N'nvarchar', CONVERT(bit,1)),
    (N'ReleaseObligationAttestations', N'RevokedAtUtc', N'datetime2', CONVERT(bit,1)),
    (N'ReleaseObligationAttestations', N'RevokedByUserId', N'int', CONVERT(bit,1)),
    (N'FormAttestationChangeReviewFlags', N'FormId', N'int', CONVERT(bit,1)),
    (N'FormAttestationChangeReviewFlags', N'ReleaseObligationId', N'bigint', CONVERT(bit,1))
) expected(TableName, ColumnName, TypeName, IsNullable)
LEFT JOIN sys.columns c ON c.object_id = OBJECT_ID(N'dbo.' + expected.TableName)
    AND c.name = expected.ColumnName
LEFT JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.column_id IS NULL OR t.name <> expected.TypeName OR c.is_nullable <> expected.IsNullable)
    THROW 52510, 'A migrated column is missing or has the wrong type/nullability.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Notes')
               AND name = N'IX_Notes_ReleaseObligationId' AND is_disabled = 0) OR
   NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags')
               AND name = N'IX_FormAttestationChangeReviewFlags_ReleaseObligationId' AND is_disabled = 0)
    THROW 52511, 'A release-link index is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.Notes')
               AND name = N'FK_Notes_ReleaseObligations_ReleaseObligationId'
               AND delete_referential_action = 0 AND is_disabled = 0 AND is_not_trusted = 0) OR
   NOT EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE parent_object_id = OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags')
               AND name = N'FK_FormAttestationChangeReviewFlags_ReleaseObligations_ReleaseObligationId'
               AND delete_referential_action = 0 AND is_disabled = 0 AND is_not_trusted = 0) OR
   NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags')
               AND name = N'CK_FormAttestationChangeReviewFlags_OneSource'
               AND is_disabled = 0 AND is_not_trusted = 0)
    THROW 52512, 'A release relationship or source check is missing or untrusted.', 1;
IF EXISTS (SELECT 1 FROM dbo.FormAttestationChangeReviewFlags
           WHERE (FormId IS NULL AND ReleaseObligationId IS NULL) OR
                 (FormId IS NOT NULL AND ReleaseObligationId IS NOT NULL))
    THROW 52513, 'A review flag does not have exactly one source.', 1;
'@

try {
    $connection.Open()
    $applied = [int](Invoke-Sql -Connection $connection -Sql $identitySql -Scalar)
    if ($applied -notin @(0, 4)) {
        throw "Only $applied of the four migration IDs are present. Stopping for investigation."
    }
    if ($applied -eq 0) {
        Invoke-Sql -Connection $connection -Sql $beforeShapeSql
        Write-Host 'Identity-checked SatiDemo is at the exact prior migration boundary (112 rows).'
    }
    else {
        Invoke-Sql -Connection $connection -Sql $afterShapeSql
        Write-Host 'Identity-checked SatiDemo already has all four migrations and their verified schema.'
    }

    if ($PreflightOnly) { return }
    if ($applied -eq 4) {
        Write-Host 'No changes required. Idempotency check passed.'
        return
    }

    $rawSql = Get-Content -LiteralPath $sqlPath -Raw
    $batches = @($rawSql -split '(?im)^GO\s*$' | Where-Object { $_.Trim() })
    if ($batches.Count -ne 4) {
        throw "Expected four EF SQL batches; found $($batches.Count)."
    }
    $transaction = $connection.BeginTransaction()
    try {
        for ($batchIndex = 0; $batchIndex -lt $batches.Count; $batchIndex++) {
            $batch = $batches[$batchIndex]
            Write-Host "Executing migration batch $($batchIndex + 1) of 4."
            $statement = $batch -replace '(?im)^BEGIN TRANSACTION;\s*', ''
            $statement = $statement -replace '(?im)^COMMIT;\s*', ''
            if ($batchIndex -eq 3) {
                # SQL Server binds the new column in a CHECK expression before it
                # executes ADD COLUMN when both appear in one compiled batch.
                $anchor = 'ALTER TABLE [FormAttestationChangeReviewFlags] ADD [ReleaseObligationId] bigint NULL;'
                $position = $statement.IndexOf($anchor, [StringComparison]::Ordinal)
                if ($position -lt 0) { throw 'The reviewed release column statement is missing.' }
                $boundary = $position + $anchor.Length
                Invoke-Sql -Connection $connection -Transaction $transaction -Sql $statement.Substring(0, $boundary)
                Invoke-Sql -Connection $connection -Transaction $transaction -Sql $statement.Substring($boundary)
            }
            else {
                Invoke-Sql -Connection $connection -Transaction $transaction -Sql $statement
            }
        }
        Write-Host 'Verifying the resulting schema.'
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $afterShapeSql
        if ($WhatIfOnly) {
            $transaction.Rollback()
            Write-Host 'Rollback rehearsal passed: all four migrations and schema checks ran, then rolled back.'
            Invoke-Sql -Connection $connection -Sql $beforeShapeSql
            Write-Host 'Verified the prior schema remains intact.'
        }
        else {
            $transaction.Commit()
            Invoke-Sql -Connection $connection -Sql $afterShapeSql
            Write-Host 'Applied and verified all four migrations in SatiDemo.'
        }
    }
    catch {
        try { $transaction.Rollback() } catch { }
        throw
    }
    finally { $transaction.Dispose() }
}
finally { $connection.Dispose() }
