[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('TokenTimeout', 'Success', 'TokenFailure', 'OpenFailure', 'LockFailure',
        'RestoreFailure', 'RollFailure', 'ComplianceFailure', 'CleanupFailure',
        'CleanupPreservesOriginalFailure', 'AuditFailure')]
    [string]$Case,
    [string]$ResetScriptPath
)

# Executes the real reset orchestrator with in-process identity/SQL/seed fakes. There
# are no external processes, network calls, database connections, or Azure changes.
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ResetScriptPath)) {
    $ResetScriptPath = Join-Path $PSScriptRoot '..\Sati.DemoRefresh\Shared\DemoReset.ps1'
}
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) `
    ('SatiDemoResetRuntime_' + [Guid]::NewGuid().ToString('N'))))
$originalEnvironment = @{}
foreach ($name in @('SATI_DEMO_SQL_SERVER', 'IDENTITY_ENDPOINT', 'IDENTITY_HEADER')) {
    $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Assert-Test([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Demo reset runtime assertion failed: $Message" }
}

try {
    [void][IO.Directory]::CreateDirectory((Join-Path $temporaryRoot 'Shared'))
    [void][IO.Directory]::CreateDirectory((Join-Path $temporaryRoot 'ComplianceSeed'))
    Copy-Item -LiteralPath $ResetScriptPath -Destination (Join-Path $temporaryRoot 'Shared\DemoReset.ps1')
    # The real helper is loaded, then its executable boundary is substituted below.
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\Sati.DemoRefresh\ComplianceSeed\Invoke-DemoComplianceSeed.ps1') `
        -Destination (Join-Path $temporaryRoot 'ComplianceSeed\Invoke-DemoComplianceSeed.ps1')
    Set-Content -LiteralPath (Join-Path $temporaryRoot 'Shared\Seed-DemoShowcaseData.ps1') -Value @'
