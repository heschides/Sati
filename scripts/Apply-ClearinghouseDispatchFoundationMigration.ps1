<#
.SYNOPSIS
    Applies the clearinghouse dispatch foundation migration to identity-marked SatiDemo.

.DESCRIPTION
    Checks the database identity, exact migration boundary, and schema before writing.
    Uses the reviewed EF-generated SQL with its pinned SHA-256 in one outer transaction.
    Run -PreflightOnly, then -WhatIfOnly, then apply, and finally rerun to prove
    idempotency. This script provisions schema only: it does not enable a connector,
    create an account, set a secret, submit a claim, change a firewall rule, or print
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

$sqlPath = Join-Path $PSScriptRoot 'Apply-ClearinghouseDispatchFoundationMigration.generated.sql'
$expectedHash = '91687B0FEF97352242D094CBD6D5AEFB9CB8B515C1D1B9770BBEECA920EB5388'
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
    THROW 52850, 'Connected database is not SatiDemo.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 52851, 'Demo identity table is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity
               WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 52852, 'Database identity is not Demo.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL OR
   OBJECT_ID(N'dbo.Agencies', N'U') IS NULL OR
   OBJECT_ID(N'dbo.Users', N'U') IS NULL OR
   OBJECT_ID(N'dbo.EdiGenerations', N'U') IS NULL OR
   OBJECT_ID(N'dbo.ClearinghouseResponseReceipts', N'U') IS NULL
    THROW 52853, 'A prerequisite Sati table is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
               WHERE MigrationId = N'20260923180000_ReconcileDuplicateScheduledAgendaNotes')
    THROW 52854, 'The preceding migration is not recorded.', 1;
SELECT COUNT(*) FROM dbo.__EFMigrationsHistory
WHERE MigrationId = N'20260926183942_AddClearinghouseDispatchFoundation';
'@

$beforeShapeSql = @'
SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 117 OR
   (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC)
       <> N'20260923180000_ReconcileDuplicateScheduledAgendaNotes'
    THROW 52855, 'Demo migration history is not at the reviewed 117-row predecessor boundary.', 1;
IF OBJECT_ID(N'dbo.ClearinghouseAccounts', N'U') IS NOT NULL OR
   OBJECT_ID(N'dbo.ClearinghouseDispatches', N'U') IS NOT NULL OR
   OBJECT_ID(N'dbo.ClearinghouseDispatchAttempts', N'U') IS NOT NULL OR
   OBJECT_ID(N'dbo.ClearinghouseFeedCheckpoints', N'U') IS NOT NULL
    THROW 52856, 'A clearinghouse dispatch table exists without complete migration history.', 1;
IF COL_LENGTH(N'dbo.ClearinghouseResponseReceipts', N'AccountId') IS NOT NULL OR
   COL_LENGTH(N'dbo.ClearinghouseResponseReceipts', N'ConnectorKind') IS NOT NULL OR
   COL_LENGTH(N'dbo.ClearinghouseResponseReceipts', N'ConnectorVersion') IS NOT NULL OR
   COL_LENGTH(N'dbo.ClearinghouseResponseReceipts', N'ContentType') IS NOT NULL OR
   COL_LENGTH(N'dbo.ClearinghouseResponseReceipts', N'ExternalArtifactId') IS NOT NULL OR
   COL_LENGTH(N'dbo.ClearinghouseResponseReceipts', N'FeedKind') IS NOT NULL OR
   COL_LENGTH(N'dbo.ClearinghouseResponseReceipts', N'Source') IS NOT NULL
    THROW 52857, 'Clearinghouse response provenance columns exist without complete migration history.', 1;
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name IN
    (N'AK_EdiGenerations_AgencyId_Id',
     N'AK_ClearinghouseResponseReceipts_AgencyId_Id'))
    THROW 52858, 'A clearinghouse alternate key exists without complete migration history.', 1;
'@

