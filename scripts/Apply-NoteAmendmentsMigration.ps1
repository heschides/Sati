<#
.SYNOPSIS
    Applies the reviewed amendment schema only to identity-checked SatiDemo.
.DESCRIPTION
    Run -WhatIfOnly, apply, then rerun for idempotency. Each object is existence
    guarded and its columns, keys, indexes and relationships checked. Schema and
    history commit atomically under the exclusive Demo reset/mutation lock.
    No firewall rule, configuration, narrative or original financial data changes.
#>
[CmdletBinding()]
param([switch]$WhatIfOnly)
$ErrorActionPreference = 'Stop'
$sqlPath = Join-Path $PSScriptRoot 'Apply-NoteAmendmentsMigration.guarded.sql'
$expectedHash = '642F27BBA3EBC9888BDE56FF9A2CEEF8B25868F62DAFCB7E51A47EF4BBF24769'
if ((Get-FileHash -LiteralPath $sqlPath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Guarded SQL changed. Review and repin it before controlled application.'
}
$taskAccessToken = & az account get-access-token --resource 'https://database.windows.net/' --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($taskAccessToken)) { throw 'Azure SQL token unavailable.' }
$connection = [System.Data.SqlClient.SqlConnection]::new(
    'Server=sati-demo-satilogica-central.database.windows.net;Database=SatiDemo;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;')
$connection.AccessToken = $taskAccessToken.Trim()
$taskAccessToken = $null
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
 THROW 53600, 'Refusing a non-Demo database.', 1;
DECLARE @lockResult int;
EXEC @lockResult=sys.sp_getapplock @Resource=N'SatiDemo.FullReset',
 @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=60000;
IF @lockResult<0 THROW 53604, 'Demo is busy; migration did not begin.', 1;
SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;
'@
        $before = [int]$command.ExecuteScalar()
        $command.CommandText = Get-Content -LiteralPath $sqlPath -Raw
        [void]$command.Parameters.AddWithValue('@expectedDatabase', 'SatiDemo')
        $command.ExecuteNonQuery() | Out-Null
        $command.Dispose()
        if ($WhatIfOnly) { $transaction.Rollback() } else { $transaction.Commit() }
        $verify = $connection.CreateCommand()
        $verify.CommandText = 'SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;'
        $after = [int]$verify.ExecuteScalar()
        $verify.Dispose()
        $expectedCount = if ($WhatIfOnly) { $before } else { 125 }
        if ($after -ne $expectedCount) { throw 'Persisted migration count differs from the expected outcome.' }
        [pscustomobject]@{
            Result = $(if ($WhatIfOnly) { 'ROLLBACK_REHEARSAL_PASSED' } elseif ($before -eq 125) { 'IDEMPOTENCY_VERIFIED' } else { 'NOTE_AMENDMENTS_APPLIED' })
            PersistedMigrationCount = $after
            SqlSha256 = $expectedHash
        } | Format-List
    }
    catch { try { $transaction.Rollback() } catch { }; throw }
    finally { $transaction.Dispose() }
}
finally { $connection.Dispose() }
