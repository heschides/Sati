<#
.SYNOPSIS
    Applies and verifies the 2026-09-15 annual compliance and service-date billing schema.

.DESCRIPTION
    Covers three migrations, applied in this order:

      20260915004541_CorrectAnnualComplianceAndBillingPolicy   Form.TargetEffectiveDate and the
                                                               corrected deadlines, opening and
                                                               attestation evidence, append-only
                                                               agency policy versions, exact note
                                                               exceptions, recipient release
                                                               obligations, signature projections,
                                                               and recovery records
      20260915013852_AddBillingCompliancePolicyReviewFlags     dbo.BillingCompliancePolicyReviewFlags
      20260915153000_AllowSupersedingBillingComplianceRecovery recovery NoteId uniqueness moves to
                                                               decision + note

    This runner is deliberately narrower than `dotnet ef database update`. The DDL below is EF's
    own generated idempotent script for exactly these three migrations, extracted verbatim rather
    than retyped, so each block's own `dbo.__EFMigrationsHistory` guard is already correct. This
    script adds what that generated script does not: fail-closed database and environment identity
    checks, an explicit precondition that the chain's prior migration is applied, one outer
    transaction instead of three auto-committing ones, and the dry-run/real-run/rerun discipline the
    rest of this directory uses.

    The first migration converts existing data. It carries its own fail-closed guards and aborts on
    unknown form types, consumers without an effective date, agencies without settings, anomalous
    legacy deadlines, duplicate (PersonId, Type, TargetEffectiveDate) obligations, and legacy
    blanket note overrides that cannot become attested blocker-specific exceptions. An abort leaves
    the database unchanged; reconcile the named rows and run again.

    Nothing here reads or writes dbo.Scratchpad or dbo.ScratchpadComments; the runner counts both
    before and after inside its transaction and aborts if either changes.

    Local SatiProduction receives a verified full backup before the real schema change when it
    holds records. Azure SatiDemo relies on configured point-in-time recovery and requires an Entra
    access token plus the separately operator-managed temporary firewall rule. This script never
    adds, alters, or removes a firewall rule.

