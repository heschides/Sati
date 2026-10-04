<#
.SYNOPSIS
    Rehearses a point-in-time restore of SatiDemo into an owned scratch database.
.DESCRIPTION
    Source-only operator script. It is pinned to the reviewed Demo subscription,
    resource group, server and source database. Production needs a separate,
    reviewed adapter. -WhatIf makes zero Azure/SQL calls and writes no files.

    A completed restore is an ordinary Azure SQL database billed at normal rates
    until deleted. The restore and delete commands here follow Microsoft's CLI
    contract: https://learn.microsoft.com/en-us/cli/azure/sql/db?view=azure-cli-latest#az-sql-db-restore
    Cost behavior: https://learn.microsoft.com/en-us/azure/azure-sql/database/recovery-using-backups?view=azuresql

    The approved manifest must describe the chosen historical restore point,
    including the restored database's identity InstanceId, its complete EF
    migration ID set, and selected dbo table row counts. It must be prepared
    from approved historical evidence, not the mutable live source after restore.
    The script never queries SatiDemo data. Evidence contains counts and status
    only. It never changes firewall rules or grants. A workstation still needs
    separately approved SQL connectivity to inspect the scratch database.

    An uncertain restore command is never retried or cleaned automatically.
    A used RunId cannot be replayed. Investigate the named scratch resource and
    evidence manually before a new rehearsal.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)] [guid]$RunId,
    [Parameter(Mandatory)] [string]$ExpectedManifestPath,
    [switch]$RunRehearsal,
    [string]$EvidenceDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'output/demo-restore-verification'),
    # In-process test seams; never supply them to an operator rehearsal.
    [scriptblock]$AzInvoker,
    [scriptblock]$SqlInvoker,
    [scriptblock]$EvidenceWriter
)

$ErrorActionPreference = 'Stop'
$subscription = '253e5008-51c0-434b-80b9-ae3ac94bd66b'
$resourceGroup = 'rg-sati-demo'
$server = 'sati-demo-satilogica-central'
$sourceDatabase = 'SatiDemo'
$scratch = "SatiRestoreCheck_$($RunId.ToString('N'))"
$sourceId = "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Sql/servers/$server/databases/$sourceDatabase"
$scratchId = "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Sql/servers/$server/databases/$scratch"
$runIdText = $RunId.ToString('D')

