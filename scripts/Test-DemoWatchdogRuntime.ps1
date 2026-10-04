[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Clean', 'MissingOutcome', 'FailedOutcome', 'PoisonQueue',
        'PausedMissingOutcome', 'PausedPoisonQueue', 'PausedFailedOutcome',
        'OverduePending', 'BillingStale', 'BillingUnknown', 'SignaturePackage',
        'SignatureMail', 'DisabledFeatures', 'ReadFailure', 'PartialReadFailure')]
    [string]$Case,
    [string]$WatchdogScriptPath
)

# Runs the real watchdog decision path with in-process fakes. It does not connect
# to Azure, SQL, Storage, or Application Insights.
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($WatchdogScriptPath)) {
    $WatchdogScriptPath = Join-Path $PSScriptRoot '..\Sati.DemoRefresh\Shared\DemoWatchdog.ps1'
}
function Assert-Test([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Demo watchdog runtime assertion failed: $Message" }
}

$originalEnvironment = @{}
foreach ($name in @('SATI_DEMO_SQL_SERVER', 'SATI_WATCHDOG_EXPECT_NOTE_ABANDONMENT',
    'SATI_WATCHDOG_EXPECT_CLEARINGHOUSE', 'SATI_WATCHDOG_EXPECT_SIGNATURE',
    'AzureWebJobs.RefreshCaseload.Disabled')) {
    $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    . $WatchdogScriptPath
    $global:WatchdogTest = [pscustomobject]@{
        Lines = [Collections.Generic.List[string]]::new()
        Calls = [Collections.Generic.List[string]]::new()
        Secret = 'SYNTHETIC_WATCHDOG_SECRET_NEVER_LOG'
        Disposed = $false
    }
    $env:SATI_DEMO_SQL_SERVER = 'mock-only.invalid'
    $env:SATI_WATCHDOG_EXPECT_NOTE_ABANDONMENT = if ($Case -in @('OverduePending', 'PartialReadFailure')) { 'true' } else { 'false' }
    $env:SATI_WATCHDOG_EXPECT_CLEARINGHOUSE = if ($Case -in @('BillingStale', 'BillingUnknown')) { 'true' } else { 'false' }
    $env:SATI_WATCHDOG_EXPECT_SIGNATURE = if ($Case -in @('SignaturePackage', 'SignatureMail')) { 'true' } else { 'false' }
    [Environment]::SetEnvironmentVariable('AzureWebJobs.RefreshCaseload.Disabled',
        $(if ($Case -in @('PausedMissingOutcome', 'PausedPoisonQueue', 'PausedFailedOutcome')) { 'true' } else { 'false' }),
        'Process')

    function Write-Host { param($Object) [void]$global:WatchdogTest.Lines.Add([string]$Object) }
    function Get-DemoSqlToken { return $global:WatchdogTest.Secret }
    function New-DemoConnection {
        param($Server, $Token)
        Assert-Test ($Server -eq 'mock-only.invalid' -and $Token -eq $global:WatchdogTest.Secret) 'Unexpected SQL boundary.'
        $connection = [pscustomobject]@{}
        $connection | Add-Member ScriptMethod Dispose { $global:WatchdogTest.Disposed = $true }
        return $connection
    }
    function Assert-DemoWatchdogDatabase {
        param($Connection)
        [void]$global:WatchdogTest.Calls.Add('identity')
        if ($Case -eq 'ReadFailure') { throw [InvalidOperationException]::new($global:WatchdogTest.Secret) }
    }
    function Read-DemoWatchdogResetState {
        param($Connection, $ScheduledStartUtc)
        [void]$global:WatchdogTest.Calls.Add('reset')
        Assert-Test ($ScheduledStartUtc.ToString('o') -eq '2026-10-03T07:15:00.0000000Z') 'Eastern scheduled reset cutoff is wrong.'
        return [pscustomobject]@{
            RecentOutcomeCount = if ($Case -in @('MissingOutcome', 'PausedMissingOutcome',
                'PausedPoisonQueue', 'PausedFailedOutcome')) { 0 } else { 1 }
            LatestAction = if ($Case -in @('FailedOutcome', 'PausedFailedOutcome')) {
                'demo.reset.failed'
            } else { 'demo.reset.completed' }
        }
    }
    function Get-DemoPoisonQueueCount { [void]$global:WatchdogTest.Calls.Add('queue'); return $(if ($Case -in @('PoisonQueue', 'PausedPoisonQueue', 'PartialReadFailure')) { 3 } else { 0 }) }
    function Read-DemoWatchdogOverdueCount {
        param($Connection, $Today)
        [void]$global:WatchdogTest.Calls.Add('overdue')
        Assert-Test ($Today -eq [datetime]'2026-10-03') 'The watchdog did not use Eastern day.'
        if ($Case -eq 'PartialReadFailure') { throw [InvalidOperationException]::new($global:WatchdogTest.Secret) }
        return $(if ($Case -eq 'OverduePending') { 5 } else { 0 })
    }
    function Read-DemoWatchdogBillingState {
        param($Connection, $NowUtc)
        [void]$global:WatchdogTest.Calls.Add('billing')
        return [pscustomobject]@{
            Enabled = $true
            StaleDispatches = if ($Case -eq 'BillingStale') { 2 } else { 0 }
            UnknownOutcomes = if ($Case -eq 'BillingUnknown') { 1 } else { 0 }
        }
    }
    function Read-DemoWatchdogSignatureState {
        param($Connection, $NowUtc)
        [void]$global:WatchdogTest.Calls.Add('signature')
        return [pscustomobject]@{
            Enabled = $true
            UnpreparedPackages = if ($Case -eq 'SignaturePackage') { 1 } else { 0 }
            FailedNotifications = if ($Case -eq 'SignatureMail') { 4 } else { 0 }
        }
    }

    Invoke-DemoWatchdog -NowUtc ([datetime]'2026-10-03T08:00:00Z')
    if ($global:WatchdogTest.Lines.Count -eq 1 -and $global:WatchdogTest.Lines[0] -like '*WatchdogCheckFailed*' -and $Case -notin @('ReadFailure', 'PartialReadFailure')) {
        throw "Unexpected check failure: $($Error[0].Exception.GetType().Name) $($Error[0].Exception.Message)"
    }
    Assert-Test ($global:WatchdogTest.Lines.Count -eq 1) 'Exactly one result trace must be emitted.'
    $line = $global:WatchdogTest.Lines[0]
    $expected = switch ($Case) {
        'MissingOutcome' { 'ResetOutcomeMissing' }
        'FailedOutcome' { 'ResetOutcomeFailed' }
        'PoisonQueue' { 'PoisonQueueMessages' }
        'PausedPoisonQueue' { 'PoisonQueueMessages' }
        'PausedFailedOutcome' { 'ResetOutcomeFailed' }
        'OverduePending' { 'OverduePendingNotes' }
        'BillingStale' { 'BillingDispatchStale' }
        'BillingUnknown' { 'BillingOutcomeUnknown' }
        'SignaturePackage' { 'SignaturePackageUnprepared' }
        'SignatureMail' { 'SignatureNotificationFailed' }
        'ReadFailure' { 'WatchdogCheckFailed' }
        'PartialReadFailure' { 'WatchdogCheckFailed' }
        default { '' }
    }
    if ($expected) {
        Assert-Test ($line -like "SATI_WATCHDOG_FINDING *$expected=*") "Missing finding $expected. Actual marker: $line"
    }
    else {
        Assert-Test ($line -eq 'SATI_WATCHDOG_OK') 'Clean/disabled path should be OK.'
    }
    if ($Case -in @('PausedMissingOutcome', 'PausedPoisonQueue', 'PausedFailedOutcome')) {
        Assert-Test (-not $line.Contains('ResetOutcomeMissing')) 'Intentional timer pause was reported as a missing reset.'
    }
    Assert-Test (-not $line.Contains($global:WatchdogTest.Secret)) 'Secret reached the result trace.'
    if ($Case -in @('ReadFailure', 'PartialReadFailure')) {
        Assert-Test ($line -like '*FailureType=InvalidOperationException*') 'Failure type was not available for safe diagnosis.'
    }
    if ($Case -eq 'PartialReadFailure') {
        Assert-Test ($line -like '*PoisonQueueMessages=3*') 'A later failed check discarded an earlier safe finding.'
    }
    Assert-Test ($global:WatchdogTest.Disposed) 'SQL connection was not disposed.'
    Assert-Test ($global:WatchdogTest.Calls.Contains('queue') -eq ($Case -ne 'ReadFailure')) 'Queue call order was wrong.'
    Assert-Test ($global:WatchdogTest.Calls.Contains('overdue') -eq ($Case -in @('OverduePending', 'PartialReadFailure'))) 'Overdue feature gate failed.'
    Assert-Test ($global:WatchdogTest.Calls.Contains('billing') -eq ($Case -in @('BillingStale', 'BillingUnknown'))) 'Billing feature gate failed.'
    Assert-Test ($global:WatchdogTest.Calls.Contains('signature') -eq ($Case -in @('SignaturePackage', 'SignatureMail'))) 'Signature feature gate failed.'
    Write-Output "DEMO_WATCHDOG_RUNTIME_TEST_PASSED Case=$Case"
}
finally {
    foreach ($name in $originalEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name], 'Process')
    }
}