param($SqlServer, $Database, $AccessToken, $AsOfDate)
Invoke-TestSeed
'@

    . (Join-Path $temporaryRoot 'Shared\DemoReset.ps1')
    $global:SatiDemoResetRuntimeState = [pscustomobject]@{
        Events = [Collections.Generic.List[string]]::new()
        Audits = [Collections.Generic.List[object]]::new()
        Connections = [Collections.Generic.List[object]]::new()
        OutcomeAttempts = [Collections.Generic.List[object]]::new()
        Timeout = 0
        Secret = 'SYNTHETIC_SECRET_MUST_NEVER_REACH_A_LOG'
        Token = 'SYNTHETIC_ACCESS_TOKEN_MUST_NEVER_REACH_A_LOG'
        FailureStage = switch ($Case) {
            'TokenFailure' { 'AcquireManagedIdentityToken' }
            'OpenFailure' { 'OpenDemoDatabase' }
            'LockFailure' { 'AcquireExclusiveResetLock' }
            'RestoreFailure' { 'RestoreCanonicalBaseline' }
            'CleanupPreservesOriginalFailure' { 'RestoreCanonicalBaseline' }
            'RollFailure' { 'RollShowcaseDates' }
            'ComplianceFailure' { 'CompleteComplianceHistory' }
            'AuditFailure' { 'RestoreCanonicalBaseline' }
            'CleanupFailure' { 'ReleaseExclusiveResetLock' }
            default { '' }
        }
        FailCleanup = $Case -in @('CleanupFailure', 'CleanupPreservesOriginalFailure')
        FailAudit = $Case -eq 'AuditFailure'
    }
    $env:SATI_DEMO_SQL_SERVER = 'mock-only.invalid'
    $env:IDENTITY_ENDPOINT = 'http://127.0.0.1/mock-identity'
    $env:IDENTITY_HEADER = $global:SatiDemoResetRuntimeState.Secret

    function Write-Host { param($Object) [void]$global:SatiDemoResetRuntimeState.Events.Add("log:$Object") }
    function Write-Warning { param($Message) [void]$global:SatiDemoResetRuntimeState.Events.Add("warning:$Message") }

    function Invoke-RestMethod {
        param($Method, $Uri, $Headers, [int]$TimeoutSec)
        [void]$global:SatiDemoResetRuntimeState.Events.Add('operation:AcquireManagedIdentityToken')
        $global:SatiDemoResetRuntimeState.Timeout = $TimeoutSec
        if ($global:SatiDemoResetRuntimeState.FailureStage -eq 'AcquireManagedIdentityToken') {
            throw [TimeoutException]::new($global:SatiDemoResetRuntimeState.Secret)
        }
        return @{ access_token = $global:SatiDemoResetRuntimeState.Token }
    }

    function New-TestCommand {
        $command = [pscustomobject]@{
            CommandText = ''
            CommandTimeout = 30
            Parameters = ([System.Data.SqlClient.SqlCommand]::new()).Parameters
        }
        $command | Add-Member ScriptMethod ExecuteScalar {
            Assert-Test ($this.CommandText -like '*sp_getapplock*') 'Unexpected scalar command.'
            [void]$global:SatiDemoResetRuntimeState.Events.Add('operation:AcquireExclusiveResetLock')
            if ($global:SatiDemoResetRuntimeState.FailureStage -eq 'AcquireExclusiveResetLock') { return -1 }
            return 0
        }
        $command | Add-Member ScriptMethod ExecuteNonQuery {
            if ($this.CommandText -like '*SatiResetToCanonicalBaseline*') {
                [void]$global:SatiDemoResetRuntimeState.Events.Add('operation:RestoreCanonicalBaseline')
                Assert-Test ($this.CommandTimeout -gt 0 -and $this.CommandTimeout -le 300) `
                    'Restore must time out with room left for cleanup before the 600-second host limit.'
                if ($global:SatiDemoResetRuntimeState.FailureStage -eq 'RestoreCanonicalBaseline') {
                    throw [InvalidOperationException]::new($global:SatiDemoResetRuntimeState.Secret)
                }
            }
            elseif ($this.CommandText -like '*sp_releaseapplock*') {
                [void]$global:SatiDemoResetRuntimeState.Events.Add('operation:ReleaseExclusiveResetLock')
                if ($global:SatiDemoResetRuntimeState.FailCleanup) {
                    throw [TimeoutException]::new($global:SatiDemoResetRuntimeState.Secret)
                }
            }
            elseif ($this.CommandText -like '*INSERT dbo.AuditEvents*') {
                [void]$global:SatiDemoResetRuntimeState.Events.Add('operation:WriteAudit')
                [void]$global:SatiDemoResetRuntimeState.Audits.Add([pscustomobject]@{
                    Action = $this.Parameters['@Action'].Value
                    RequestId = $this.Parameters['@RequestId'].Value
                    ActorUserId = $this.Parameters['@ActorUserId'].Value
                    Metadata = $this.Parameters['@Metadata'].Value | ConvertFrom-Json
                })
                if ($global:SatiDemoResetRuntimeState.FailAudit) {
                    throw [InvalidOperationException]::new($global:SatiDemoResetRuntimeState.Secret)
                }
            }
            else { throw 'Unexpected non-query command in the mock SQL boundary.' }
            return 1
        }
        return $command
    }

    function New-DemoConnection {
        param([string]$Server, [string]$Token)
        Assert-Test ($Server -eq 'mock-only.invalid') 'Unexpected database target.'
        Assert-Test ($Token -eq $global:SatiDemoResetRuntimeState.Token) 'Token did not reach the SQL boundary.'
        [void]$global:SatiDemoResetRuntimeState.Events.Add('operation:OpenDemoDatabase')
        if ($global:SatiDemoResetRuntimeState.FailureStage -eq 'OpenDemoDatabase' -and $global:SatiDemoResetRuntimeState.OutcomeAttempts.Count -eq 0) {
            throw [InvalidOperationException]::new($global:SatiDemoResetRuntimeState.Secret)
        }
        $connection = [pscustomobject]@{ State = [Data.ConnectionState]::Open; Disposed = $false }
        $connection | Add-Member ScriptMethod CreateCommand { New-TestCommand }
        $connection | Add-Member ScriptMethod Dispose { $this.Disposed = $true; $this.State = [Data.ConnectionState]::Closed }
        [void]$global:SatiDemoResetRuntimeState.Connections.Add($connection)
        return $connection
    }

    function Invoke-TestSeed {
        [void]$global:SatiDemoResetRuntimeState.Events.Add('operation:RollShowcaseDates')
        if ($global:SatiDemoResetRuntimeState.FailureStage -eq 'RollShowcaseDates') {
            throw [InvalidOperationException]::new($global:SatiDemoResetRuntimeState.Secret)
        }
    }

    function Invoke-DemoComplianceSeed {
        param($Server, $Token, $AsOfDate)
        [void]$global:SatiDemoResetRuntimeState.Events.Add('operation:CompleteComplianceHistory')
        if ($global:SatiDemoResetRuntimeState.FailureStage -eq 'CompleteComplianceHistory') {
            throw [InvalidOperationException]::new($global:SatiDemoResetRuntimeState.Secret)
        }
    }

    # Preserve and execute the real audit writer, recording that even token failures
    # reach it. Its own SQL call uses the same in-process fake as restoration.
    $script:realOutcome = ${function:Write-DemoResetOutcome}
    function Write-DemoResetOutcome {
        param($Server, $Token, $RequestId, $ActorUserId, $Trigger, $Succeeded, $Stage, $Seconds, $Detail)
        [void]$global:SatiDemoResetRuntimeState.OutcomeAttempts.Add([pscustomobject]@{ Succeeded = $Succeeded; Stage = $Stage })
        & $script:realOutcome @PSBoundParameters
    }

    if ($Case -eq 'TokenTimeout') {
        $token = Get-DemoSqlToken
        Assert-Test ($token -eq $global:SatiDemoResetRuntimeState.Token) 'Managed identity token was not returned.'
        Assert-Test ($global:SatiDemoResetRuntimeState.Timeout -gt 0 -and $global:SatiDemoResetRuntimeState.Timeout -le 30) `
            'Managed identity HTTP request must have a bounded timeout of at most 30 seconds.'
    }
    else {
        $requestId = [Guid]'99a6776a-0e47-428d-9f7f-12ca5f08c9e4'
        $caught = $null
        try { Invoke-DemoFullReset -RequestId $requestId -ActorUserId 1006 -Trigger Manual }
        catch { $caught = $_ }

        $failed = $Case -ne 'Success'
        Assert-Test (($null -ne $caught) -eq $failed) 'Reset result did not match the injected failure.'
        Assert-Test ($global:SatiDemoResetRuntimeState.OutcomeAttempts.Count -eq 1) 'Reset must attempt exactly one outcome audit.'
        Assert-Test ($global:SatiDemoResetRuntimeState.OutcomeAttempts[0].Succeeded -eq (-not $failed)) 'Wrong audit outcome.'
        $expectedStage = if ($failed) { $global:SatiDemoResetRuntimeState.FailureStage } else { 'Completed' }
        Assert-Test ($global:SatiDemoResetRuntimeState.OutcomeAttempts[0].Stage -eq $expectedStage) 'Failure stage was lost or replaced.'
        if ($failed) {
            Assert-Test ($caught.Exception.Message -like "*Stage=$expectedStage *") 'Thrown failure does not identify its stage.'
            Assert-Test ($caught.Exception.Message -like '*ExceptionTypes=*') 'Thrown failure must retain safe exception types.'
        }
        if ($Case -eq 'CleanupPreservesOriginalFailure') {
            Assert-Test ($caught.Exception.Message -like '*System.InvalidOperationException*') 'Original exception type was lost.'
            Assert-Test ($caught.Exception.Message -notlike '*System.TimeoutException*') 'Cleanup exception replaced the original failure.'
        }

        $expectedStages = @('AcquireManagedIdentityToken', 'OpenDemoDatabase', 'AcquireExclusiveResetLock',
            'RestoreCanonicalBaseline', 'RollShowcaseDates', 'CompleteComplianceHistory')
        foreach ($stageName in $expectedStages) {
            $operationIndex = $global:SatiDemoResetRuntimeState.Events.IndexOf("operation:$stageName")
            if ($operationIndex -lt 0) { continue }
            $markers = @($global:SatiDemoResetRuntimeState.Events | Where-Object { $_ -like "log:DEMO_RESET_STAGE *Stage=$stageName *" })
            Assert-Test ($markers.Count -eq 1) "Missing or duplicated stage marker: $stageName."
            Assert-Test ($global:SatiDemoResetRuntimeState.Events.IndexOf($markers[0]) -lt $operationIndex) `
                "Stage marker was emitted after its potentially blocking operation: $stageName."
            Assert-Test ($markers[0] -like "*RequestId=$requestId Trigger=Manual *") 'Stage marker lost request correlation.'
        }
        $outcomeStage = if ($failed) { 'RecordFailureOutcome' } else { 'RecordSuccessOutcome' }
        Assert-Test (@($global:SatiDemoResetRuntimeState.Events | Where-Object { $_ -like "*DEMO_RESET_STAGE *Stage=$outcomeStage *" }).Count -eq 1) `
            'Outcome stage marker is missing.'
        Assert-Test (@($global:SatiDemoResetRuntimeState.Connections | Where-Object { -not $_.Disposed }).Count -eq 0) `
            'A SQL connection was not disposed.'

        if ($Case -eq 'TokenFailure') {
            Assert-Test ($global:SatiDemoResetRuntimeState.Audits.Count -eq 0) 'Audit SQL must not execute without a token.'
            Assert-Test (@($global:SatiDemoResetRuntimeState.Events | Where-Object { $_ -like '*outcome not recorded: no database token*' }).Count -eq 1) `
                'Unavailable audit must be visible.'
        }
        else {
            Assert-Test ($global:SatiDemoResetRuntimeState.Audits.Count -eq 1) 'The real audit writer did not execute.'
            $audit = $global:SatiDemoResetRuntimeState.Audits[0]
            $expectedAction = if ($failed) { 'demo.reset.failed' } else { 'demo.reset.completed' }
            Assert-Test ($audit.Action -eq $expectedAction) 'Wrong persisted audit action.'
            Assert-Test ($audit.Metadata.stage -eq $expectedStage) 'Wrong persisted audit stage.'
            Assert-Test ($audit.RequestId -eq $requestId.ToString() -and $audit.ActorUserId -eq 1006) `
                'The audit must correlate to the original actor and request.'
        }
        if ($Case -eq 'AuditFailure') {
            Assert-Test (@($global:SatiDemoResetRuntimeState.Events | Where-Object { $_ -like '*outcome not recorded.*' }).Count -eq 1) `
                'Audit failure was silently dropped.'
        }
        if ($global:SatiDemoResetRuntimeState.FailCleanup) {
            Assert-Test (@($global:SatiDemoResetRuntimeState.Events | Where-Object { $_ -like '*cleanup failed.*' }).Count -eq 1) `
                'Cleanup failure was silently dropped.'
        }
        $visible = ($global:SatiDemoResetRuntimeState.Events -join "`n") + ($global:SatiDemoResetRuntimeState.Audits | ConvertTo-Json -Depth 6) +
            $(if ($null -ne $caught) { $caught.Exception.Message } else { '' })
        Assert-Test (-not $visible.Contains($global:SatiDemoResetRuntimeState.Secret)) 'Exception details or managed identity header leaked.'
        Assert-Test (-not $visible.Contains($global:SatiDemoResetRuntimeState.Token)) 'Access token leaked.'
    }
    Write-Output "DEMO_RESET_RUNTIME_TEST_PASSED Case=$Case"
}
finally {
    foreach ($name in $originalEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name], 'Process')
    }
    $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    Assert-Test (([IO.Path]::GetDirectoryName($temporaryRoot) -eq $expectedParent) -and
        ([IO.Path]::GetFileName($temporaryRoot) -like 'SatiDemoResetRuntime_*')) 'Unsafe temporary directory cleanup target.'
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
