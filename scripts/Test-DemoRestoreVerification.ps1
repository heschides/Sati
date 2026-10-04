[CmdletBinding()]
param([string]$RestoreScriptPath)

# All Azure and SQL operations are in-process callbacks. This script never
# logs in to Azure, opens SQL, or restores a database.
$ErrorActionPreference = 'Stop'
$env:SATI_RESTORE_VERIFICATION_TEST_MODE = '1'
$scriptPath = if ([string]::IsNullOrWhiteSpace($RestoreScriptPath)) {
    Join-Path $PSScriptRoot 'Invoke-DemoRestoreVerification.ps1'
} else { $RestoreScriptPath }
$testRoot = Join-Path (Split-Path $PSScriptRoot -Parent) ('output/demo-restore-test-' + [guid]::NewGuid().ToString('N'))
$manifestPath = Join-Path $testRoot 'manifest.json'
$evidenceDirectory = Join-Path $testRoot 'evidence'
$subscription = '253e5008-51c0-434b-80b9-ae3ac94bd66b'
$server = 'sati-demo-satilogica-central'
$resourcePrefix = "/subscriptions/$subscription/resourceGroups/rg-sati-demo/providers/Microsoft.Sql/servers/$server/databases"
$instanceId = [guid]::NewGuid().ToString('D')
$global:RestoreTestRunIds = [Collections.Generic.List[string]]::new()

