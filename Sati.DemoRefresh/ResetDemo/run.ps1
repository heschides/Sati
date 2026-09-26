using namespace System.Net

param($Request, $TriggerMetadata)

$ErrorActionPreference = 'Stop'
$server = $env:SATI_DEMO_SQL_SERVER
$requestId = [Guid]::Empty
$actorUserId = 0
$body = $Request.Body
if ($null -eq $body) {
    $body = $Request.RawBody
}
if ($body -is [System.Text.Json.JsonDocument]) {
    $body = $body.RootElement.GetRawText()
}
elseif ($body -is [System.Text.Json.JsonElement]) {
    $body = $body.GetRawText()
}
if ($body -is [System.IO.Stream]) {
    $reader = [System.IO.StreamReader]::new($body, [Text.Encoding]::UTF8, $true, 1024, $true)
    try {
        $body = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}
if ($body -is [byte[]]) {
    $body = [Text.Encoding]::UTF8.GetString($body)
}
if ($body -is [string]) {
    try {
        $body = $body | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        $body = $null
    }
}
$requestIdText = if ($null -eq $body) { '' } else { [string]$body.requestId }
$actorUserIdText = if ($null -eq $body) { '' } else { [string]$body.actorUserId }
$requestIdValid = [Guid]::TryParse($requestIdText, [ref]$requestId)
$actorUserIdValid = [int]::TryParse($actorUserIdText, [ref]$actorUserId)
$serverConfigured = -not [string]::IsNullOrWhiteSpace($server)
if (-not $serverConfigured -or -not $requestIdValid -or -not $actorUserIdValid -or $actorUserId -lt 1) {
    Push-OutputBinding -Name Response -Value ([HttpResponseContext]@{
        StatusCode = [HttpStatusCode]::BadRequest
        Body = @{
            error = 'A valid reset request is required.'
            bodyType = if ($null -eq $body) { 'null' } else { $body.GetType().FullName }
            propertyNames = if ($null -eq $body) { @() } else { @($body.PSObject.Properties.Name) }
            serverConfigured = $serverConfigured
            requestIdPresent = -not [string]::IsNullOrWhiteSpace($requestIdText)
            actorUserIdPresent = -not [string]::IsNullOrWhiteSpace($actorUserIdText)
            requestIdValid = $requestIdValid
            actorUserIdValid = $actorUserIdValid
            actorUserIdPositive = $actorUserId -gt 0
        }
    })
    return
}

# The reset itself takes minutes, longer than the Function's HTTP front end holds a request
# open, so this only validates and queues it. ResetDemoWorker performs it under the reset
# lock and records the outcome as an audit event. A queued request is never retried: a
# failed reset is reviewed before it is run again.
Push-OutputBinding -Name ResetRequest -Value (@{
    requestId = $requestId.ToString()
    actorUserId = $actorUserId
} | ConvertTo-Json -Compress)
Write-Host "Full Demo reset queued. RequestId=$requestId ActorUserId=$actorUserId"
Push-OutputBinding -Name Response -Value ([HttpResponseContext]@{
    StatusCode = [HttpStatusCode]::Accepted
    Body = @{ requestId = $requestId; status = 'Queued' }
})
