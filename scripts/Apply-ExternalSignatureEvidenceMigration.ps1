<#
.SYNOPSIS
    Applies the external-signature evidence migration to identity-marked SatiDemo.

.DESCRIPTION
    Checks the database identity, exact migration boundary, and schema before writing.
    Uses the reviewed EF-generated SQL with its pinned SHA-256 in one outer transaction.
    Run -PreflightOnly, then -WhatIfOnly, then apply, and finally rerun to prove
    idempotency. This script never changes a firewall rule or prints the Azure token.

    The immediately preceding clearinghouse migration is a hard prerequisite. This
    runner will not apply it, skip it, or create a gap in EF migration history.
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

$migrationId = '20260927152031_AddExternalSignatureEvidence'
$predecessorId = '20260926183942_AddClearinghouseDispatchFoundation'
$sqlPath = Join-Path $PSScriptRoot 'Apply-ExternalSignatureEvidenceMigration.generated.sql'
$expectedHash = 'B925A0FE759CE7E62FB108B835B95E29C5FABA68ADA729E0DD9567E499B801CF'
if (-not (Test-Path -LiteralPath $sqlPath -PathType Leaf)) {
    throw 'The reviewed EF migration SQL file is missing.'
}
if ((Get-FileHash -LiteralPath $sqlPath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'The reviewed EF migration SQL has changed. Regenerate, review, and repin it before proceeding.'
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
    THROW 52900, 'Connected database is not SatiDemo.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 52901, 'Demo identity table is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity
               WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 52902, 'Database identity is not Demo.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL OR
   OBJECT_ID(N'dbo.Settings', N'U') IS NULL OR
   OBJECT_ID(N'dbo.Agencies', N'U') IS NULL OR
   OBJECT_ID(N'dbo.People', N'U') IS NULL OR
   OBJECT_ID(N'dbo.Users', N'U') IS NULL OR
   OBJECT_ID(N'dbo.DocumentArtifacts', N'U') IS NULL OR
   OBJECT_ID(N'dbo.ReleaseObligations', N'U') IS NULL
    THROW 52903, 'A prerequisite Sati table is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
               WHERE MigrationId = N'20260926183942_AddClearinghouseDispatchFoundation')
    THROW 52904, 'The preceding clearinghouse migration is not recorded. This runner will not apply or skip it.', 1;
SELECT COUNT(*) FROM dbo.__EFMigrationsHistory
WHERE MigrationId = N'20260927152031_AddExternalSignatureEvidence';
'@

$beforeShapeSql = @'
SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 118 OR
   (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC)
       <> N'20260926183942_AddClearinghouseDispatchFoundation'
    THROW 52905, 'Demo migration history is not at the reviewed 118-row predecessor boundary.', 1;
IF COL_LENGTH(N'dbo.Settings', N'IsInternalElectronicSignatureEnabled') IS NOT NULL OR
   OBJECT_ID(N'dbo.ExternalSignatureEvidence', N'U') IS NOT NULL
    THROW 52906, 'Signature schema exists without its complete migration history.', 1;
'@

$afterShapeSql = @'
SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 119 OR
   (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC)
       <> N'20260927152031_AddExternalSignatureEvidence' OR
   (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20260927152031_AddExternalSignatureEvidence') <> 1
    THROW 52907, 'Demo migration history did not reach the reviewed 119-row boundary.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns AS c
    JOIN sys.types AS t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.Settings')
      AND c.name = N'IsInternalElectronicSignatureEnabled'
      AND t.name = N'bit' AND c.is_nullable = 0)
    THROW 52908, 'The agency signature setting is missing or has the wrong shape.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints AS d
    JOIN sys.columns AS c ON c.object_id = d.parent_object_id
                         AND c.column_id = d.parent_column_id
    WHERE d.parent_object_id = OBJECT_ID(N'dbo.Settings')
      AND c.name = N'IsInternalElectronicSignatureEnabled')
    THROW 52909, 'The agency signature setting has no default constraint.', 1;

IF OBJECT_ID(N'dbo.ExternalSignatureEvidence', N'U') IS NULL
    THROW 52910, 'ExternalSignatureEvidence was not created.', 1;
IF EXISTS (
    SELECT 1
    FROM (VALUES
        (N'Id', N'int', 4, CONVERT(bit,0), CONVERT(bit,1)),
        (N'ClientRequestId', N'uniqueidentifier', 16, CONVERT(bit,0), CONVERT(bit,0)),
        (N'AgencyId', N'int', 4, CONVERT(bit,0), CONVERT(bit,0)),
        (N'PersonId', N'int', 4, CONVERT(bit,0), CONVERT(bit,0)),
        (N'DocumentArtifactId', N'int', 4, CONVERT(bit,0), CONVERT(bit,0)),
        (N'ReleaseObligationId', N'bigint', 8, CONVERT(bit,0), CONVERT(bit,0)),
        (N'Method', N'nvarchar', 80, CONVERT(bit,0), CONVERT(bit,0)),
        (N'SignedOn', N'date', 3, CONVERT(bit,0), CONVERT(bit,0)),
        (N'SignerName', N'nvarchar', 240, CONVERT(bit,0), CONVERT(bit,0)),
        (N'SignerCapacity', N'nvarchar', 80, CONVERT(bit,0), CONVERT(bit,0)),
        (N'AttestedByUserId', N'int', 4, CONVERT(bit,0), CONVERT(bit,0)),
        (N'AttestedAtUtc', N'datetime2', 8, CONVERT(bit,0), CONVERT(bit,0)),
        (N'AttestationText', N'nvarchar', 2000, CONVERT(bit,0), CONVERT(bit,0)),
        (N'BlobPath', N'nvarchar', 800, CONVERT(bit,0), CONVERT(bit,0)),
        (N'ContentSha256', N'char', 64, CONVERT(bit,0), CONVERT(bit,0)),
        (N'ByteCount', N'bigint', 8, CONVERT(bit,0), CONVERT(bit,0)),
        (N'VerificationNote', N'nvarchar', 2000, CONVERT(bit,1), CONVERT(bit,0))
    ) expected(ColumnName, TypeName, MaxLength, IsNullable, IsIdentity)
    LEFT JOIN sys.columns AS c
      ON c.object_id = OBJECT_ID(N'dbo.ExternalSignatureEvidence')
     AND c.name = expected.ColumnName
    LEFT JOIN sys.types AS t ON t.user_type_id = c.user_type_id
    WHERE c.column_id IS NULL OR t.name <> expected.TypeName OR
          c.max_length <> expected.MaxLength OR
          c.is_nullable <> expected.IsNullable OR
          c.is_identity <> expected.IsIdentity)
    THROW 52911, 'An ExternalSignatureEvidence column is missing or has the wrong shape.', 1;