.EXAMPLE
    ./scripts/Apply-Release1311Migrations.ps1 -DatabaseName SatiProduction -InspectOnly
    ./scripts/Apply-Release1311Migrations.ps1 -DatabaseName SatiProduction -WhatIfOnly
    ./scripts/Apply-Release1311Migrations.ps1 -DatabaseName SatiProduction

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    ./scripts/Apply-Release1311Migrations.ps1 -DatabaseName SatiDemo `
        -SqlServer sati-demo-satilogica-central.database.windows.net -AccessToken $token -WhatIfOnly
#>
[CmdletBinding()]
param(
    [ValidateSet('SatiDemo', 'SatiProduction')]
    [Parameter(Mandatory)]
    [string]$DatabaseName,

    [string]$SqlServer = '(localdb)\MSSQLLocalDB',

    [string]$AccessToken,

    [switch]$InspectOnly,

    [switch]$WhatIfOnly,

    # The correction migration adds Forms.TargetEffectiveDate and then reads it from its own
    # hand-written SQL. SQL Server compiles a batch before running it, and there is no legal
    # place for a GO separator inside the generated script's guarded IF blocks, so the script
    # body cannot apply this range. EF sends each operation as its own command, which is the
    # only applier that works here. The identity, chain, and scratchpad guards below still run
    # before and after, so what the playbook's guarded script protects is still enforced.
    [switch]$UseEntityFrameworkApplier
)

$ErrorActionPreference = 'Stop'
if ($InspectOnly -and $WhatIfOnly) {
    throw 'Choose either -InspectOnly or -WhatIfOnly, not both.'
}

$expectedEnvironment = if ($DatabaseName -ceq 'SatiDemo') { 'Demo' } else { 'Production' }
$priorMigrationId = '20260914030703_AddGoalProgressToCaseNotes'
$correctionMigration = '20260915004541_CorrectAnnualComplianceAndBillingPolicy'
$reviewFlagMigration = '20260915013852_AddBillingCompliancePolicyReviewFlags'
$recoveryMigration = '20260915153000_AllowSupersedingBillingComplianceRecovery'
$targetMigrationIds = @($correctionMigration, $reviewFlagMigration, $recoveryMigration)
$usesAccessToken = -not [string]::IsNullOrWhiteSpace($AccessToken)

if ($DatabaseName -ceq 'SatiDemo' -and -not $usesAccessToken) {
    throw 'SatiDemo requires an Entra access token; integrated workstation credentials are not permitted.'
}
if ($DatabaseName -ceq 'SatiProduction' -and $usesAccessToken) {
    throw 'The local SatiProduction runner does not accept an Azure access token.'
}

$bodyPath = Join-Path $PSScriptRoot 'Apply-Release1311Migrations.body.sql'
if (-not (Test-Path -LiteralPath $bodyPath) -and -not $UseEntityFrameworkApplier) {
    throw "The generated migration body $bodyPath is missing. Regenerate it before running this script."
}
$body = if ($UseEntityFrameworkApplier) { '' } else { Get-Content -Raw -LiteralPath $bodyPath }
if ($UseEntityFrameworkApplier -and $WhatIfOnly) {
    throw 'EF applies each migration in its own committed transaction, so it has no rollback-only mode. Use -InspectOnly to look first.'
}
if ($body -match '(?i)Scratchpad') {
    throw 'The migration body references a scratchpad table. Refusing to run it.'
}

# EF generates one transaction per migration plus GO separators. When this runner executes
# the body itself it wants the three migrations to be one all-or-nothing change, so its own
# transaction replaces them: split on the batch separators and drop EF's BEGIN/COMMIT lines.
$bodyBatches = @(
    $body -split '(?im)^\s*GO\s*$' |
        ForEach-Object {
            ($_ -split "`r?`n" |
                Where-Object { $_ -notmatch '(?i)^\s*(BEGIN TRANSACTION|COMMIT)\s*;\s*$' }) -join [Environment]::NewLine
        } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if (-not $UseEntityFrameworkApplier) {
    if ($bodyBatches.Count -lt 1) {
        throw 'The generated migration body produced no executable batches.'
    }
    foreach ($batch in $bodyBatches) {
        if ($batch -match '(?im)^\s*(BEGIN TRANSACTION|COMMIT)\s*;\s*$') {
            throw 'A migration batch still manages its own transaction; this runner must own it.'
        }
    }
    $expectedMigrationBlocks = $targetMigrationIds.Count
    $recordedBlocks = ([regex]::Matches($body, 'INSERT INTO \[__EFMigrationsHistory\]')).Count
    if ($recordedBlocks -ne $expectedMigrationBlocks) {
        throw "The migration body records $recordedBlocks migrations; this runner expects exactly $expectedMigrationBlocks."
    }
}

$connectionString = if ($usesAccessToken) {
    "Server=$SqlServer;Database=$DatabaseName;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;"
}
else {
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=30;"
}

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
if ($usesAccessToken) {
    $connection.AccessToken = $AccessToken
}
$connection.Open()

$backupPath = $null
try {
    $preflight = $connection.CreateCommand()
    $preflight.CommandTimeout = 180
    $preflight.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
    $preflight.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
    $preflight.Parameters.AddWithValue('@priorMigrationId', $priorMigrationId) | Out-Null
    $preflight.Parameters.AddWithValue('@correctionMigration', $correctionMigration) | Out-Null
    $preflight.Parameters.AddWithValue('@reviewFlagMigration', $reviewFlagMigration) | Out-Null
    $preflight.Parameters.AddWithValue('@recoveryMigration', $recoveryMigration) | Out-Null
    $preflight.CommandText = @'
SET NOCOUNT ON;

IF DB_NAME() COLLATE Latin1_General_100_BIN2 <> @expectedDatabase COLLATE Latin1_General_100_BIN2
    THROW 52400, 'The connected database is not the exact database requested.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 52401, 'dbo.SatiDatabaseIdentity is missing.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1
      AND EnvironmentName COLLATE Latin1_General_100_BIN2 = @expectedEnvironment COLLATE Latin1_General_100_BIN2)
    THROW 52402, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 52403, 'dbo.__EFMigrationsHistory is missing.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL OR OBJECT_ID(N'dbo.People', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Forms', N'U') IS NULL OR OBJECT_ID(N'dbo.Notes', N'U') IS NULL
    THROW 52404, 'A required existing Sati table is missing.', 1;

-- Counts the operator needs before deciding to proceed: how much annual data the
-- conversion will touch, and whether any of it is already converted.
SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    (SELECT COUNT_BIG(*) FROM dbo.People) AS PersonCount,
    (SELECT COUNT_BIG(*) FROM dbo.Forms) AS FormCount,
    (SELECT COUNT_BIG(*) FROM dbo.Notes) AS NoteCount,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@priorMigrationId) THEN 1 ELSE 0 END AS bit) AS PriorMigrationApplied,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@correctionMigration) THEN 1 ELSE 0 END AS bit) AS CorrectionHistory,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@reviewFlagMigration) THEN 1 ELSE 0 END AS bit) AS ReviewFlagHistory,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@recoveryMigration) THEN 1 ELSE 0 END AS bit) AS RecoveryHistory,
    CAST(CASE WHEN COL_LENGTH(N'dbo.Forms', N'TargetEffectiveDate') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS TargetColumn,
    CAST(CASE WHEN OBJECT_ID(N'dbo.ReleaseObligations', N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS ReleaseObligationTable,
    CAST(CASE WHEN OBJECT_ID(N'dbo.BillingCompliancePolicyVersions', N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS PolicyVersionTable,
    CAST(CASE WHEN OBJECT_ID(N'dbo.BillingCompliancePolicyReviewFlags', N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS ReviewFlagTable,
    CAST(CASE WHEN OBJECT_ID(N'dbo.BillingComplianceRecoveryDecisions', N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS RecoveryDecisionTable,
    (SELECT COUNT_BIG(*) FROM dbo.Notes WHERE ComplianceOverride = 1) AS LegacyOverrideNoteCount;
'@

    $preflightReader = $preflight.ExecuteReader()
    if (-not $preflightReader.Read()) {
        throw 'The migration preflight row was not returned.'
    }
    $inspection = [pscustomobject][ordered]@{
        DatabaseName = $preflightReader.GetString(0)
        EnvironmentName = $preflightReader.GetString(1)
        PersonCount = $preflightReader.GetInt64(2)
        FormCount = $preflightReader.GetInt64(3)
        NoteCount = $preflightReader.GetInt64(4)
        MigrationCount = $preflightReader.GetInt64(5)
        PriorMigrationApplied = $preflightReader.GetBoolean(6)
        CorrectionHistory = $preflightReader.GetBoolean(7)
        ReviewFlagHistory = $preflightReader.GetBoolean(8)
        RecoveryHistory = $preflightReader.GetBoolean(9)
        TargetColumn = $preflightReader.GetBoolean(10)
        ReleaseObligationTable = $preflightReader.GetBoolean(11)
        PolicyVersionTable = $preflightReader.GetBoolean(12)
        ReviewFlagTable = $preflightReader.GetBoolean(13)
        RecoveryDecisionTable = $preflightReader.GetBoolean(14)
        LegacyOverrideNoteCount = $preflightReader.GetInt64(15)
    }
    $preflightReader.Close()

    if ($InspectOnly) {
        $inspection
        return
    }

    if (-not $inspection.PriorMigrationApplied) {
        throw "The migration immediately before this range ($priorMigrationId) is not recorded as applied. Refusing rather than guessing the database state."
    }

    $hasPendingHistory = -not ($inspection.CorrectionHistory -and $inspection.ReviewFlagHistory -and $inspection.RecoveryHistory)
    if (-not $WhatIfOnly -and -not $usesAccessToken -and $hasPendingHistory -and
        ($inspection.PersonCount -gt 0 -or $inspection.NoteCount -gt 0)) {
        $backupDirectory = Join-Path `
            ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) `
            'Sati\schema-backups'
        [System.IO.Directory]::CreateDirectory($backupDirectory) | Out-Null
        $stamp = Get-Date -Format 'yyyy-MM-dd-HHmmss'
        $backupPath = Join-Path $backupDirectory "$DatabaseName-$stamp.bak"
        $escapedBackupPath = $backupPath.Replace("'", "''", [StringComparison]::Ordinal)

        $backup = $connection.CreateCommand()
        $backup.CommandTimeout = 900
        $backup.CommandText = "BACKUP DATABASE [$DatabaseName] TO DISK = '$escapedBackupPath' WITH INIT, SKIP, NOFORMAT, CHECKSUM;"
        $backup.ExecuteNonQuery() | Out-Null

        $verifyBackup = $connection.CreateCommand()
        $verifyBackup.CommandTimeout = 900
        $verifyBackup.CommandText = "RESTORE VERIFYONLY FROM DISK = '$escapedBackupPath' WITH CHECKSUM;"
        $verifyBackup.ExecuteNonQuery() | Out-Null
    }

    # One transaction owns the guards, all three migrations, and the verification, so a
    # failure anywhere leaves the database exactly as it was.
    $transaction = $connection.BeginTransaction()
    $committed = $false
    try {
        $guard = $connection.CreateCommand()
        $guard.Transaction = $transaction
        $guard.CommandTimeout = 300
        $guard.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
        $guard.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
        $guard.Parameters.AddWithValue('@priorMigrationId', $priorMigrationId) | Out-Null
        $guard.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Fail closed on identity, then on chain position. Every statement below assumes the
-- chain is contiguous up to the migration before this range.
IF DB_NAME() COLLATE Latin1_General_100_BIN2 <> @expectedDatabase COLLATE Latin1_General_100_BIN2
    THROW 52400, 'The connected database is not the exact database requested.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1
      AND EnvironmentName COLLATE Latin1_General_100_BIN2 = @expectedEnvironment COLLATE Latin1_General_100_BIN2)
    THROW 52402, 'The database identity marker does not match the requested environment.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @priorMigrationId)
    THROW 52405, 'The migration immediately before this script''s range is not recorded as applied.', 1;

-- Counted through sp_executesql so a missing table is absence, not a compile error:
-- a direct reference binds even inside a branch that never runs.
DECLARE @pad bigint = -1, @comments bigint = -1;
IF OBJECT_ID(N'dbo.Scratchpad', N'U') IS NOT NULL
    EXEC sp_executesql N'SELECT @out = COUNT_BIG(*) FROM dbo.Scratchpad',
        N'@out bigint OUTPUT', @out = @pad OUTPUT;
IF OBJECT_ID(N'dbo.ScratchpadComments', N'U') IS NOT NULL
    EXEC sp_executesql N'SELECT @out = COUNT_BIG(*) FROM dbo.ScratchpadComments',
        N'@out bigint OUTPUT', @out = @comments OUTPUT;
SELECT CONCAT(@pad, N'/', @comments) AS ScratchpadRows;
'@
        $scratchpadRowsBefore = [string]$guard.ExecuteScalar()

        if ($UseEntityFrameworkApplier) {
            # The guards above have passed. Release this connection's transaction before
            # EF opens its own: the two cannot hold the same schema locks.
            $transaction.Rollback()

            $efConnection = if ($usesAccessToken) {
                "Server=$SqlServer;Database=$DatabaseName;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connect Timeout=90;"
            }
            else {
                $connectionString
            }
            $repositoryRoot = Split-Path -Parent $PSScriptRoot
            $efOutput = & dotnet ef database update $recoveryMigration `
                --project (Join-Path $repositoryRoot 'Sati.Persistence') `
                --startup-project (Join-Path $repositoryRoot 'Sati.Persistence') `
                --context SatiContext --no-build --connection $efConnection 2>&1
            $efOutput | ForEach-Object { Write-Verbose $_ }
            if ($LASTEXITCODE -ne 0) {
                $efOutput | ForEach-Object { Write-Host $_ }
                throw "dotnet ef database update failed with exit code $LASTEXITCODE."
            }

            $transaction = $connection.BeginTransaction()
        }
        else {
            foreach ($batch in $bodyBatches) {
                $migrate = $connection.CreateCommand()
                $migrate.Transaction = $transaction
                $migrate.CommandTimeout = 1800
                $migrate.CommandText = $batch
                $migrate.ExecuteNonQuery() | Out-Null
            }
        }

        $verify = $connection.CreateCommand()
        $verify.Transaction = $transaction
        $verify.CommandTimeout = 300
        $verify.CommandText = @'
