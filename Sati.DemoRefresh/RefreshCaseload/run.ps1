param($Timer)

# The nightly full Demo reset. Its outcome is recorded as an audit event, so a night that
# fails is visible on the Admin dashboard instead of only in the Function log.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\Shared\DemoReset.ps1')

Write-Host "Starting the nightly full Demo reset for $([DateTime]::Today.ToString('yyyy-MM-dd'))."
Invoke-DemoFullReset -RequestId ([Guid]::NewGuid()) -ActorUserId 0 -Trigger 'Scheduled'