$afterShapeSql = @'
SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 118 OR
   (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC)
       <> N'20260926183942_AddClearinghouseDispatchFoundation' OR
   (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20260926183942_AddClearinghouseDispatchFoundation') <> 1
    THROW 52859, 'Demo migration history did not reach the reviewed 118-row boundary.', 1;

IF EXISTS (
    SELECT 1
    FROM (VALUES
        (N'ActorUserId', N'int', 4, CONVERT(bit,1)),
        (N'AccountId', N'uniqueidentifier', 16, CONVERT(bit,1)),
        (N'ConnectorKind', N'int', 4, CONVERT(bit,1)),
        (N'ConnectorVersion', N'nvarchar', 80, CONVERT(bit,1)),
        (N'ContentType', N'nvarchar', 160, CONVERT(bit,1)),
        (N'ExternalArtifactId', N'nvarchar', 256, CONVERT(bit,1)),
        (N'FeedKind', N'int', 4, CONVERT(bit,1)),
        (N'Source', N'int', 4, CONVERT(bit,0))
    ) expected(ColumnName, TypeName, MaxLength, IsNullable)
    LEFT JOIN sys.columns AS c
      ON c.object_id = OBJECT_ID(N'dbo.ClearinghouseResponseReceipts')
     AND c.name = expected.ColumnName
    LEFT JOIN sys.types AS t ON t.user_type_id = c.user_type_id
    WHERE c.column_id IS NULL OR t.name <> expected.TypeName OR
          c.max_length <> expected.MaxLength OR c.is_nullable <> expected.IsNullable)
    THROW 52860, 'A clearinghouse response provenance column is missing or has the wrong shape.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints AS d
    JOIN sys.columns AS c ON c.object_id = d.parent_object_id
                         AND c.column_id = d.parent_column_id
    WHERE d.parent_object_id = OBJECT_ID(N'dbo.ClearinghouseResponseReceipts')
      AND c.name = N'Source')
    THROW 52861, 'The clearinghouse response Source column has no default constraint.', 1;
IF EXISTS (SELECT 1 FROM dbo.ClearinghouseResponseReceipts
           WHERE Source <> 0 OR AccountId IS NOT NULL OR ConnectorKind IS NOT NULL OR
                 ConnectorVersion IS NOT NULL OR ContentType IS NOT NULL OR
                 ExternalArtifactId IS NOT NULL OR FeedKind IS NOT NULL)
    THROW 52862, 'Existing clearinghouse response rows were not initialized conservatively.', 1;

