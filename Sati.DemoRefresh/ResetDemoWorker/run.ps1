param($QueueItem, $TriggerMetadata)

# Performs a full Demo reset an Admin requested through ResetDemo. host.json allows one
# delivery (maxDequeueCount 1): a failed reset lands in the poison queue and is reviewed,
# never silently run again. The outcome is recorded as an audit event by DemoReset.ps1.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\Shared\DemoReset.ps1')

$message = if ($QueueItem -is [string]) { $QueueItem | ConvertFrom-Json } else { $QueueItem }
$requestId = [Guid]::Empty
$actorUserId = 0
if (-not [Guid]::TryParse([string]$message.requestId, [ref]$requestId) -or
    -not [int]::TryParse([string]$message.actorUserId, [ref]$actorUserId) -or
    $actorUserId -lt 1) {
    throw 'The queued Demo reset request is malformed; nothing was changed.'
}

Invoke-DemoFullReset -RequestId $requestId -ActorUserId $actorUserId -Trigger 'Manual'
