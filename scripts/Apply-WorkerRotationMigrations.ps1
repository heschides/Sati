<#
.SYNOPSIS
    Controlled worker-rotation migration for the exact approved SatiDemo target.
.DESCRIPTION
    Defaults to -PreflightOnly: validate identity, exact history and existing schema
    without persistent target changes. A connection-local temp table canonicalizes
    the expected CHECK expression and is removed within its SQL scope.
    After separately approving the migration and pausing dispatch, polling and signature work on every host,
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
$taskRotationIsPreflight = -not ($WhatIfOnly -or $Apply)
$taskRotationSubscription = '253e5008-51c0-434b-80b9-ae3ac94bd66b'
$taskRotationSqlPath = Join-Path $PSScriptRoot 'Apply-WorkerRotationMigrations.guarded.sql'
$taskRotationExpectedHash = '2D856F4BCCBD1C78321EB3991AC52DA07540CF0C8DE25997581583606B5B69CF'
if (-not (Test-Path -LiteralPath $taskRotationSqlPath -PathType Leaf) -or
    (Get-FileHash -LiteralPath $taskRotationSqlPath -Algorithm SHA256).Hash -cne $taskRotationExpectedHash) {
    throw 'Guarded rotation SQL changed or is missing. Review and repin it before controlled use.'
}
$taskRotationSql = [IO.File]::ReadAllText($taskRotationSqlPath)
if ($taskRotationSql -match '(?im)^\s*(COMMIT|ROLLBACK|BEGIN\s+TRAN(SACTION)?)\b') {
    throw 'Guarded SQL must leave transaction ownership with this runner.'
}

$taskRotationSelectedSubscription = & az account show --query id --output tsv
if ($LASTEXITCODE -ne 0 -or
    ($taskRotationSelectedSubscription -join '').Trim() -cne $taskRotationSubscription) {
    throw 'Azure CLI must be signed in to the exact reviewed Demo subscription.'
}
$taskRotationToken = & az account get-access-token --subscription $taskRotationSubscription `
    --resource 'https://database.windows.net/' --query accessToken --output tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($taskRotationToken -join ''))) {
    $taskRotationToken = $null
    throw 'Azure SQL access token is unavailable.'
}
$taskRotationConnection = [System.Data.SqlClient.SqlConnection]::new(
    'Server=sati-demo-satilogica-central.database.windows.net;Database=SatiDemo;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;')
$taskRotationConnection.AccessToken = ($taskRotationToken -join '').Trim()
$taskRotationToken = $null

function Invoke-RotationGuard {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [bool]$ReadOnly,
        [bool]$Commit
    )
    $taskRotationTransaction = $Connection.BeginTransaction()
    try {
        $taskRotationCommand = $Connection.CreateCommand()
        try {
            $taskRotationCommand.Transaction = $taskRotationTransaction
            $taskRotationCommand.CommandTimeout = 180
            $taskRotationCommand.CommandText = $taskRotationSql
            [void]$taskRotationCommand.Parameters.Add('@expectedDatabase',[System.Data.SqlDbType]::NVarChar,128)
            $taskRotationCommand.Parameters['@expectedDatabase'].Value = 'SatiDemo'
            [void]$taskRotationCommand.Parameters.Add('@preflightOnly',[System.Data.SqlDbType]::Bit)
            $taskRotationCommand.Parameters['@preflightOnly'].Value = $ReadOnly
            $taskRotationReader = $taskRotationCommand.ExecuteReader()
            try {
                if (-not $taskRotationReader.Read()) { throw 'Guarded migration returned no verification row.' }
                $taskRotationResult = [pscustomobject]@{
                    MigrationWasApplied = $taskRotationReader.GetBoolean(0)
                    ChangesRequired = $taskRotationReader.GetBoolean(1)
                    MigrationCount = $taskRotationReader.GetInt32(2)
                }
                if ($taskRotationReader.Read() -or $taskRotationReader.NextResult()) {
                    throw 'Guarded migration returned an unexpected extra result.'
                }
            }
            finally { $taskRotationReader.Dispose() }
            $taskRotationCommand.Parameters.Clear()
            $taskRotationCommand.CommandText = @'
IF @@TRANCOUNT<>1 OR XACT_STATE()<>1 OR
   COALESCE(APPLOCK_MODE(N'public',N'SatiDemo.FullReset',N'Transaction'),N'')<>N'Exclusive'
    THROW 54014, 'Guarded migration did not preserve transaction and reset ownership.', 1;
'@
            [void]$taskRotationCommand.ExecuteNonQuery()
        }
        finally { $taskRotationCommand.Dispose() }
        if ($Commit) { $taskRotationTransaction.Commit() }
        else { $taskRotationTransaction.Rollback() }
        return $taskRotationResult
    }
    catch {
        try { $taskRotationTransaction.Rollback() } catch { }
        throw
    }
    finally { $taskRotationTransaction.Dispose() }
}

try {
    $taskRotationConnection.Open()
    $taskRotationBefore = Invoke-RotationGuard -Connection $taskRotationConnection -ReadOnly $true -Commit $false
    if ($taskRotationIsPreflight) {
        [pscustomobject]@{
            Result = 'PREFLIGHT_PASSED_NO_PERSISTENT_TARGET_CHANGES'
            DatabaseName = 'SatiDemo'
            MigrationAlreadyApplied = $taskRotationBefore.MigrationCount -eq 132
            ChangesRequired = $taskRotationBefore.ChangesRequired
            PersistedMigrationCount = $taskRotationBefore.MigrationCount
            SqlSha256 = $taskRotationExpectedHash
        }
        return
    }
    $taskRotationExecuted = Invoke-RotationGuard -Connection $taskRotationConnection -ReadOnly $false -Commit ([bool]$Apply)
    $taskRotationAfter = Invoke-RotationGuard -Connection $taskRotationConnection -ReadOnly $true -Commit $false
    if ($WhatIfOnly) {
        if ($taskRotationAfter.MigrationWasApplied -ne $taskRotationBefore.MigrationWasApplied -or
            $taskRotationAfter.MigrationCount -ne $taskRotationBefore.MigrationCount -or
            $taskRotationAfter.ChangesRequired -ne $taskRotationBefore.ChangesRequired) {
            throw 'Rollback rehearsal did not preserve the reviewed predecessor state.'
        }
        $taskRotationOutcome = 'ROLLBACK_REHEARSAL_PASSED'
    }
    else {
        if ($taskRotationAfter.MigrationCount -ne 132 -or $taskRotationAfter.ChangesRequired) {
            throw 'Persisted rotation schema or history differs from the reviewed target.'
        }
        $taskRotationOutcome = if ($taskRotationExecuted.MigrationWasApplied) {
            'WORKER_ROTATION_SCHEMA_APPLIED'
        } else { 'IDEMPOTENCY_VERIFIED' }
    }
    [pscustomobject]@{
        Result = $taskRotationOutcome
        DatabaseName = 'SatiDemo'
        PersistedMigrationCount = $taskRotationAfter.MigrationCount
        SqlSha256 = $taskRotationExpectedHash
    }
}
finally {
    $taskRotationConnection.Dispose()
    $taskRotationToken = $null
}