IF OBJECT_ID(N'dbo.ClearinghouseAccounts', N'U') IS NULL OR
   OBJECT_ID(N'dbo.ClearinghouseDispatches', N'U') IS NULL OR
   OBJECT_ID(N'dbo.ClearinghouseDispatchAttempts', N'U') IS NULL OR
   OBJECT_ID(N'dbo.ClearinghouseFeedCheckpoints', N'U') IS NULL
    THROW 52863, 'A clearinghouse dispatch foundation table is missing.', 1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ClearinghouseAccounts')) <> 12 OR
   (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ClearinghouseDispatches')) <> 13 OR
   (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ClearinghouseDispatchAttempts')) <> 16 OR
   (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ClearinghouseFeedCheckpoints')) <> 8
    THROW 52864, 'A clearinghouse dispatch foundation table has an unexpected column count.', 1;
IF EXISTS (
    SELECT 1
    FROM (VALUES
        (N'ClearinghouseAccounts', N'Id', N'uniqueidentifier', 16, CONVERT(bit,0)),
        (N'ClearinghouseAccounts', N'AgencyId', N'int', 4, CONVERT(bit,0)),
        (N'ClearinghouseAccounts', N'ExternalAccountNumber', N'nvarchar', 160, CONVERT(bit,0)),
        (N'ClearinghouseAccounts', N'ClaimNamespace', N'nvarchar', 16, CONVERT(bit,1)),
        (N'ClearinghouseAccounts', N'SecretReference', N'nvarchar', 1000, CONVERT(bit,1)),
        (N'ClearinghouseAccounts', N'Revision', N'bigint', 8, CONVERT(bit,0)),
        (N'ClearinghouseDispatches', N'Id', N'uniqueidentifier', 16, CONVERT(bit,0)),
        (N'ClearinghouseDispatches', N'AccountId', N'uniqueidentifier', 16, CONVERT(bit,0)),
        (N'ClearinghouseDispatches', N'EdiGenerationId', N'bigint', 8, CONVERT(bit,0)),
        (N'ClearinghouseDispatches', N'ExternalFileId', N'nvarchar', 256, CONVERT(bit,1)),
        (N'ClearinghouseDispatches', N'Revision', N'bigint', 8, CONVERT(bit,0)),
        (N'ClearinghouseDispatchAttempts', N'Id', N'uniqueidentifier', 16, CONVERT(bit,0)),
        (N'ClearinghouseDispatchAttempts', N'DispatchId', N'uniqueidentifier', 16, CONVERT(bit,0)),
        (N'ClearinghouseDispatchAttempts', N'ContentSha256', N'nvarchar', 128, CONVERT(bit,0)),
        (N'ClearinghouseDispatchAttempts', N'ResponseCiphertext', N'varbinary', -1, CONVERT(bit,1)),
        (N'ClearinghouseDispatchAttempts', N'ResponseNonce', N'varbinary', 12, CONVERT(bit,1)),
        (N'ClearinghouseDispatchAttempts', N'ResponseTag', N'varbinary', 16, CONVERT(bit,1)),
        (N'ClearinghouseFeedCheckpoints', N'Id', N'uniqueidentifier', 16, CONVERT(bit,0)),
        (N'ClearinghouseFeedCheckpoints', N'AccountId', N'uniqueidentifier', 16, CONVERT(bit,0)),
        (N'ClearinghouseFeedCheckpoints', N'Cursor', N'nvarchar', 512, CONVERT(bit,1)),
        (N'ClearinghouseFeedCheckpoints', N'LastReceiptId', N'uniqueidentifier', 16, CONVERT(bit,1)),
        (N'ClearinghouseFeedCheckpoints', N'Revision', N'bigint', 8, CONVERT(bit,0))
    ) expected(TableName, ColumnName, TypeName, MaxLength, IsNullable)
    LEFT JOIN sys.columns AS c
      ON c.object_id = OBJECT_ID(N'dbo.' + expected.TableName)
     AND c.name = expected.ColumnName
    LEFT JOIN sys.types AS t ON t.user_type_id = c.user_type_id
    WHERE c.column_id IS NULL OR t.name <> expected.TypeName OR
          c.max_length <> expected.MaxLength OR c.is_nullable <> expected.IsNullable)
    THROW 52865, 'A critical clearinghouse dispatch column is missing or has the wrong shape.', 1;

IF (SELECT COUNT(*) FROM sys.key_constraints WHERE name IN
    (N'PK_ClearinghouseAccounts', N'PK_ClearinghouseDispatches',
     N'PK_ClearinghouseDispatchAttempts', N'PK_ClearinghouseFeedCheckpoints',
     N'AK_ClearinghouseAccounts_AgencyId_Id', N'AK_EdiGenerations_AgencyId_Id',
     N'AK_ClearinghouseResponseReceipts_AgencyId_Id')) <> 7
    THROW 52866, 'A clearinghouse primary or alternate key is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.ClearinghouseAccounts')
                 AND name = N'CK_ClearinghouseAccounts_ClaimMdProfile'
                 AND is_disabled = 0 AND is_not_trusted = 0)
    THROW 52867, 'The Claim.MD account-profile check is missing, disabled, or untrusted.', 1;

IF (SELECT COUNT(*) FROM sys.indexes WHERE name IN
    (N'IX_ClearinghouseResponseReceipts_AccountId_FeedKind_ExternalArtifactId',
     N'IX_ClearinghouseResponseReceipts_AgencyId_AccountId',
     N'IX_ClearinghouseAccounts_AgencyId_ConnectorKind_IsTest',
     N'IX_ClearinghouseAccounts_ClaimNamespace',
     N'IX_ClearinghouseDispatchAttempts_DispatchId_AttemptNumber',
     N'IX_ClearinghouseDispatches_AgencyId_AccountId',
     N'IX_ClearinghouseDispatches_AgencyId_EdiGenerationId',
     N'IX_ClearinghouseDispatches_RequestingUserId',
     N'IX_ClearinghouseDispatches_AgencyId_State_RequestedAtUtc',
     N'IX_ClearinghouseDispatches_EdiGenerationId',
     N'IX_ClearinghouseFeedCheckpoints_AccountId_FeedKind',
     N'IX_ClearinghouseFeedCheckpoints_AgencyId_AccountId',
     N'IX_ClearinghouseFeedCheckpoints_AgencyId_LastReceiptId')
     AND is_disabled = 0) <> 13
    THROW 52868, 'A clearinghouse dispatch index is missing or disabled.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ClearinghouseResponseReceipts')
                 AND name = N'IX_ClearinghouseResponseReceipts_AccountId_FeedKind_ExternalArtifactId'
                 AND is_unique = 1 AND has_filter = 1) OR
   NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ClearinghouseAccounts')
                 AND name = N'IX_ClearinghouseAccounts_AgencyId_ConnectorKind_IsTest'
                 AND is_unique = 1 AND has_filter = 1) OR
   NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ClearinghouseAccounts')
                 AND name = N'IX_ClearinghouseAccounts_ClaimNamespace'
                 AND is_unique = 1 AND has_filter = 1) OR
   NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ClearinghouseDispatchAttempts')
                 AND name = N'IX_ClearinghouseDispatchAttempts_DispatchId_AttemptNumber'
                 AND is_unique = 1) OR
   NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ClearinghouseDispatches')
                 AND name = N'IX_ClearinghouseDispatches_EdiGenerationId'
                 AND is_unique = 1) OR
   NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ClearinghouseFeedCheckpoints')
                 AND name = N'IX_ClearinghouseFeedCheckpoints_AccountId_FeedKind'
                 AND is_unique = 1)
    THROW 52869, 'A required filtered or unique index is incompatible.', 1;

