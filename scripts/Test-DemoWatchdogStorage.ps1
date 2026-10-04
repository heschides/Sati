[CmdletBinding()]
param()

# Exercises the real metadata-only Azure Storage request against an in-process
# HTTP fake. No network call is made and no queue body is read.
$ErrorActionPreference = 'Stop'
function Assert-Test([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Demo watchdog storage assertion failed: $Message" }
}
. (Join-Path $PSScriptRoot '..\Sati.DemoRefresh\Shared\DemoWatchdog.ps1')
$oldConnection = $env:AzureWebJobsStorage
$global:WatchdogStorageCalls = 0
$global:WatchdogStorageStatus = 200
try {
    $env:AzureWebJobsStorage = 'DefaultEndpointsProtocol=https;AccountName=satidemorefreshst;AccountKey=' +
        [Convert]::ToBase64String(([byte[]](1..32))) + ';EndpointSuffix=core.windows.net'
    function Invoke-WebRequest {
        param($Method, $Uri, $Headers, $TimeoutSec, $ErrorAction)
        $global:WatchdogStorageCalls++
        Assert-Test ($Method -eq 'Get') 'Queue metadata must use GET.'
        Assert-Test ($Uri -eq 'https://satidemorefreshst.queue.core.windows.net/demo-reset-requests-poison?comp=metadata') 'Unexpected queue or request type.'
        Assert-Test ($Headers.Authorization -like 'SharedKey satidemorefreshst:*') 'Missing Shared Key authorization.'
        Assert-Test ($TimeoutSec -le 15) 'Queue metadata request must be bounded.'
        if ($global:WatchdogStorageStatus -ne 200) {
            throw [System.Net.Http.HttpRequestException]::new('Synthetic HTTP failure', $null,
                [System.Net.HttpStatusCode]$global:WatchdogStorageStatus)
        }
        return [pscustomobject]@{ Headers = @{ 'x-ms-approximate-messages-count' = '3' } }
    }
    Assert-Test ((Get-DemoPoisonQueueCount) -eq 3) 'Approximate count was not returned.'
    Assert-Test ($global:WatchdogStorageCalls -eq 1) 'Expected one metadata-only request.'

    $global:WatchdogStorageStatus = 404
    Assert-Test ((Get-DemoPoisonQueueCount) -eq 0) 'A never-created poison queue must count as zero.'
    $global:WatchdogStorageStatus = 403
    $forbiddenFailed = $false
    try { [void](Get-DemoPoisonQueueCount) }
    catch { $forbiddenFailed = $true }
    Assert-Test $forbiddenFailed 'An authorization failure must not count as an empty queue.'
    Assert-Test ($global:WatchdogStorageCalls -eq 3) 'Expected one GET per storage check.'

    $env:AzureWebJobsStorage = $env:AzureWebJobsStorage.Replace('satidemorefreshst', 'someotheraccount')
    $failed = $false
    try { [void](Get-DemoPoisonQueueCount) }
    catch { $failed = $true }
    Assert-Test $failed 'A different storage account must fail closed.'
    Assert-Test ($global:WatchdogStorageCalls -eq 3) 'Unreviewed storage account was contacted.'
    Write-Output 'DEMO_WATCHDOG_STORAGE_TEST_PASSED'
}
finally {
    $env:AzureWebJobsStorage = $oldConnection
    Remove-Variable WatchdogStorageCalls -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable WatchdogStorageStatus -Scope Global -ErrorAction SilentlyContinue
}
