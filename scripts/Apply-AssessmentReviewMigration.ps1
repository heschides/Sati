<#
.SYNOPSIS
    Owner-approved, identity-checked Demo assessment review migration only.
.DESCRIPTION
    Review -WhatIfOnly, apply once, then rerun for idempotency. The schema and
    history commit together under the exclusive Demo reset/mutation lock.
    Firewall changes remain user-run; see ASSESSMENT_REVIEW_RUNBOOK.md.
#>
[CmdletBinding()]
param([switch]$WhatIfOnly)
$ErrorActionPreference = 'Stop'
$sqlPath = Join-Path $PSScriptRoot 'Apply-AssessmentReviewMigration.guarded.sql'
$expectedHash = 'F544FE2360FFA9EB0726C0F14815935408352F3495A64D2121195E982B5D8B02'
if ((Get-FileHash -LiteralPath $sqlPath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Guarded SQL changed. Review and repin before controlled application.'
}
$taskGovernanceAccessToken = & az account get-access-token --resource 'https://database.windows.net/' --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($taskGovernanceAccessToken)) { throw 'Azure SQL token unavailable.' }
$connection = [System.Data.SqlClient.SqlConnection]::new(
    'Server=sati-demo-satilogica-central.database.windows.net;Database=SatiDemo;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;')
$connection.AccessToken = $taskGovernanceAccessToken.Trim()
$taskGovernanceAccessToken = $null
try {
    $connection.Open()
    $transaction = $connection.BeginTransaction()
    try {
        $command = $connection.CreateCommand()
        $command.Transaction = $transaction
        $command.CommandTimeout = 180
        $command.CommandText = @'
SET XACT_ABORT ON;
IF DB_NAME()<>N'SatiDemo' OR NOT EXISTS
 (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id=1 AND EnvironmentName=N'Demo')
 THROW 53810, 'Refusing a non-Demo database.', 1;
DECLARE @lockResult int;
EXEC @lockResult=sys.sp_getapplock @Resource=N'SatiDemo.FullReset',
 @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=60000;
IF @lockResult<0 THROW 53815, 'Demo is busy; migration did not begin.', 1;
SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;
'@
        $before = [int]$command.ExecuteScalar()
        $command.CommandText = Get-Content -LiteralPath $sqlPath -Raw
        [void]$command.Parameters.AddWithValue('@expectedDatabase', 'SatiDemo')
        [void]$command.ExecuteNonQuery()
        $command.Dispose()
        if ($WhatIfOnly) { $transaction.Rollback() } else { $transaction.Commit() }
        $verify = $connection.CreateCommand()
        $verify.CommandText = 'SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;'
        $after = [int]$verify.ExecuteScalar()
        $verify.Dispose()
        $expectedCount = if ($WhatIfOnly) { $before } else { 128 }
        if ($after -ne $expectedCount) { throw 'Persisted migration count differs from the expected outcome.' }
        [pscustomobject]@{
            Result = $(if ($WhatIfOnly) { 'ROLLBACK_REHEARSAL_PASSED' } elseif ($before -eq 128) { 'IDEMPOTENCY_VERIFIED' } else { 'ASSESSMENT_REVIEW_SCHEMA_APPLIED' })
            PersistedMigrationCount = $after
            SqlSha256 = $expectedHash
        }
    }
    catch { try { $transaction.Rollback() } catch { }; throw }
    finally { $transaction.Dispose() }
}
finally { $connection.Dispose() }
