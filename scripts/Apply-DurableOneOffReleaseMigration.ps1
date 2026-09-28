<#
.SYNOPSIS
    Applies the durable one-off release migration to identity-marked SatiDemo.

.DESCRIPTION
    Checks database identity, the exact migration boundary, and the relevant schema
    before writing. Uses reviewed EF-generated SQL with a pinned SHA-256 inside one
    outer transaction. Run -PreflightOnly, then -WhatIfOnly, then apply, and rerun
    once to prove idempotency. This script never changes a firewall rule or prints
    the Azure access token.
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

$migrationId = '20260927232039_SupportDurableOneOffReleases'
$predecessorId = '20260927152031_AddExternalSignatureEvidence'
$sqlPath = Join-Path $PSScriptRoot 'Apply-DurableOneOffReleaseMigration.generated.sql'
$expectedHash = 'B1DD0734BBF669AC0F89C55ED1320EDA4547D7528CFE6EEF4673CAE1B5B6AFE0'
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
    THROW 53000, 'Connected database is not SatiDemo.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 53001, 'Demo identity table is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity
               WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 53002, 'Database identity is not Demo.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL OR
   OBJECT_ID(N'dbo.Providers', N'U') IS NULL OR
   OBJECT_ID(N'dbo.DocumentArtifacts', N'U') IS NULL OR
   OBJECT_ID(N'dbo.ExternalSignatureEvidence', N'U') IS NULL
    THROW 53003, 'A prerequisite Sati table is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
               WHERE MigrationId = N'20260927152031_AddExternalSignatureEvidence')
    THROW 53004, 'The preceding signature migration is not recorded.', 1;
SELECT COUNT(*) FROM dbo.__EFMigrationsHistory
WHERE MigrationId = N'20260927232039_SupportDurableOneOffReleases';
'@

$beforeShapeSql = @'
SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 119 OR
   (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC)
       <> N'20260927152031_AddExternalSignatureEvidence'
    THROW 53005, 'Demo migration history is not at the reviewed 119-row predecessor boundary.', 1;
IF COL_LENGTH(N'dbo.Providers', N'Email') IS NOT NULL OR
   COL_LENGTH(N'dbo.DocumentArtifacts', N'OneOffRecipientJson') IS NOT NULL OR
   COL_LENGTH(N'dbo.DocumentArtifacts', N'OneOffReleaseId') IS NOT NULL OR
   COL_LENGTH(N'dbo.DocumentArtifacts', N'PromotedProviderId') IS NOT NULL OR
   EXISTS (SELECT 1 FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'dbo.DocumentArtifacts')
             AND name IN (N'IX_DocumentArtifacts_OneLivePerOneOffRelease',
                          N'IX_DocumentArtifacts_PromotedProviderId')) OR
   EXISTS (SELECT 1 FROM sys.foreign_keys
           WHERE parent_object_id = OBJECT_ID(N'dbo.DocumentArtifacts')
             AND name = N'FK_DocumentArtifacts_Providers_PromotedProviderId')
    THROW 53006, 'One-off release schema exists without its complete migration history.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.columns AS c
    JOIN sys.types AS t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.ExternalSignatureEvidence')
      AND c.name = N'ReleaseObligationId' AND t.name = N'bigint'
      AND c.max_length = 8 AND c.is_nullable = 0)
    THROW 53007, 'ReleaseObligationId is not at the expected required predecessor shape.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = OBJECT_ID(N'dbo.DocumentArtifacts')
      AND i.name = N'IX_DocumentArtifacts_OneLivePerCycle'
      AND i.is_unique = 1 AND i.is_disabled = 0
      AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            i.filter_definition, N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N'')
          = N'ReleaseObligationIdISNULLANDSupersededByArtifactIdISNULL'
      AND 3 = (SELECT COUNT(*) FROM sys.index_columns
               WHERE object_id = i.object_id AND index_id = i.index_id AND key_ordinal > 0)
      AND COL_NAME(i.object_id, (SELECT column_id FROM sys.index_columns
            WHERE object_id = i.object_id AND index_id = i.index_id AND key_ordinal = 1)) = N'PersonId'
      AND COL_NAME(i.object_id, (SELECT column_id FROM sys.index_columns
            WHERE object_id = i.object_id AND index_id = i.index_id AND key_ordinal = 2)) = N'Kind'
      AND COL_NAME(i.object_id, (SELECT column_id FROM sys.index_columns
            WHERE object_id = i.object_id AND index_id = i.index_id AND key_ordinal = 3)) = N'CycleStart')
    THROW 53008, 'The predecessor live-cycle artifact index has an unexpected definition.', 1;
'@

