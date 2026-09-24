<#
.SYNOPSIS
    Applies the exact-form Work Agenda duplicate reconciliation migration.

.DESCRIPTION
    Runs only against a database named exactly SatiDemo or SatiProduction whose
    dbo.SatiDatabaseIdentity marker agrees with that selection. The reviewed,
    EF-generated migration SQL is hash-pinned and executes in one outer transaction.

    Publish the fixed application before applying this repair; the deployed 1.3.25
    application can still create the duplicate shape. Run -WhatIfOnly first, apply
    normally, then rerun to prove idempotency. A rerun evaluates the exact repair
    logic inside a rollback-only transaction. If migration history already exists
    but any new eligible cancellation is found, the runner rolls back and stops so
    post-migration drift is never repaired silently.

    The result contains only database/environment identity and aggregate counts. It
    does not return note, form, person, narrative, or other client-level values. This
    script never changes a firewall rule and never prints the access token.

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net/ `
        --query accessToken --output tsv
    ./scripts/Apply-WorkAgendaDuplicateReconciliationMigration.ps1 `
        -DatabaseName SatiDemo `
        -SqlServer sati-demo-satilogica-central.database.windows.net `
        -AccessToken $token `
        -WhatIfOnly

.EXAMPLE
    ./scripts/Apply-WorkAgendaDuplicateReconciliationMigration.ps1 `
        -DatabaseName SatiProduction `
        -WhatIfOnly
#>
[CmdletBinding()]
param(
    [ValidateSet('SatiDemo', 'SatiProduction')]
    [Parameter(Mandatory)]
    [string]$DatabaseName,

    [string]$SqlServer = '(localdb)\MSSQLLocalDB',

    [string]$AccessToken,

    # Executes the complete repair and verification, then rolls back.
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
$expectedEnvironment = if ($DatabaseName -ceq 'SatiDemo') { 'Demo' } else { 'Production' }
$migrationId = '20260923180000_ReconcileDuplicateScheduledAgendaNotes'
$previousMigrationId = '20260922191918_SupportReleaseAttestationReviewFlags'
$expectedMigrationCountBefore = 116L
$expectedMigrationCountAfter = 117L
$expectedProductVersion = '10.0.5'
$sqlPath = Join-Path $PSScriptRoot 'Apply-WorkAgendaDuplicateReconciliationMigration.generated.sql'
$expectedSqlHash = '2F8C29457221E7C3EA67776E806969388CD19D85546C86AD85DF8AAD1006954C'
$usesAccessToken = -not [string]::IsNullOrWhiteSpace($AccessToken)
$localDbServer = '(localdb)\MSSQLLocalDB'

if ($DatabaseName -ceq 'SatiDemo' -and -not $usesAccessToken) {
    throw 'SatiDemo requires an Entra access token; integrated workstation credentials are not permitted.'
}
if ($usesAccessToken -and $SqlServer -ceq $localDbServer) {
    throw 'An Azure access token cannot be used with the LocalDB server.'
}
if (-not $usesAccessToken -and $SqlServer -cne $localDbServer) {
    throw 'Integrated authentication is restricted to the exact LocalDB server. Supply an Azure access token for any other server.'
}
if (-not (Test-Path -LiteralPath $sqlPath -PathType Leaf)) {
    throw 'The reviewed EF migration SQL file is missing.'
}

function Get-NormalizedSha256 {
    param([Parameter(Mandatory)][string]$Path)

    $text = [System.IO.File]::ReadAllText($Path)
    $normalized = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($normalized)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

if ((Get-NormalizedSha256 -Path $sqlPath) -cne $expectedSqlHash) {
    throw 'The reviewed EF migration SQL has changed. Regenerate, review, and repin it before proceeding.'
}

$rawSql = [System.IO.File]::ReadAllText($sqlPath)
$batches = @($rawSql -split '(?im)^GO\s*$' | Where-Object { $_.Trim() })
if ($batches.Count -ne 1) {
    throw "Expected one reviewed EF SQL batch; found $($batches.Count)."
}

$migrationSql = $batches[0]
if ($migrationSql -notmatch '(?is)^\s*BEGIN TRANSACTION;') {
    throw 'The reviewed EF SQL does not begin with the expected transaction wrapper.'
}
if ($migrationSql -notmatch '(?is)COMMIT;\s*$') {
    throw 'The reviewed EF SQL does not end with the expected transaction wrapper.'
}
$migrationSql = [System.Text.RegularExpressions.Regex]::Replace(
    $migrationSql,
    '(?is)^\s*BEGIN TRANSACTION;\s*',
    '')
$migrationSql = [System.Text.RegularExpressions.Regex]::Replace(
    $migrationSql,
    '(?is)\s*COMMIT;\s*$',
    '')

$connectionString = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$connectionString['Data Source'] = $SqlServer
$connectionString['Initial Catalog'] = $DatabaseName
$connectionString['Application Name'] = 'Sati controlled Work Agenda repair'
$connectionString['Connect Timeout'] = if ($usesAccessToken) { 90 } else { 30 }
$connectionString['Encrypt'] = $usesAccessToken
$connectionString['TrustServerCertificate'] = -not $usesAccessToken
$connectionString['Integrated Security'] = -not $usesAccessToken

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString.ConnectionString
if ($usesAccessToken) {
    $connection.AccessToken = $AccessToken.Trim()
    $AccessToken = $null
}

function Invoke-NonQuerySql {
    param(
        [Parameter(Mandatory)][System.Data.SqlClient.SqlConnection]$Connection,
        [System.Data.SqlClient.SqlTransaction]$Transaction,
        [Parameter(Mandatory)][string]$Sql
    )

    $command = $Connection.CreateCommand()
    $command.CommandTimeout = 300
    if ($Transaction) { $command.Transaction = $Transaction }
    $command.CommandText = $Sql
    try { $command.ExecuteNonQuery() | Out-Null }
    finally { $command.Dispose() }
}

$preflightSql = @'
SET NOCOUNT ON;

IF DB_NAME() COLLATE Latin1_General_100_BIN2 <>
   @expectedDatabase COLLATE Latin1_General_100_BIN2
    THROW 52800, 'The connected database is not the exact database requested.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 52801, 'dbo.SatiDatabaseIdentity is missing.', 1;
IF (SELECT COUNT_BIG(*) FROM dbo.SatiDatabaseIdentity WITH (UPDLOCK, HOLDLOCK)) <> 1 OR NOT EXISTS (
    SELECT 1
    FROM dbo.SatiDatabaseIdentity WITH (UPDLOCK, HOLDLOCK)
    WHERE Id = 1
      AND EnvironmentName COLLATE Latin1_General_100_BIN2 =
          @expectedEnvironment COLLATE Latin1_General_100_BIN2)
    THROW 52802, 'The database identity marker does not exactly match the requested environment.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 52803, 'dbo.__EFMigrationsHistory is missing.', 1;

DECLARE @requiredTables TABLE (TableName sysname NOT NULL PRIMARY KEY);
INSERT @requiredTables (TableName) VALUES
    (N'AuditEvents'),
    (N'BillingCompliancePolicyReviewFlags'),
    (N'BillingComplianceRecoveryNotes'),
    (N'ClaimCorrections'),
    (N'ClaimLines'),
    (N'FormAttestationChangeReviewFlags'),
    (N'FormAttestations'),
    (N'Forms'),
    (N'Notes'),
    (N'People'),
    (N'ReleaseObligationAttestations'),
    (N'Users');
IF EXISTS (
    SELECT 1 FROM @requiredTables
    WHERE OBJECT_ID(N'dbo.' + TableName, N'U') IS NULL)
    THROW 52804, 'A table required by the reviewed repair migration is missing.', 1;

DECLARE @requiredColumns TABLE
(
    TableName sysname NOT NULL,
    ColumnName sysname NOT NULL,
    PRIMARY KEY (TableName, ColumnName)
);
INSERT @requiredColumns (TableName, ColumnName) VALUES
    (N'AuditEvents', N'Action'),
    (N'AuditEvents', N'ActorUserId'),
    (N'AuditEvents', N'AgencyId'),
    (N'AuditEvents', N'CorrelationId'),
    (N'AuditEvents', N'EventId'),
    (N'AuditEvents', N'MetadataJson'),
    (N'AuditEvents', N'OccurredAtUtc'),
    (N'AuditEvents', N'ResourceId'),
    (N'AuditEvents', N'ResourceType'),
    (N'BillingCompliancePolicyReviewFlags', N'NoteId'),
    (N'BillingComplianceRecoveryNotes', N'NoteId'),
    (N'ClaimCorrections', N'NoteId'),
    (N'ClaimLines', N'NoteId'),
    (N'FormAttestationChangeReviewFlags', N'NoteId'),
    (N'FormAttestations', N'EvidenceNoteId'),
    (N'Forms', N'Id'),
    (N'Forms', N'PersonId'),
    (N'Forms', N'Type'),
    (N'Notes', N'Activities'),
    (N'Notes', N'AgencyId'),
    (N'Notes', N'ApprovedAt'),
    (N'Notes', N'ApprovedById'),
    (N'Notes', N'CaseManagerJustification'),
    (N'Notes', N'ComplianceOverride'),
    (N'Notes', N'EventDate'),
    (N'Notes', N'FormDateCorrectionReason'),
    (N'Notes', N'FormId'),
    (N'Notes', N'FormType'),
    (N'Notes', N'GoalProgress'),
    (N'Notes', N'Id'),
    (N'Notes', N'Minutes'),
    (N'Notes', N'Narrative'),
    (N'Notes', N'NoteType'),
    (N'Notes', N'OverrideApprovedAt'),
    (N'Notes', N'OverrideApprovedById'),
    (N'Notes', N'OverrideAttestationConfirmed'),
    (N'Notes', N'OverrideObligationIdsJson'),
    (N'Notes', N'OverrideReason'),
    (N'Notes', N'PersonId'),
    (N'Notes', N'ReleaseObligationId'),
    (N'Notes', N'ReturnReason'),
    (N'Notes', N'ReturnedAt'),
    (N'Notes', N'ReturnedById'),
    (N'Notes', N'Revision'),
    (N'Notes', N'StartTime'),
    (N'Notes', N'Status'),
    (N'Notes', N'VisitDocumentationJson'),
    (N'People', N'AgencyId'),
    (N'People', N'Id'),
    (N'People', N'UserId'),
    (N'ReleaseObligationAttestations', N'EvidenceNoteId'),
    (N'Users', N'AgencyId'),
    (N'Users', N'Id');
IF EXISTS (
    SELECT 1
    FROM @requiredColumns AS expected
    LEFT JOIN sys.columns AS actual
      ON actual.object_id = OBJECT_ID(N'dbo.' + expected.TableName)
     AND actual.name = expected.ColumnName
    WHERE actual.column_id IS NULL)
    THROW 52805, 'A column required by the reviewed repair migration is missing.', 1;

-- These columns encode the identity, enum, revision, exact-form, and audit semantics
-- on which the data-only migration relies. A same-named column with another shape
-- is drift and must not be treated as safe.
IF EXISTS (
    SELECT 1
    FROM (VALUES
        (N'Notes', N'Id', N'int', CONVERT(bit, 0)),
        (N'Notes', N'PersonId', N'int', CONVERT(bit, 0)),
        (N'Notes', N'AgencyId', N'int', CONVERT(bit, 1)),
        (N'Notes', N'Status', N'int', CONVERT(bit, 1)),
        (N'Notes', N'NoteType', N'int', CONVERT(bit, 1)),
        (N'Notes', N'FormType', N'int', CONVERT(bit, 1)),
        (N'Notes', N'FormId', N'int', CONVERT(bit, 1)),
        (N'Notes', N'ReleaseObligationId', N'bigint', CONVERT(bit, 1)),
        (N'Notes', N'Revision', N'int', CONVERT(bit, 0)),
        (N'Notes', N'Narrative', N'nvarchar', CONVERT(bit, 0)),
        (N'Forms', N'Id', N'int', CONVERT(bit, 0)),
        (N'Forms', N'PersonId', N'int', CONVERT(bit, 0)),
        (N'Forms', N'Type', N'nvarchar', CONVERT(bit, 0)),
        (N'AuditEvents', N'EventId', N'uniqueidentifier', CONVERT(bit, 0)),
        (N'AuditEvents', N'ActorUserId', N'int', CONVERT(bit, 0)),
        (N'AuditEvents', N'Action', N'nvarchar', CONVERT(bit, 0)),
        (N'AuditEvents', N'ResourceType', N'nvarchar', CONVERT(bit, 0)),
        (N'AuditEvents', N'ResourceId', N'nvarchar', CONVERT(bit, 1)),
        (N'AuditEvents', N'MetadataJson', N'nvarchar', CONVERT(bit, 0))
    ) AS expected(TableName, ColumnName, TypeName, IsNullable)
    LEFT JOIN sys.columns AS columnRow
      ON columnRow.object_id = OBJECT_ID(N'dbo.' + expected.TableName)
     AND columnRow.name = expected.ColumnName
    LEFT JOIN sys.types AS typeRow
      ON typeRow.user_type_id = columnRow.user_type_id
    WHERE columnRow.column_id IS NULL
       OR typeRow.name <> expected.TypeName
       OR columnRow.is_nullable <> expected.IsNullable)
    THROW 52806, 'A critical repair column has an unexpected type or nullability.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.Notes')
      AND name = N'FK_Notes_Forms_FormId'
      AND delete_referential_action = 0
      AND is_disabled = 0
      AND is_not_trusted = 0)
    THROW 52807, 'The trusted restrictive Notes-to-Forms relationship is missing.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AuditEvents')
      AND name = N'IX_AuditEvents_EventId'
      AND is_unique = 1
      AND is_disabled = 0)
    THROW 52819, 'The unique audit EventId index is missing or disabled.', 1;

DECLARE @historyCount bigint = (
    SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory WITH (UPDLOCK, HOLDLOCK));
DECLARE @targetCount bigint = (
    SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory WITH (UPDLOCK, HOLDLOCK)
    WHERE MigrationId = @migrationId);
DECLARE @targetVersion nvarchar(32) = (
    SELECT MAX(ProductVersion) FROM dbo.__EFMigrationsHistory WITH (UPDLOCK, HOLDLOCK)
    WHERE MigrationId = @migrationId);
DECLARE @latestMigration nvarchar(150) = (
    SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory WITH (UPDLOCK, HOLDLOCK)
    ORDER BY MigrationId DESC);

IF NOT EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory WITH (UPDLOCK, HOLDLOCK)
    WHERE MigrationId = @previousMigrationId)
    THROW 52808, 'The preceding Work Agenda migration is not recorded.', 1;
IF @targetCount NOT IN (0, 1)
    THROW 52809, 'The repair migration history row is duplicated.', 1;
IF @targetCount = 0 AND
   (@historyCount <> @expectedCountBefore OR @latestMigration <> @previousMigrationId)
    THROW 52810, 'Migration history is not at the exact reviewed pre-repair boundary.', 1;
IF @targetCount = 1 AND
   (@historyCount <> @expectedCountAfter OR @latestMigration <> @migrationId OR
    @targetVersion <> @expectedProductVersion)
    THROW 52811, 'Recorded repair migration history does not match the reviewed boundary.', 1;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    CONVERT(bit, @targetCount) AS MigrationAlreadyApplied,
    @historyCount AS MigrationCount,
    (SELECT COUNT_BIG(*)
     FROM dbo.AuditEvents
     WHERE [Action] = N'note.scheduled-duplicate-cancelled') AS ExistingRepairAuditCount;
'@

$createRepairAuditSnapshotSql = @'
SET NOCOUNT ON;

CREATE TABLE #ExistingWorkAgendaRepairAudits
(
    EventId uniqueidentifier NOT NULL PRIMARY KEY
);
'@

$prepareSql = @'
SET NOCOUNT ON;

INSERT #ExistingWorkAgendaRepairAudits (EventId)
SELECT EventId
FROM dbo.AuditEvents WITH (UPDLOCK, HOLDLOCK)
WHERE [Action] = N'note.scheduled-duplicate-cancelled';

IF @migrationAlreadyApplied = 1
BEGIN
    DELETE FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId;
    IF @@ROWCOUNT <> 1
        THROW 52812, 'Could not stage the rollback-only post-history drift check.', 1;
END;
'@

$verificationSql = @'
SET NOCOUNT ON;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = @migrationId
      AND ProductVersion = @expectedProductVersion)
    THROW 52813, 'The repair migration history row was not written as expected.', 1;
IF (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) <> @expectedMigrationCount OR
   (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC) <>
       @migrationId
    THROW 52820, 'Migration history changed outside the exact reviewed repair boundary.', 1;

SELECT auditRow.EventId, auditRow.ResourceId, auditRow.MetadataJson
INTO #RunWorkAgendaRepairAudits
FROM dbo.AuditEvents AS auditRow
LEFT JOIN #ExistingWorkAgendaRepairAudits AS existing
    ON existing.EventId = auditRow.EventId
WHERE existing.EventId IS NULL
  AND auditRow.[Action] = N'note.scheduled-duplicate-cancelled';

IF EXISTS (
    SELECT 1
    FROM #RunWorkAgendaRepairAudits AS runAudit
    LEFT JOIN dbo.AuditEvents AS auditRow ON auditRow.EventId = runAudit.EventId
    LEFT JOIN dbo.Notes AS noteRow ON noteRow.Id = TRY_CONVERT(int, runAudit.ResourceId)
    WHERE auditRow.EventId IS NULL
       OR auditRow.ActorUserId <> 0
       OR auditRow.ResourceType <> N'Note'
       OR auditRow.CorrelationId NOT LIKE N'migration-work-agenda-dedup-%'
       OR ISJSON(auditRow.MetadataJson) <> 1
       OR JSON_VALUE(auditRow.MetadataJson, N'$.reason') <>
          N'exact-form-work-agenda-fan-out'
       OR noteRow.Id IS NULL
       OR noteRow.Status <> 4)
    THROW 52814, 'A repair audit or cancelled note failed post-migration verification.', 1;

DECLARE @eligibleCandidateGroups bigint = (
    SELECT COUNT_BIG(DISTINCT TRY_CONVERT(int, JSON_VALUE(MetadataJson, N'$.survivingNoteId')))
    FROM #RunWorkAgendaRepairAudits);
DECLARE @notesCancelled bigint = (
    SELECT COUNT_BIG(*)
    FROM #RunWorkAgendaRepairAudits AS runAudit
    INNER JOIN dbo.Notes AS noteRow
        ON noteRow.Id = TRY_CONVERT(int, runAudit.ResourceId)
       AND noteRow.Status = 4);
DECLARE @auditEventsAdded bigint = (SELECT COUNT_BIG(*) FROM #RunWorkAgendaRepairAudits);

IF @notesCancelled <> @auditEventsAdded
    THROW 52815, 'Cancellation and audit counts differ.', 1;
IF @auditEventsAdded > 0 AND @eligibleCandidateGroups = 0
    THROW 52816, 'Repair audits do not identify their surviving candidate groups.', 1;

SELECT
    @eligibleCandidateGroups AS EligibleCandidateGroups,
    @notesCancelled AS NotesCancelled,
    @auditEventsAdded AS AuditEventsAdded,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount;
'@

$postTransactionSql = @'
SET NOCOUNT ON;

DECLARE @targetCount bigint = (
    SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId);
DECLARE @repairAuditCount bigint = (
    SELECT COUNT_BIG(*) FROM dbo.AuditEvents
    WHERE [Action] = N'note.scheduled-duplicate-cancelled');

IF @targetCount <> @expectedTargetCount
    THROW 52817, 'The repair migration history state differs from the expected committed state.', 1;
IF (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) <> @expectedMigrationCount
    THROW 52821, 'The committed migration-history count differs from the expected boundary.', 1;
IF (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC) <>
   @expectedLatestMigration
    THROW 52822, 'The committed latest migration differs from the expected boundary.', 1;
IF @repairAuditCount <> @expectedRepairAuditCount
    THROW 52818, 'The repair audit count differs from the expected committed state.', 1;

SELECT @targetCount AS TargetMigrationCount, @repairAuditCount AS RepairAuditCount;
'@

try {
    $connection.Open()
    $transaction = $connection.BeginTransaction(
        [System.Data.IsolationLevel]::Serializable)
    $transactionFinished = $false
    try {
        $preflight = $connection.CreateCommand()
        $preflight.CommandTimeout = 300
        $preflight.Transaction = $transaction
        $preflight.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
        $preflight.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
        $preflight.Parameters.AddWithValue('@migrationId', $migrationId) | Out-Null
        $preflight.Parameters.AddWithValue('@previousMigrationId', $previousMigrationId) | Out-Null
        $preflight.Parameters.AddWithValue('@expectedCountBefore', $expectedMigrationCountBefore) | Out-Null
        $preflight.Parameters.AddWithValue('@expectedCountAfter', $expectedMigrationCountAfter) | Out-Null
        $preflight.Parameters.AddWithValue('@expectedProductVersion', $expectedProductVersion) | Out-Null
        $preflight.CommandText = $preflightSql
        $preflightReader = $preflight.ExecuteReader()
        try {
            if (-not $preflightReader.Read()) {
                throw 'The Work Agenda repair preflight row was not returned.'
            }
            $verifiedDatabase = $preflightReader.GetString(0)
            $verifiedEnvironment = $preflightReader.GetString(1)
            $migrationAlreadyApplied = $preflightReader.GetBoolean(2)
            $migrationCountBefore = $preflightReader.GetInt64(3)
            $repairAuditCountBefore = $preflightReader.GetInt64(4)
        }
        finally {
            $preflightReader.Close()
            $preflight.Dispose()
        }

        # A local temp table created by a parameterized SqlCommand is scoped to
        # that command's sp_executesql call and disappears before the next command.
        # Create it as an unparameterized session batch; parameterized commands can
        # then populate and consume it for the rest of this connection.
        Invoke-NonQuerySql `
            -Connection $connection `
            -Transaction $transaction `
            -Sql $createRepairAuditSnapshotSql

        $prepare = $connection.CreateCommand()
        $prepare.CommandTimeout = 300
        $prepare.Transaction = $transaction
        $prepare.Parameters.AddWithValue('@migrationAlreadyApplied', $migrationAlreadyApplied) | Out-Null
        $prepare.Parameters.AddWithValue('@migrationId', $migrationId) | Out-Null
        $prepare.CommandText = $prepareSql
        try { $prepare.ExecuteNonQuery() | Out-Null }
        finally { $prepare.Dispose() }

        Invoke-NonQuerySql -Connection $connection -Transaction $transaction -Sql $migrationSql

        $verify = $connection.CreateCommand()
        $verify.CommandTimeout = 300
        $verify.Transaction = $transaction
        $verify.Parameters.AddWithValue('@migrationId', $migrationId) | Out-Null
        $verify.Parameters.AddWithValue('@expectedProductVersion', $expectedProductVersion) | Out-Null
        $verify.Parameters.AddWithValue('@expectedMigrationCount', $expectedMigrationCountAfter) | Out-Null
        $verify.CommandText = $verificationSql
        $verifyReader = $verify.ExecuteReader()
        try {
            if (-not $verifyReader.Read()) {
                throw 'The Work Agenda repair verification row was not returned.'
            }
            $eligibleCandidateGroups = $verifyReader.GetInt64(0)
            $notesCancelled = $verifyReader.GetInt64(1)
            $auditEventsAdded = $verifyReader.GetInt64(2)
            $migrationCountDuring = $verifyReader.GetInt64(3)
        }
        finally {
            $verifyReader.Close()
            $verify.Dispose()
        }

        if ($migrationAlreadyApplied) {
            $transaction.Rollback()
            $transactionFinished = $true
            if ($eligibleCandidateGroups -ne 0 -or $notesCancelled -ne 0 -or
                $auditEventsAdded -ne 0) {
                throw "Post-history duplicate drift detected: EligibleCandidateGroups=$eligibleCandidateGroups; NotesCancelled=$notesCancelled; AuditEventsAdded=$auditEventsAdded. Every staged change was rolled back."
            }
        }
        elseif ($WhatIfOnly) {
            $transaction.Rollback()
            $transactionFinished = $true
        }
        else {
            $transaction.Commit()
            $transactionFinished = $true
        }
    }
    catch {
        if (-not $transactionFinished) {
            try { $transaction.Rollback() } catch { }
        }
        throw
    }
    finally {
        $transaction.Dispose()
    }

    $expectedTargetCount = if ($migrationAlreadyApplied -or -not $WhatIfOnly) { 1L } else { 0L }
    $expectedMigrationCount = if ($expectedTargetCount -eq 1L) {
        $expectedMigrationCountAfter
    }
    else {
        $expectedMigrationCountBefore
    }
    $expectedLatestMigration = if ($expectedTargetCount -eq 1L) {
        $migrationId
    }
    else {
        $previousMigrationId
    }
    $expectedRepairAuditCount = if ($migrationAlreadyApplied -or $WhatIfOnly) {
        $repairAuditCountBefore
    }
    else {
        $repairAuditCountBefore + $auditEventsAdded
    }

    $post = $connection.CreateCommand()
    $post.CommandTimeout = 300
    $post.Parameters.AddWithValue('@migrationId', $migrationId) | Out-Null
    $post.Parameters.AddWithValue('@expectedTargetCount', $expectedTargetCount) | Out-Null
    $post.Parameters.AddWithValue('@expectedMigrationCount', $expectedMigrationCount) | Out-Null
    $post.Parameters.AddWithValue('@expectedLatestMigration', $expectedLatestMigration) | Out-Null
    $post.Parameters.AddWithValue('@expectedRepairAuditCount', $expectedRepairAuditCount) | Out-Null
    $post.CommandText = $postTransactionSql
    $postReader = $post.ExecuteReader()
    try {
        if (-not $postReader.Read()) {
            throw 'The post-transaction verification row was not returned.'
        }
        $verifiedTargetCount = $postReader.GetInt64(0)
        $verifiedRepairAuditCount = $postReader.GetInt64(1)
    }
    finally {
        $postReader.Close()
        $post.Dispose()
    }

    [pscustomobject][ordered]@{
        DatabaseName = $verifiedDatabase
        EnvironmentName = $verifiedEnvironment
        MigrationAlreadyApplied = $migrationAlreadyApplied
        EligibleCandidateGroups = $eligibleCandidateGroups
        NotesCancelled = $notesCancelled
        AuditEventsAdded = $auditEventsAdded
        HistoryRowsStaged = if ($migrationAlreadyApplied) { 0 } else { 1 }
        HistoryRowsCommitted = if (-not $migrationAlreadyApplied -and -not $WhatIfOnly) { 1 } else { 0 }
        MigrationCountBefore = $migrationCountBefore
        MigrationCountDuringVerification = $migrationCountDuring
        TargetMigrationCountAfter = $verifiedTargetCount
        RepairAuditCountAfter = $verifiedRepairAuditCount
        RolledBack = [bool]($WhatIfOnly -or $migrationAlreadyApplied)
    }
}
finally {
    $connection.Dispose()
}