IF (SELECT COUNT(*) FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.ExternalSignatureEvidence')) <> 17
    THROW 52912, 'ExternalSignatureEvidence has an unexpected column count.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.ExternalSignatureEvidence')
                 AND name = N'PK_ExternalSignatureEvidence' AND type = N'PK')
    THROW 52913, 'ExternalSignatureEvidence primary key is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ExternalSignatureEvidence')
                 AND name = N'IX_ExternalSignatureEvidence_AgencyId_ClientRequestId'
                 AND is_unique = 1 AND is_disabled = 0) OR
   NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ExternalSignatureEvidence')
                 AND name = N'IX_ExternalSignatureEvidence_DocumentArtifactId'
                 AND is_unique = 1 AND is_disabled = 0) OR
   NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ExternalSignatureEvidence')
                 AND name = N'IX_ExternalSignatureEvidence_AttestedByUserId'
                 AND is_unique = 0 AND is_disabled = 0) OR
   NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ExternalSignatureEvidence')
                 AND name = N'IX_ExternalSignatureEvidence_PersonId'
                 AND is_unique = 0 AND is_disabled = 0) OR
   NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ExternalSignatureEvidence')
                 AND name = N'IX_ExternalSignatureEvidence_ReleaseObligationId'
                 AND is_unique = 0 AND is_disabled = 0)
    THROW 52914, 'An ExternalSignatureEvidence index is missing or incompatible.', 1;

IF (SELECT COUNT(*) FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.ExternalSignatureEvidence')
      AND name IN (
          N'FK_ExternalSignatureEvidence_Agencies_AgencyId',
          N'FK_ExternalSignatureEvidence_DocumentArtifacts_DocumentArtifactId',
          N'FK_ExternalSignatureEvidence_People_PersonId',
          N'FK_ExternalSignatureEvidence_ReleaseObligations_ReleaseObligationId',
          N'FK_ExternalSignatureEvidence_Users_AttestedByUserId')
      AND delete_referential_action = 0
      AND is_disabled = 0 AND is_not_trusted = 0) <> 5
    THROW 52915, 'An ExternalSignatureEvidence relationship is missing, disabled, untrusted, or cascading.', 1;
'@

try {
    $connection.Open()
    $applied = [int](Invoke-Sql -Connection $connection -Sql $identitySql -Scalar)
    if ($applied -notin @(0, 1)) {
        throw 'The signature migration history row is duplicated.'
    }
    if ($applied -eq 0) {
        Invoke-Sql -Connection $connection -Sql $beforeShapeSql
        Write-Host 'Identity-checked SatiDemo is at the exact 118-row predecessor boundary.'
    }
    else {
        Invoke-Sql -Connection $connection -Sql $afterShapeSql
        Write-Host 'Identity-checked SatiDemo already has the signature migration and verified schema.'
    }

    if ($PreflightOnly) { return }
    if ($applied -eq 1) {
        Write-Host 'No changes required. Idempotency check passed.'
        return
    }

    $rawSql = Get-Content -LiteralPath $sqlPath -Raw
    $batches = @($rawSql -split '(?im)^GO\s*$' | Where-Object { $_.Trim() })
    if ($batches.Count -ne 1) {
        throw "Expected one EF SQL batch; found $($batches.Count)."
    }
    $statement = [System.Text.RegularExpressions.Regex]::Replace(
        $batches[0], '(?im)^\s*BEGIN TRANSACTION;\s*', '')
    $statement = [System.Text.RegularExpressions.Regex]::Replace(
        $statement, '(?im)COMMIT;\s*$', '')

    $transaction = $connection.BeginTransaction()
    try {
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $statement
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $afterShapeSql
        if ($WhatIfOnly) {
            $transaction.Rollback()
            Write-Host 'Rollback rehearsal passed: migration and schema checks ran, then rolled back.'
            Invoke-Sql -Connection $connection -Sql $beforeShapeSql
            Write-Host 'Verified the 118-row predecessor schema remains intact.'
        }
        else {
            $transaction.Commit()
            Invoke-Sql -Connection $connection -Sql $afterShapeSql
            Write-Host 'Applied and verified the external-signature evidence migration in SatiDemo.'
        }
    }
    catch {
        try { $transaction.Rollback() } catch { }
        throw
    }
    finally { $transaction.Dispose() }
}
finally { $connection.Dispose() }