$afterShapeSql = @'
SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 120 OR
   (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC)
       <> N'20260927232039_SupportDurableOneOffReleases' OR
   (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20260927232039_SupportDurableOneOffReleases') <> 1
    THROW 53009, 'Demo migration history did not reach the reviewed 120-row boundary.', 1;

IF EXISTS (
    SELECT 1
    FROM (VALUES
        (N'Email', OBJECT_ID(N'dbo.Providers'), N'nvarchar', 508, CONVERT(bit,1)),
        (N'OneOffRecipientJson', OBJECT_ID(N'dbo.DocumentArtifacts'), N'nvarchar', 8000, CONVERT(bit,1)),
        (N'OneOffReleaseId', OBJECT_ID(N'dbo.DocumentArtifacts'), N'uniqueidentifier', 16, CONVERT(bit,1)),
        (N'PromotedProviderId', OBJECT_ID(N'dbo.DocumentArtifacts'), N'int', 4, CONVERT(bit,1)),
        (N'ReleaseObligationId', OBJECT_ID(N'dbo.ExternalSignatureEvidence'), N'bigint', 8, CONVERT(bit,1))
    ) expected(ColumnName, ObjectId, TypeName, MaxLength, IsNullable)
    LEFT JOIN sys.columns AS c ON c.object_id = expected.ObjectId AND c.name = expected.ColumnName
    LEFT JOIN sys.types AS t ON t.user_type_id = c.user_type_id
    WHERE c.column_id IS NULL OR t.name <> expected.TypeName OR
          c.max_length <> expected.MaxLength OR c.is_nullable <> expected.IsNullable)
    THROW 53010, 'A one-off release column is missing or has the wrong shape.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = OBJECT_ID(N'dbo.DocumentArtifacts')
      AND i.name = N'IX_DocumentArtifacts_OneLivePerCycle'
      AND i.is_unique = 1 AND i.is_disabled = 0
      AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            i.filter_definition, N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N'')
          = N'ReleaseObligationIdISNULLANDOneOffReleaseIdISNULLANDSupersededByArtifactIdISNULL'
      AND 3 = (SELECT COUNT(*) FROM sys.index_columns
               WHERE object_id = i.object_id AND index_id = i.index_id AND key_ordinal > 0))
    THROW 53011, 'The live-cycle artifact index is missing or incompatible.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = OBJECT_ID(N'dbo.DocumentArtifacts')
      AND i.name = N'IX_DocumentArtifacts_OneLivePerOneOffRelease'
      AND i.is_unique = 1 AND i.is_disabled = 0
      AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            i.filter_definition, N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N'')
          = N'OneOffReleaseIdISNOTNULLANDSupersededByArtifactIdISNULL'
      AND 2 = (SELECT COUNT(*) FROM sys.index_columns
               WHERE object_id = i.object_id AND index_id = i.index_id AND key_ordinal > 0))
    THROW 53012, 'The live one-off artifact index is missing or incompatible.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = OBJECT_ID(N'dbo.DocumentArtifacts')
      AND i.name = N'IX_DocumentArtifacts_PromotedProviderId'
      AND i.is_unique = 0 AND i.is_disabled = 0
      AND 1 = (SELECT COUNT(*) FROM sys.index_columns
               WHERE object_id = i.object_id AND index_id = i.index_id AND key_ordinal > 0)
      AND COL_NAME(i.object_id, (SELECT column_id FROM sys.index_columns
            WHERE object_id = i.object_id AND index_id = i.index_id AND key_ordinal = 1)) = N'PromotedProviderId')
    THROW 53013, 'The promoted-provider artifact index is missing or incompatible.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys AS fk
    JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.DocumentArtifacts')
      AND fk.referenced_object_id = OBJECT_ID(N'dbo.Providers')
      AND fk.name = N'FK_DocumentArtifacts_Providers_PromotedProviderId'
      AND fk.delete_referential_action = 2
      AND fk.is_disabled = 0 AND fk.is_not_trusted = 0
      AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = N'PromotedProviderId'
      AND COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) = N'Id')
    THROW 53014, 'The promoted-provider relationship is missing, untrusted, or has the wrong delete behavior.', 1;
'@

try {
    $connection.Open()
    $applied = [int](Invoke-Sql -Connection $connection -Sql $identitySql -Scalar)
    if ($applied -notin @(0, 1)) {
        throw 'The durable one-off release migration history row is duplicated.'
    }
    if ($applied -eq 0) {
        Invoke-Sql -Connection $connection -Sql $beforeShapeSql
        Write-Host 'Identity-checked SatiDemo is at the exact 119-row predecessor boundary.'
    }
    else {
        Invoke-Sql -Connection $connection -Sql $afterShapeSql
        Write-Host 'Identity-checked SatiDemo already has the durable one-off release migration and verified schema.'
    }

    if ($PreflightOnly) { return }
    if ($applied -eq 1) {
        Write-Host 'No changes required. Idempotency check passed.'
        return
    }

    $rawSql = Get-Content -LiteralPath $sqlPath -Raw
    # EF emitted the migration as one batch even though its later indexes name
    # columns added earlier in that batch. SQL Server binds those index column
    # names before running the ALTER statements. The reviewed SQL file therefore
    # adds only GO separators between EF commands; it does not change any DDL.
    $batches = @($rawSql -split '(?im)^GO\s*$' | Where-Object { $_.Trim() })
    if ($batches.Count -ne 11) {
        throw "Expected eleven reviewed EF SQL batches; found $($batches.Count)."
    }
    $batches[0] = [System.Text.RegularExpressions.Regex]::Replace(
        $batches[0], '(?im)^\s*BEGIN TRANSACTION;\s*', '')
    $batches[$batches.Count - 1] = [System.Text.RegularExpressions.Regex]::Replace(
        $batches[$batches.Count - 1], '(?im)COMMIT;\s*$', '')

    $transaction = $connection.BeginTransaction()
    try {
        foreach ($statement in $batches) {
            Invoke-Sql -Connection $connection -Transaction $transaction -Sql $statement
        }
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $afterShapeSql
        if ($WhatIfOnly) {
            $transaction.Rollback()
            Write-Host 'Rollback rehearsal passed: migration and schema checks ran, then rolled back.'
            Invoke-Sql -Connection $connection -Sql $beforeShapeSql
            Write-Host 'Verified the 119-row predecessor schema remains intact.'
        }
        else {
            $transaction.Commit()
            Invoke-Sql -Connection $connection -Sql $afterShapeSql
            Write-Host 'Applied and verified the durable one-off release migration in SatiDemo.'
        }
    }
    catch {
        try { $transaction.Rollback() } catch { }
        throw
    }
    finally { $transaction.Dispose() }
}
finally { $connection.Dispose() }
