<#
.SYNOPSIS
    Controlled worker-readiness migration for the exact approved SatiDemo target.
.DESCRIPTION
    Defaults to -PreflightOnly: validate identity, exact history and existing schema
    without persistent target changes. A connection-local temp table canonicalizes
    the expected CHECK expression and is removed within its SQL scope.
    After separately approving the migration and pausing dispatch on every host,
    run -WhatIfOnly, -Apply, then -Apply again to prove idempotency. WhatIf executes
    the real DDL in an outer transaction and rolls it back. Apply rechecks the
    persisted schema in a fresh transaction before reporting success.
    This runner never changes firewall rules, worker settings or reset baselines.
    Compatible partial objects are retained; incompatible schema/history is refused.
    Requires Azure CLI and Windows PowerShell 5.1 or PowerShell 7. No token is output.
#>
[CmdletBinding()]
param(
    [switch]$PreflightOnly,
    [switch]$WhatIfOnly,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
if (([int]$PreflightOnly.IsPresent + [int]$WhatIfOnly.IsPresent + [int]$Apply.IsPresent) -gt 1) {
    throw 'Choose only one of -PreflightOnly, -WhatIfOnly or -Apply.'
}
$taskReadinessIsPreflight = -not ($WhatIfOnly -or $Apply)
$taskReadinessSubscription = '253e5008-51c0-434b-80b9-ae3ac94bd66b'
$taskReadinessSqlPath = Join-Path $PSScriptRoot 'Apply-ClearinghousePreflightReadinessMigration.guarded.sql'
$taskReadinessExpectedHash = '6E9C8AC1614FF2E9D38894786434E58E21D82484D210BA217C6C283E056FA1AA'
if (-not (Test-Path -LiteralPath $taskReadinessSqlPath -PathType Leaf) -or
    (Get-FileHash -LiteralPath $taskReadinessSqlPath -Algorithm SHA256).Hash -cne $taskReadinessExpectedHash) {
    throw 'Guarded readiness SQL changed or is missing. Review and repin it before controlled use.'
}
$taskReadinessSql = [IO.File]::ReadAllText($taskReadinessSqlPath)
if ($taskReadinessSql -match '(?im)^\s*(COMMIT|ROLLBACK|BEGIN\s+TRAN(SACTION)?)\b') {
    throw 'Guarded SQL must leave transaction ownership with this runner.'
}

$taskReadinessSelectedSubscription = & az account show --query id --output tsv
if ($LASTEXITCODE -ne 0 -or
    ($taskReadinessSelectedSubscription -join '').Trim() -cne $taskReadinessSubscription) {
    throw 'Azure CLI must be signed in to the exact reviewed Demo subscription.'
}
$taskReadinessToken = & az account get-access-token --subscription $taskReadinessSubscription `
    --resource 'https://database.windows.net/' --query accessToken --output tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($taskReadinessToken -join ''))) {
    $taskReadinessToken = $null
    throw 'Azure SQL access token is unavailable.'
}
$taskReadinessConnection = [System.Data.SqlClient.SqlConnection]::new(
    'Server=sati-demo-satilogica-central.database.windows.net;Database=SatiDemo;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;')
$taskReadinessConnection.AccessToken = ($taskReadinessToken -join '').Trim()
$taskReadinessToken = $null

function Invoke-ReadinessGuard {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [bool]$ReadOnly,
        [bool]$Commit
    )
    $taskReadinessTransaction = $Connection.BeginTransaction()
    try {
        $taskReadinessCommand = $Connection.CreateCommand()
        try {
            $taskReadinessCommand.Transaction = $taskReadinessTransaction
            $taskReadinessCommand.CommandTimeout = 180
            $taskReadinessCommand.CommandText = $taskReadinessSql
            [void]$taskReadinessCommand.Parameters.Add('@expectedDatabase',[System.Data.SqlDbType]::NVarChar,128)
            $taskReadinessCommand.Parameters['@expectedDatabase'].Value = 'SatiDemo'
            [void]$taskReadinessCommand.Parameters.Add('@preflightOnly',[System.Data.SqlDbType]::Bit)
            $taskReadinessCommand.Parameters['@preflightOnly'].Value = $ReadOnly
            $taskReadinessReader = $taskReadinessCommand.ExecuteReader()
            try {
                if (-not $taskReadinessReader.Read()) { throw 'Guarded migration returned no verification row.' }
                $taskReadinessResult = [pscustomobject]@{
                    MigrationWasApplied = $taskReadinessReader.GetBoolean(0)
                    ChangesRequired = $taskReadinessReader.GetBoolean(1)
                    MigrationCount = $taskReadinessReader.GetInt32(2)
                }
                if ($taskReadinessReader.Read() -or $taskReadinessReader.NextResult()) {
                    throw 'Guarded migration returned an unexpected extra result.'
                }
            }
            finally { $taskReadinessReader.Dispose() }
            $taskReadinessCommand.Parameters.Clear()
            $taskReadinessCommand.CommandText = @'
IF @@TRANCOUNT<>1 OR XACT_STATE()<>1 OR
   COALESCE(APPLOCK_MODE(N'public',N'SatiDemo.FullReset',N'Transaction'),N'')<>N'Exclusive'
    THROW 53914, 'Guarded migration did not preserve transaction and reset ownership.', 1;
'@
            [void]$taskReadinessCommand.ExecuteNonQuery()
        }
        finally { $taskReadinessCommand.Dispose() }
        if ($Commit) { $taskReadinessTransaction.Commit() }
        else { $taskReadinessTransaction.Rollback() }
        return $taskReadinessResult
    }
    catch {
        try { $taskReadinessTransaction.Rollback() } catch { }
        throw
    }
    finally { $taskReadinessTransaction.Dispose() }
}

try {
    $taskReadinessConnection.Open()
    $taskReadinessBefore = Invoke-ReadinessGuard -Connection $taskReadinessConnection -ReadOnly $true -Commit $false
    if ($taskReadinessIsPreflight) {
        [pscustomobject]@{
            Result = 'PREFLIGHT_PASSED_NO_PERSISTENT_TARGET_CHANGES'
            DatabaseName = 'SatiDemo'
            MigrationAlreadyApplied = $taskReadinessBefore.MigrationCount -eq 129
            ChangesRequired = $taskReadinessBefore.ChangesRequired
            PersistedMigrationCount = $taskReadinessBefore.MigrationCount
            SqlSha256 = $taskReadinessExpectedHash
        }
        return
    }
    $taskReadinessExecuted = Invoke-ReadinessGuard -Connection $taskReadinessConnection -ReadOnly $false -Commit ([bool]$Apply)
    $taskReadinessAfter = Invoke-ReadinessGuard -Connection $taskReadinessConnection -ReadOnly $true -Commit $false
    if ($WhatIfOnly) {
        if ($taskReadinessAfter.MigrationWasApplied -ne $taskReadinessBefore.MigrationWasApplied -or
            $taskReadinessAfter.MigrationCount -ne $taskReadinessBefore.MigrationCount -or
            $taskReadinessAfter.ChangesRequired -ne $taskReadinessBefore.ChangesRequired) {
            throw 'Rollback rehearsal did not preserve the reviewed predecessor state.'
        }
        $taskReadinessOutcome = 'ROLLBACK_REHEARSAL_PASSED'
    }
    else {
        if ($taskReadinessAfter.MigrationCount -ne 129 -or $taskReadinessAfter.ChangesRequired) {
            throw 'Persisted readiness schema or history differs from the reviewed target.'
        }
        $taskReadinessOutcome = if ($taskReadinessExecuted.MigrationWasApplied) {
            'CLEARINGHOUSE_READINESS_SCHEMA_APPLIED'
        } else { 'IDEMPOTENCY_VERIFIED' }
    }
    [pscustomobject]@{
        Result = $taskReadinessOutcome
        DatabaseName = 'SatiDemo'
        PersistedMigrationCount = $taskReadinessAfter.MigrationCount
        SqlSha256 = $taskReadinessExpectedHash
    }
}
finally {
    $taskReadinessConnection.Dispose()
    $taskReadinessToken = $null
}
