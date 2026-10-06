<#
.SYNOPSIS
    Owner-approved, identity-checked Demo payer configuration migration only.
.DESCRIPTION
    Review -WhatIfOnly, apply once, then rerun for idempotency. The schema and
    history commit together under the exclusive Demo reset/mutation lock.
    Firewall changes remain user-run; see PAYER_BILLING_CERTIFICATION.md.
#>
[CmdletBinding()]
param([switch]$WhatIfOnly)
$ErrorActionPreference = 'Stop'
$sqlPath = Join-Path $PSScriptRoot 'Apply-PayerBillingMigration.guarded.sql'
$expectedHash = 'C8BC119D26C647DACA32FCB1EC7A49E56B6504EBEB3BDF50EFE6623D24176E27'
if ((Get-FileHash -LiteralPath $sqlPath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Guarded SQL changed. Review and repin before controlled application.'
}
$taskPayerAccessToken = & az account get-access-token --resource 'https://database.windows.net/' --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($taskPayerAccessToken)) { throw 'Azure SQL token unavailable.' }
$connection = [System.Data.SqlClient.SqlConnection]::new(
    'Server=sati-demo-satilogica-central.database.windows.net;Database=SatiDemo;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;')
$connection.AccessToken = $taskPayerAccessToken.Trim()
$taskPayerAccessToken = $null
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
 THROW 53700, 'Refusing a non-Demo database.', 1;
DECLARE @lockResult int;
EXEC @lockResult=sys.sp_getapplock @Resource=N'SatiDemo.FullReset',
 @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=60000;
IF @lockResult<0 THROW 53703, 'Demo is busy; migration did not begin.', 1;
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
        $expectedCount = if ($WhatIfOnly) { $before } else { 126 }
        if ($after -ne $expectedCount) { throw 'Persisted migration count differs from the expected outcome.' }
        [pscustomobject]@{
            Result = $(if ($WhatIfOnly) { 'ROLLBACK_REHEARSAL_PASSED' } elseif ($before -eq 126) { 'IDEMPOTENCY_VERIFIED' } else { 'PAYER_BILLING_SCHEMA_APPLIED' })
            PersistedMigrationCount = $after
            SqlSha256 = $expectedHash
        }
    }
    catch { try { $transaction.Rollback() } catch { }; throw }
    finally { $transaction.Dispose() }
}
finally { $connection.Dispose() }