if ($RunId -eq [guid]::Empty) { throw 'RunId must be a newly generated nonempty GUID.' }
if (($AzInvoker -or $SqlInvoker -or $EvidenceWriter) -and
    $env:SATI_RESTORE_VERIFICATION_TEST_MODE -cne '1') {
    throw 'Injected command callbacks are reserved for in-process tests.'
}
if (-not (Test-Path -LiteralPath $ExpectedManifestPath -PathType Leaf)) {
    throw 'The approved expected-inventory manifest was not found.'
}
$manifestBytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $ExpectedManifestPath))
$manifestHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($manifestBytes))
$manifest = [Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json -AsHashtable -DateKind String
if ($manifest.schemaVersion -ne 1 -or $manifest.sourceEnvironment -cne 'Demo' -or
    $manifest.sourceDatabase -cne $sourceDatabase -or
    $manifest.sourceServer -cne $server -or
    $manifest.sourceSubscriptionId -cne $subscription) {
    throw 'The manifest does not identify the exact reviewed SatiDemo source.'
}
if ($manifest.restorePointUtc -cnotmatch '\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z\z') {
    throw 'The manifest restorePointUtc must be exact UTC to the second (YYYY-MM-DDTHH:MM:SSZ).'
}
$restorePoint = [datetimeoffset]::ParseExact($manifest.restorePointUtc,
    "yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AssumeUniversal)
if ($restorePoint -gt [datetimeoffset]::UtcNow) { throw 'The restore point is in the future.' }
$expectedInstanceId = [guid]::Empty
if (-not [guid]::TryParse([string]$manifest.expectedInstanceId, [ref]$expectedInstanceId) -or
    $expectedInstanceId -eq [guid]::Empty) {
    throw 'The manifest needs the historical SatiDatabaseIdentity InstanceId.'
}
$expectedMigrations = @($manifest.migrationIds)
if ($expectedMigrations.Count -lt 1 -or
    @($expectedMigrations | Where-Object { $_ -cnotmatch '\A\d{14}_[A-Za-z0-9_]+\z' }).Count -gt 0 -or
    @($expectedMigrations | Select-Object -Unique).Count -ne $expectedMigrations.Count) {
    throw 'The manifest needs a complete, unique EF migration ID inventory.'
}
$expectedTables = $manifest.tableCounts
if ($expectedTables -isnot [System.Collections.IDictionary] -or $expectedTables.Count -lt 1) {
    throw 'The manifest needs at least one expected dbo table count.'
}
foreach ($table in $expectedTables.Keys) {
    if ($table -cnotmatch '\Adbo\.[A-Za-z_][A-Za-z0-9_]*\z' -or
        ($expectedTables[$table] -isnot [long] -and $expectedTables[$table] -isnot [int]) -or
        [long]$expectedTables[$table] -lt 0) {
        throw 'The manifest has an invalid table name or count.'
    }
}

function Convert-RestoreReaderRows([System.Data.IDataReader]$Reader) {
    $rows = [Collections.Generic.List[object]]::new()
    while ($Reader.Read()) {
        $row = [ordered]@{}
        for ($index = 0; $index -lt $Reader.FieldCount; $index++) {
            $row[$Reader.GetName($index)] = $Reader.GetValue($index)
        }
        $rows.Add($row)
    }
    # PowerShell must enumerate these rows. A unary comma wraps all rows into
    # one Object[] and silently corrupts migration and identity comparisons.
    return $rows.ToArray()
}

$plan = [pscustomobject]@{
    Mode = 'WhatIf'; Subscription = $subscription; Server = $server
    SourceDatabase = $sourceDatabase; ScratchDatabase = $scratch
    RestorePointUtc = $manifest.restorePointUtc; ExpectedTableCount = $expectedTables.Count
    ExpectedMigrationCount = $expectedMigrations.Count
    ManifestSha256 = $manifestHash
}
if ($WhatIfPreference) { return $plan }
if (-not $RunRehearsal) { throw 'A real restore requires the explicit -RunRehearsal switch.' }
if (-not $PSCmdlet.ShouldProcess($scratch, 'Restore, verify and delete owned Demo scratch database')) {
    return $plan
}

# This fixed, checkout-local ledger is independent of the caller's evidence
# destination. It prevents the same RunId being replayed with a new path.
# A different checkout must reconcile its own ledger with Azure manually.
$runLedgerDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'output/demo-restore-verification/runs'
[void][System.IO.Directory]::CreateDirectory($runLedgerDirectory)
$runLedgerPath = Join-Path $runLedgerDirectory "$runIdText.json"
try {
    $reservation = [ordered]@{
        runId = $runIdText; scratchDatabase = $scratch
        reservedAtUtc = [datetimeoffset]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 4
    $reservationBytes = [Text.Encoding]::UTF8.GetBytes($reservation)
    $reservationStream = [System.IO.FileStream]::new($runLedgerPath,
        [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try { $reservationStream.Write($reservationBytes, 0, $reservationBytes.Length) }
    finally { $reservationStream.Dispose() }
}
catch [System.IO.IOException] {
    throw 'This RunId is reserved in the checkout-local run ledger. Refusing to replay the restore.'
}

function Invoke-AzJson([string[]]$Arguments) {
    $output = if ($AzInvoker) { & $AzInvoker $Arguments } else { & az @Arguments }
    if (-not $AzInvoker -and $LASTEXITCODE -ne 0) {
        throw 'Azure CLI command failed; see the local evidence for the recovery state.'
    }
    return ($output | Out-String | ConvertFrom-Json -AsHashtable)
}

function Test-ExactResource($Resource, [string]$ExpectedId) {
    return $null -ne $Resource -and
        [string]::Equals([string]$Resource.id, $ExpectedId, [StringComparison]::OrdinalIgnoreCase)
}

function Test-ScratchOwnership($Resource) {
    return (Test-ExactResource $Resource $scratchId) -and
        [string]::Equals([string]$Resource.tags.SatiRestoreRunId, $runIdText,
            [StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals([string]$Resource.tags.SatiRestorePurpose, 'Verification',
            [StringComparison]::Ordinal)
}

function Invoke-RestoreSqlProbe([string]$DatabaseName, [string]$AccessToken, [string[]]$TableNames) {
    if ($SqlInvoker) { return (& $SqlInvoker $DatabaseName $AccessToken $TableNames) }
    $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
    $builder['Data Source'] = "$server.database.windows.net"
    $builder['Initial Catalog'] = $DatabaseName
    $builder['Application Name'] = 'Sati Demo restore verification'
    $builder['Connect Timeout'] = 45
    $builder['Encrypt'] = $true
    $builder['TrustServerCertificate'] = $false
    $connection = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    $connection.AccessToken = $AccessToken
    try {
        $connection.Open()
        function Invoke-ReadOnlyQuery([string]$Sql) {
            $command = $connection.CreateCommand()
            $command.CommandText = $Sql
            $command.CommandTimeout = 120
            try {
                $reader = $command.ExecuteReader()
                try {
                    return Convert-RestoreReaderRows $reader
                }
                finally { $reader.Dispose() }
            }
            finally { $command.Dispose() }
        }
        $identity = @(Invoke-ReadOnlyQuery 'SELECT Id, DB_NAME() AS DatabaseName, EnvironmentName, CONVERT(nvarchar(36), InstanceId) AS InstanceId FROM dbo.SatiDatabaseIdentity;')
        $migrations = @(Invoke-ReadOnlyQuery 'SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;')
        $counts = @{}
        foreach ($table in $TableNames) {
            # Names were restricted to simple dbo identifiers before any Azure call.
            $name = $table.Substring(4)
            $rows = @(Invoke-ReadOnlyQuery "SELECT COUNT_BIG(*) AS RowCount FROM dbo.[$name];")
            if ($rows.Count -ne 1) { throw 'A requested table count did not return one row.' }
            $counts[$table] = [long]$rows[0].RowCount
        }
        return @{
            IdentityRows = $identity
            MigrationIds = @($migrations | ForEach-Object { [string]$_.MigrationId })
            TableCounts = $counts
        }
    }
    finally { $connection.Dispose() }
}

$evidencePath = Join-Path $EvidenceDirectory "demo-restore-$runIdText.json"
[void][System.IO.Directory]::CreateDirectory($EvidenceDirectory)
$evidence = [ordered]@{
    schemaVersion = 1; runId = $runIdText; scope = 'Demo'; sourceDatabase = $sourceDatabase
    scratchDatabase = $scratch; restorePointUtc = $manifest.restorePointUtc
    manifestSha256 = $manifestHash; startedAtUtc = [datetimeoffset]::UtcNow.ToString('o')
    state = 'Started'; identityMatched = $false; migrationCount = $null
    tableCounts = @{}; cleanup = 'NotStarted'; evidenceWriteFailed = $false
    failureType = $null
}
try {
    $initialJson = $evidence | ConvertTo-Json -Depth 8
    $bytes = [Text.Encoding]::UTF8.GetBytes($initialJson)
    $stream = [System.IO.FileStream]::new($evidencePath, [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length) }
    finally { $stream.Dispose() }
}
catch [System.IO.IOException] {
    throw 'This RunId already has local evidence. Refusing to overwrite or replay the restore.'
}
$script:evidenceWriteFailed = $false
function Save-Evidence {
    try {
        $evidence.updatedAtUtc = [datetimeoffset]::UtcNow.ToString('o')
        $json = $evidence | ConvertTo-Json -Depth 8
        if ($EvidenceWriter) { & $EvidenceWriter $evidencePath $json }
        else { [System.IO.File]::WriteAllText($evidencePath, $json) }
        return $true
    }
    catch {
        $script:evidenceWriteFailed = $true
        $evidence.evidenceWriteFailed = $true
        return $false
    }
}

$restoreCommandStarted = $false
$restoreCommandSucceeded = $false
$ownedScratch = $false
$validationError = $null
try {
    $account = Invoke-AzJson @('account', 'show', '-o', 'json')
    if ($account.id -cne $subscription) { throw 'The active subscription is not the reviewed Demo subscription.' }
    $source = Invoke-AzJson @('sql', 'db', 'show', '--name', $sourceDatabase,
        '--resource-group', $resourceGroup, '--server', $server,
        '--subscription', $subscription, '-o', 'json')
    if (-not (Test-ExactResource $source $sourceId)) { throw 'The exact SatiDemo source was not found.' }
    $databases = @(Invoke-AzJson @('sql', 'db', 'list', '--resource-group', $resourceGroup,
        '--server', $server, '--subscription', $subscription, '-o', 'json'))
    if (@($databases | Where-Object { $_.name -ieq $scratch }).Count -gt 0) {
        throw 'The unique scratch database already exists. Refusing to overwrite or reuse it.'
    }

    $evidence.state = 'RestoreRequested'
    if (-not (Save-Evidence)) {
        throw 'Evidence could not be persisted before restore; no restore was requested.'
    }
    # --tags applies ownership on creation; inherited source tags are not assumed.
    $restoreCommandStarted = $true
    $restored = Invoke-AzJson @('sql', 'db', 'restore', '--name', $sourceDatabase,
        '--dest-name', $scratch, '--resource-group', $resourceGroup, '--server', $server,
        '--subscription', $subscription, '--time', $manifest.restorePointUtc.TrimEnd('Z'),
        '--tags', "SatiRestoreRunId=$runIdText", 'SatiRestorePurpose=Verification', '-o', 'json')
    $restoreCommandSucceeded = $true
    $evidence.state = 'RestoreReturned'
    [void](Save-Evidence)
    if (-not (Test-ExactResource $restored $scratchId)) {
        throw 'Restore returned an unexpected resource identity.'
    }
    $scratchResource = Invoke-AzJson @('sql', 'db', 'show', '--name', $scratch,
        '--resource-group', $resourceGroup, '--server', $server,
        '--subscription', $subscription, '-o', 'json')
    if (-not (Test-ScratchOwnership $scratchResource)) {
        throw 'Scratch ownership tag or resource identity did not match this run.'
    }
    $ownedScratch = $true

    $tokenResponse = Invoke-AzJson @('account', 'get-access-token',
        '--resource', 'https://database.windows.net/', '--subscription', $subscription, '-o', 'json')
    $accessToken = [string]$tokenResponse.accessToken
    if ([string]::IsNullOrWhiteSpace($accessToken)) { throw 'An Entra SQL access token was not returned.' }
    try { $probe = Invoke-RestoreSqlProbe $scratch $accessToken @($expectedTables.Keys) }
    finally { $accessToken = $null; $tokenResponse = $null }

    $identityRows = @($probe.IdentityRows)
    if ($identityRows.Count -ne 1 -or
        [int]$identityRows[0].Id -ne 1 -or
        $identityRows[0].DatabaseName -cne $scratch -or
        $identityRows[0].EnvironmentName -cne 'Demo' -or
        [string]$identityRows[0].InstanceId -ine $expectedInstanceId.ToString('D')) {
        throw 'Restored database identity did not match the approved historical Demo identity.'
    }
    $evidence.identityMatched = $true
    $actualMigrations = @($probe.MigrationIds)
    $sortedExpected = @($expectedMigrations | Sort-Object -CaseSensitive)
    $sortedActual = @($actualMigrations | Sort-Object -CaseSensitive)
    if ($sortedActual.Count -ne $sortedExpected.Count -or
        ((Compare-Object $sortedActual $sortedExpected -CaseSensitive).Count -ne 0)) {
        throw 'Restored EF migration IDs differ from the approved historical inventory.'
    }
    $evidence.migrationCount = $actualMigrations.Count
    foreach ($table in $expectedTables.Keys) {
        if (-not $probe.TableCounts.ContainsKey($table) -or
            [long]$probe.TableCounts[$table] -ne [long]$expectedTables[$table]) {
            throw 'A restored table row count differs from the approved historical inventory.'
        }
        $evidence.tableCounts[$table] = [long]$probe.TableCounts[$table]
    }
    $evidence.state = 'Verified'
    [void](Save-Evidence)
}
catch {
    $validationError = $_
    $evidence.failureType = $_.Exception.GetType().Name
    if ($restoreCommandStarted -and -not $restoreCommandSucceeded) {
        $evidence.state = 'RestoreCreationUncertain'
    }
    elseif (-not $ownedScratch -and $restoreCommandSucceeded) {
        $evidence.state = 'OwnershipUnconfirmed'
    }
    elseif (-not $restoreCommandStarted) { $evidence.state = 'PreflightFailed' }
    else { $evidence.state = 'VerificationFailed' }
    [void](Save-Evidence)
}

if ($ownedScratch) {
    try {
        # Re-read ownership immediately before delete; no name-only cleanup.
        $current = Invoke-AzJson @('sql', 'db', 'show', '--name', $scratch,
            '--resource-group', $resourceGroup, '--server', $server,
            '--subscription', $subscription, '-o', 'json')
        if (-not (Test-ScratchOwnership $current)) {
            throw 'Scratch ownership changed before cleanup; deletion refused.'
        }
        [void](Invoke-AzJson @('sql', 'db', 'delete', '--name', $scratch,
            '--resource-group', $resourceGroup, '--server', $server,
            '--subscription', $subscription, '--yes', '-o', 'json'))
        $remaining = @(Invoke-AzJson @('sql', 'db', 'list', '--resource-group', $resourceGroup,
            '--server', $server, '--subscription', $subscription, '-o', 'json'))
        if (@($remaining | Where-Object { $_.name -ieq $scratch }).Count -gt 0) {
            throw 'The scratch database still appears after delete; cleanup is unverified.'
        }
        $evidence.cleanup = 'DeletedOwnedScratch'
        if ($null -eq $validationError) { $evidence.state = 'Completed' }
        [void](Save-Evidence)
    }
    catch {
        $evidence.cleanup = 'FailedOrOwnershipUncertain'
        $evidence.state = 'CleanupFailed'
        $evidence.failureType = $_.Exception.GetType().Name
        [void](Save-Evidence)
        throw "Restore verification cleanup failed; inspect exact scratch $scratch and evidence $evidencePath (which may be incomplete)."
    }
}
if ($null -ne $validationError) {
    $evidenceWarning = if ($script:evidenceWriteFailed) { ' Local evidence writes also failed.' } else { '' }
    throw "Restore verification ended in $($evidence.state); inspect evidence $evidencePath and exact scratch $scratch.$evidenceWarning $($validationError.Exception.Message)"
}
if ($script:evidenceWriteFailed) {
    throw "Restore verification completed and owned scratch cleanup ran, but a local evidence write failed. Inspect $evidencePath."
}
return [pscustomobject]$evidence