IF (SELECT COUNT(*) FROM sys.foreign_keys
    WHERE name IN (
        N'FK_ClearinghouseAccounts_Agencies_AgencyId',
        N'FK_ClearinghouseDispatches_ClearinghouseAccounts_AgencyId_AccountId',
        N'FK_ClearinghouseDispatches_EdiGenerations_AgencyId_EdiGenerationId',
        N'FK_ClearinghouseDispatches_Users_RequestingUserId',
        N'FK_ClearinghouseFeedCheckpoints_ClearinghouseAccounts_AgencyId_AccountId',
        N'FK_ClearinghouseFeedCheckpoints_ClearinghouseResponseReceipts_AgencyId_LastReceiptId',
        N'FK_ClearinghouseDispatchAttempts_ClearinghouseDispatches_DispatchId',
        N'FK_ClearinghouseResponseReceipts_ClearinghouseAccounts_AgencyId_AccountId')
      AND delete_referential_action = 0
      AND is_disabled = 0 AND is_not_trusted = 0) <> 8
    THROW 52870, 'A clearinghouse relationship is missing, disabled, untrusted, or cascading.', 1;
'@

try {
    $connection.Open()
    $applied = [int](Invoke-Sql -Connection $connection -Sql $identitySql -Scalar)
    if ($applied -notin @(0, 1)) {
        throw 'The clearinghouse migration history row is duplicated.'
    }
    if ($applied -eq 0) {
        Invoke-Sql -Connection $connection -Sql $beforeShapeSql
        Write-Host 'Identity-checked SatiDemo is at the exact 117-row predecessor boundary.'
    }
    else {
        Invoke-Sql -Connection $connection -Sql $afterShapeSql
        Write-Host 'Identity-checked SatiDemo already has the clearinghouse migration and verified schema.'
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

    # SQL Server compiles the filtered index before the preceding ALTER TABLE
    # statements when the whole EF script is submitted as one command. Preserve
    # the hash-pinned SQL verbatim, but cross a command boundary after the new
    # columns and tables exist. Both commands remain in the same transaction.
    $indexBoundary = 'CREATE UNIQUE INDEX [IX_ClearinghouseResponseReceipts_AccountId_FeedKind_ExternalArtifactId]'
    $indexAt = $statement.IndexOf($indexBoundary, [System.StringComparison]::Ordinal)
    if ($indexAt -le 0 -or
        $statement.IndexOf($indexBoundary, $indexAt + 1, [System.StringComparison]::Ordinal) -ge 0) {
        throw 'The reviewed clearinghouse SQL no longer has one expected index boundary.'
    }
    $schemaStatement = $statement.Substring(0, $indexAt)
    $indexAndHistoryStatement = $statement.Substring($indexAt)

    $transaction = $connection.BeginTransaction()
    try {
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $schemaStatement
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $indexAndHistoryStatement
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $afterShapeSql
        if ($WhatIfOnly) {
            $transaction.Rollback()
            Write-Host 'Rollback rehearsal passed: migration and schema checks ran, then rolled back.'
            Invoke-Sql -Connection $connection -Sql $beforeShapeSql
            Write-Host 'Verified the 117-row predecessor schema remains intact.'
        }
        else {
            $transaction.Commit()
            Invoke-Sql -Connection $connection -Sql $afterShapeSql
            Write-Host 'Applied and verified the clearinghouse dispatch foundation migration in SatiDemo.'
        }
    }
    catch {
        try { $transaction.Rollback() } catch { }
        throw
    }
    finally { $transaction.Dispose() }
}
finally { $connection.Dispose() }