function Assert-Test([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Demo restore verification test failed: $Message" }
}
function Get-Arg([string[]]$Arguments, [string]$Name) {
    $index = [array]::IndexOf($Arguments, $Name)
    if ($index -lt 0 -or $index + 1 -ge $Arguments.Count) { throw "Missing test argument $Name." }
    return $Arguments[$index + 1]
}
function New-Resource([string]$Name, [bool]$Owned) {
    $tags = if ($Owned) {
        @{ SatiRestoreRunId = $global:RestoreRunId; SatiRestorePurpose = 'Verification' }
    }
    else { @{ SatiRestoreRunId = [guid]::NewGuid().ToString('D'); SatiRestorePurpose = 'Verification' } }
    return @{ id = "$resourcePrefix/$Name"; name = $Name; tags = $tags }
}
function Assert-Throws([scriptblock]$Action, [string]$Fragment) {
    try { & $Action | Out-Null; throw 'Expected failure did not occur.' }
    catch {
        if ($_.Exception.Message -notlike "*$Fragment*") { throw }
    }
}

$az = {
    param([string[]]$Arguments)
    [void]$global:RestoreAzCalls.Add(@($Arguments))
    $verb = "$($Arguments[0]) $($Arguments[1]) $($Arguments[2])"
    switch ($verb) {
        'account show -o' { return (@{ id = $subscription } | ConvertTo-Json -Compress) }
        'account get-access-token --resource' {
            return (@{ accessToken = 'in-process-test-token' } | ConvertTo-Json -Compress)
        }
        'sql db show' {
            $name = Get-Arg $Arguments '--name'
            if ($name -ceq 'SatiDemo') {
                return ((New-Resource $name $true) | ConvertTo-Json -Compress)
            }
            if (-not $global:RestoreScratchPresent) { throw 'Scratch database not present in fake.' }
            return ((New-Resource $name ($global:RestoreMode -ne 'unknownOwnership')) | ConvertTo-Json -Compress)
        }
        'sql db list' {
            $items = @((New-Resource 'SatiDemo' $true))
            if ($global:RestoreScratchPresent) {
                $items += New-Resource $global:RestoreScratch ($global:RestoreMode -ne 'unknownOwnership')
            }
            return (ConvertTo-Json -InputObject @($items) -Compress -Depth 8)
        }
        'sql db restore' {
            $global:RestoreScratch = Get-Arg $Arguments '--dest-name'
            $global:RestoreScratchPresent = $true
            if ($global:RestoreMode -eq 'uncertainRestore') { throw 'Synthetic CLI timeout after request.' }
            return ((New-Resource $global:RestoreScratch $true) | ConvertTo-Json -Compress)
        }
        'sql db delete' {
            if ($global:RestoreMode -eq 'cleanupFailure') { throw 'Synthetic delete failure.' }
            $global:RestoreScratchPresent = $false
            return '{}'
        }
        default { throw "Unexpected Azure CLI operation in fake: $verb" }
    }
}
$sql = {
    param([string]$DatabaseName, [string]$AccessToken, [string[]]$Tables)
    [void]$global:RestoreSqlCalls.Add($DatabaseName)
    if ($AccessToken -cne 'in-process-test-token' -or
        $Tables.Count -ne 2 -or $DatabaseName -cne $global:RestoreScratch) {
        throw 'SQL fake received an unexpected target or inventory.'
    }
    $identity = if ($global:RestoreMode -eq 'wrongIdentity') {
        [guid]::NewGuid().ToString('D')
    }
    else { $instanceId }
    return @{
        IdentityRows = @(@{ Id = 1; DatabaseName = $DatabaseName; EnvironmentName = 'Demo'; InstanceId = $identity })
        MigrationIds = @('20260101000000_Initial', '20260201000000_Second')
        TableCounts = @{ 'dbo.Agencies' = 2L; 'dbo.Notes' = 42L }
    }
}
function Start-Case([string]$Mode) {
    $global:RestoreMode = $Mode
    $global:RestoreRunId = [guid]::NewGuid().ToString('D')
    [void]$global:RestoreTestRunIds.Add($global:RestoreRunId)
    $global:RestoreScratch = 'SatiRestoreCheck_' + ([guid]$global:RestoreRunId).ToString('N')
    $global:RestoreScratchPresent = $false
    $global:RestoreAzCalls = [Collections.Generic.List[object]]::new()
    $global:RestoreSqlCalls = [Collections.Generic.List[string]]::new()
}
function Invoke-Case([scriptblock]$Writer) {
    & $scriptPath -RunId ([guid]$global:RestoreRunId) -ExpectedManifestPath $manifestPath `
        -EvidenceDirectory $evidenceDirectory -RunRehearsal -Confirm:$false `
        -AzInvoker $az -SqlInvoker $sql -EvidenceWriter $Writer
}
function Read-Evidence {
    $path = Join-Path $evidenceDirectory "demo-restore-$($global:RestoreRunId).json"
    return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
}
function Count-DeleteCalls {
    return @($global:RestoreAzCalls | Where-Object { $_[0] -eq 'sql' -and $_[1] -eq 'db' -and $_[2] -eq 'delete' }).Count
}

try {
    [void][IO.Directory]::CreateDirectory($testRoot)
    @{
        schemaVersion = 1
        sourceEnvironment = 'Demo'
        sourceDatabase = 'SatiDemo'
        sourceServer = $server
        sourceSubscriptionId = $subscription
        restorePointUtc = [datetimeoffset]::UtcNow.AddDays(-1).ToString('yyyy-MM-ddTHH:mm:ssZ')
        expectedInstanceId = $instanceId
        migrationIds = @('20260101000000_Initial', '20260201000000_Second')
        tableCounts = @{ 'dbo.Agencies' = 2L; 'dbo.Notes' = 42L }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath

    Start-Case 'normal'
    $plan = & $scriptPath -RunId ([guid]$global:RestoreRunId) -ExpectedManifestPath $manifestPath `
        -EvidenceDirectory $evidenceDirectory -RunRehearsal -AzInvoker $az -SqlInvoker $sql -WhatIf
    Assert-Test ($plan.Mode -eq 'WhatIf' -and $plan.ScratchDatabase -eq $global:RestoreScratch) 'WhatIf plan was incomplete.'
    Assert-Test ($global:RestoreAzCalls.Count -eq 0 -and $global:RestoreSqlCalls.Count -eq 0) 'WhatIf called Azure or SQL.'
    Assert-Test (-not (Test-Path -LiteralPath $evidenceDirectory)) 'WhatIf wrote evidence.'

    $badManifestPath = Join-Path $testRoot 'bad-manifest.json'
    $badManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable -DateKind String
    $badManifest.tableCounts = 'dbo.Notes=42'
    $badManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badManifestPath
    Assert-Throws {
        & $scriptPath -RunId ([guid]$global:RestoreRunId) -ExpectedManifestPath $badManifestPath `
            -EvidenceDirectory $evidenceDirectory -RunRehearsal -Confirm:$false `
            -AzInvoker $az -SqlInvoker $sql
    } 'at least one expected dbo table count'
    Assert-Test ($global:RestoreAzCalls.Count -eq 0 -and $global:RestoreSqlCalls.Count -eq 0) 'Malformed tableCounts reached Azure or SQL.'

    $badManifest.tableCounts = @{ "dbo.Notes`n" = 42L }
    $badManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badManifestPath
    Assert-Throws {
        & $scriptPath -RunId ([guid]$global:RestoreRunId) -ExpectedManifestPath $badManifestPath `
            -EvidenceDirectory $evidenceDirectory -RunRehearsal -Confirm:$false `
            -AzInvoker $az -SqlInvoker $sql
    } 'invalid table name or count'
    Assert-Test ($global:RestoreAzCalls.Count -eq 0 -and $global:RestoreSqlCalls.Count -eq 0) 'A trailing newline in a table name reached Azure or SQL.'

    # Exercise the real SqlDataReader materializer with two rows. A unary comma
    # around ToArray() turns these into one nested row and fails this assertion.
    function Assert-ReaderMaterialization {
        . $scriptPath -RunId ([guid]$global:RestoreRunId) -ExpectedManifestPath $manifestPath `
            -EvidenceDirectory $evidenceDirectory -WhatIf | Out-Null
        $table = [System.Data.DataTable]::new()
        [void]$table.Columns.Add('MigrationId', [string])
        [void]$table.Rows.Add('20260101000000_Initial')
        [void]$table.Rows.Add('20260201000000_Second')
        $reader = $table.CreateDataReader()
        try { $rows = @(Convert-RestoreReaderRows $reader) }
        finally { $reader.Dispose(); $table.Dispose() }
        Assert-Test ($rows.Count -eq 2 -and
            $rows[0].MigrationId -ceq '20260101000000_Initial' -and
            $rows[1].MigrationId -ceq '20260201000000_Second') 'SQL reader rows were not individually materialized.'
    }
    Assert-ReaderMaterialization

    $result = Invoke-Case
    Assert-Test ($result.state -eq 'Completed' -and $result.cleanup -eq 'DeletedOwnedScratch') 'Normal verification did not complete and clean up.'
    Assert-Test ($result.evidenceWriteFailed -eq $false) 'Normal verification reported an evidence write failure.'
    Assert-Test ($result.migrationCount -eq 2 -and $result.tableCounts['dbo.Notes'] -eq 42) 'Normal evidence omitted approved counts.'
    Assert-Test ((Count-DeleteCalls) -eq 1 -and -not $global:RestoreScratchPresent) 'Normal cleanup did not delete the owned scratch.'
    $oldCalls = $global:RestoreAzCalls.Count
    $otherEvidence = Join-Path $testRoot 'other-evidence'
    Assert-Throws {
        & $scriptPath -RunId ([guid]$global:RestoreRunId) -ExpectedManifestPath $manifestPath `
            -EvidenceDirectory $otherEvidence -RunRehearsal -Confirm:$false `
            -AzInvoker $az -SqlInvoker $sql
    } 'run ledger'
    Assert-Test ($global:RestoreAzCalls.Count -eq $oldCalls) 'RunId replay touched Azure.'
    Assert-Test (-not (Test-Path -LiteralPath $otherEvidence)) 'RunId replay wrote to a different evidence destination.'

    Start-Case 'wrongIdentity'
    Assert-Throws { Invoke-Case } 'VerificationFailed'
    $evidence = Read-Evidence
    Assert-Test ($evidence.state -eq 'VerificationFailed' -and $evidence.cleanup -eq 'DeletedOwnedScratch') 'Wrong identity was not recorded and cleaned up.'
    Assert-Test ((Count-DeleteCalls) -eq 1) 'Wrong identity did not use owned cleanup.'

    Start-Case 'unknownOwnership'
    Assert-Throws { Invoke-Case } 'OwnershipUnconfirmed'
    $evidence = Read-Evidence
    Assert-Test ($evidence.state -eq 'OwnershipUnconfirmed' -and $evidence.cleanup -eq 'NotStarted') 'Unknown ownership was not preserved.'
    Assert-Test ((Count-DeleteCalls) -eq 0 -and $global:RestoreSqlCalls.Count -eq 0) 'Unknown ownership still deleted or queried scratch.'

    Start-Case 'cleanupFailure'
    Assert-Throws { Invoke-Case } 'cleanup failed'
    $evidence = Read-Evidence
    Assert-Test ($evidence.state -eq 'CleanupFailed' -and $evidence.cleanup -eq 'FailedOrOwnershipUncertain') 'Cleanup failure evidence was lost.'
    Assert-Test ($global:RestoreScratchPresent -and (Count-DeleteCalls) -eq 1) 'Cleanup failure did not preserve the scratch.'

    Start-Case 'uncertainRestore'
    Assert-Throws { Invoke-Case } 'RestoreCreationUncertain'
    $evidence = Read-Evidence
    Assert-Test ($evidence.state -eq 'RestoreCreationUncertain' -and (Count-DeleteCalls) -eq 0) 'Uncertain restore was retried or deleted.'
    Assert-Test ($global:RestoreScratchPresent) 'The uncertain scratch was not preserved.'

    Start-Case 'normal'
    $global:EvidenceWriteFailureInjected = $false
    $failingEvidenceWriter = {
        param([string]$Path, [string]$Json)
        $state = ($Json | ConvertFrom-Json).state
        if ($state -eq 'Verified' -and -not $global:EvidenceWriteFailureInjected) {
            $global:EvidenceWriteFailureInjected = $true
            throw 'Synthetic evidence disk failure.'
        }
        [IO.File]::WriteAllText($Path, $Json)
    }
    Assert-Throws { Invoke-Case $failingEvidenceWriter } 'evidence write failed'
    Assert-Test ((Count-DeleteCalls) -eq 1 -and -not $global:RestoreScratchPresent) 'Evidence failure skipped owned-scratch cleanup.'
    $evidence = Read-Evidence
    Assert-Test ($evidence.state -eq 'Completed' -and $evidence.cleanup -eq 'DeletedOwnedScratch' -and
        $evidence.evidenceWriteFailed -eq $true) 'Evidence failure was not durably flagged after cleanup.'
    Write-Output 'DEMO_RESTORE_VERIFICATION_TEST_PASSED'
}
finally {
    Remove-Item Env:SATI_RESTORE_VERIFICATION_TEST_MODE -ErrorAction SilentlyContinue
    $ledgerRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'output/demo-restore-verification/runs'))
    foreach ($id in $global:RestoreTestRunIds) {
        if ($id -cnotmatch '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$') { continue }
        $marker = [IO.Path]::GetFullPath((Join-Path $ledgerRoot "$id.json"))
        if (-not $marker.StartsWith($ledgerRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $marker -PathType Leaf)) { continue }
        $record = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($record.runId -ceq $id -and
            $record.scratchDatabase -ceq ('SatiRestoreCheck_' + ([guid]$id).ToString('N'))) {
            Remove-Item -LiteralPath $marker -Force
        }
    }
    $outputRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'output'))
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedTestRoot.StartsWith($outputRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTestRoot)) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
