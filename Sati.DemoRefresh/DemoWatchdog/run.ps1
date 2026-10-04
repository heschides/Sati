param($Timer)

# Daily read-only outcome check. Findings go to Application Insights and are
# routed by Azure Monitor; this function never retries a reset or changes data.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\Shared\DemoWatchdog.ps1')
Invoke-DemoWatchdog -NowUtc ([DateTime]::UtcNow)