SET NOCOUNT ON;

DECLARE @pad bigint = -1, @comments bigint = -1;
IF OBJECT_ID(N'dbo.Scratchpad', N'U') IS NOT NULL
    EXEC sp_executesql N'SELECT @out = COUNT_BIG(*) FROM dbo.Scratchpad',
        N'@out bigint OUTPUT', @out = @pad OUTPUT;
IF OBJECT_ID(N'dbo.ScratchpadComments', N'U') IS NOT NULL
    EXEC sp_executesql N'SELECT @out = COUNT_BIG(*) FROM dbo.ScratchpadComments',
        N'@out bigint OUTPUT', @out = @comments OUTPUT;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory
     WHERE MigrationId IN (
        N'20260915004541_CorrectAnnualComplianceAndBillingPolicy',
        N'20260915013852_AddBillingCompliancePolicyReviewFlags',
        N'20260915153000_AllowSupersedingBillingComplianceRecovery')) AS TargetMigrationsRecorded,
    CAST(CASE WHEN COL_LENGTH(N'dbo.Forms', N'TargetEffectiveDate') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS TargetColumn,
    CAST(CASE WHEN OBJECT_ID(N'dbo.ReleaseObligations', N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS ReleaseObligationTable,
    CAST(CASE WHEN OBJECT_ID(N'dbo.BillingCompliancePolicyReviewFlags', N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS ReviewFlagTable,
    CONCAT(@pad, N'/', @comments) AS ScratchpadRows;
'@
        $reader = $verify.ExecuteReader()
        if (-not $reader.Read()) {
            throw 'The migration verification row was not returned.'
        }
        $result = [pscustomobject][ordered]@{
            DatabaseName = $reader.GetString(0)
            EnvironmentName = $reader.GetString(1)
            MigrationCount = $reader.GetInt64(2)
            TargetMigrationsRecorded = $reader.GetInt64(3)
            TargetMigrationsExpected = $targetMigrationIds.Count
            TargetColumn = $reader.GetBoolean(4)
            ReleaseObligationTable = $reader.GetBoolean(5)
            ReviewFlagTable = $reader.GetBoolean(6)
            ScratchpadRowsBefore = $scratchpadRowsBefore
            ScratchpadRowsAfter = $reader.GetString(7)
            RolledBack = [bool]$WhatIfOnly
            BackupPath = $backupPath
        }
        $reader.Close()

        # The scratchpad is working staff content and outside this release's scope.
        # Proving the row count is unchanged is cheap, and a surprise must stop the run.
        if ($result.ScratchpadRowsAfter -ne $result.ScratchpadRowsBefore) {
            throw 'This migration changed scratchpad rows, which it must never do.'
        }
        if ($result.TargetMigrationsRecorded -ne $targetMigrationIds.Count) {
            throw "Expected all $($targetMigrationIds.Count) target migrations recorded, found $($result.TargetMigrationsRecorded)."
        }
        if (-not ($result.TargetColumn -and $result.ReleaseObligationTable -and $result.ReviewFlagTable)) {
            throw 'The migrations ran without producing the expected schema.'
        }

        if ($WhatIfOnly) {
            $transaction.Rollback()
        }
        else {
            $transaction.Commit()
            $committed = $true
        }
    }
    finally {
        if (-not $committed -and -not $WhatIfOnly) {
            try { $transaction.Rollback() } catch { }
        }
        $transaction.Dispose()
    }
    $result

}
finally {
    $connection.Dispose()
}
