# The full Demo reset, shared by ResetDemoWorker (an Admin's request, taken off the queue)
# and RefreshCaseload (the nightly timer). It holds the exclusive reset lock while it
# restores the canonical baseline, rolls it to today, and completes past compliance
# history, then records the outcome as an audit event so the next Admin sign-in sees
# whether the reset succeeded. A full reset takes minutes, longer than the Function's
# HTTP front end allows, which is why no HTTP request waits for it.

. (Join-Path $PSScriptRoot '..\ComplianceSeed\Invoke-DemoComplianceSeed.ps1')

function Get-DemoSqlToken {
    $identityEndpoint = $env:IDENTITY_ENDPOINT
    $identityHeader = $env:IDENTITY_HEADER
    if ([string]::IsNullOrWhiteSpace($identityEndpoint) -or
        [string]::IsNullOrWhiteSpace($identityHeader)) {
        throw 'The Function App managed-identity endpoint is unavailable.'
    }
    $resource = [Uri]::EscapeDataString('https://database.windows.net/')
    $separator = if ($identityEndpoint.Contains('?')) { '&' } else { '?' }
    $tokenUri = "$identityEndpoint${separator}api-version=2019-08-01&resource=$resource"
    $result = Invoke-RestMethod -Method Get -Uri $tokenUri -Headers @{
        'X-IDENTITY-HEADER' = $identityHeader
        'Metadata' = 'true'
    }
    if ([string]::IsNullOrWhiteSpace([string]$result.access_token)) {
        throw 'Managed identity did not return an Azure SQL access token.'
    }
    return [string]$result.access_token
}

function New-DemoConnection([string]$Server, [string]$Token) {
    $connection = [System.Data.SqlClient.SqlConnection]::new(
        "Server=$Server;Database=SatiDemo;Encrypt=true;TrustServerCertificate=false;Connect Timeout=30;")
    $connection.AccessToken = $Token
    $connection.Open()
    return $connection
}

# Exception types and SQL error numbers only: messages can carry data and are never logged.
function Get-SafeFailureDetail($ErrorRecord) {
    $exceptionTypes = [System.Collections.Generic.List[string]]::new()
    $sqlErrorNumbers = [System.Collections.Generic.List[int]]::new()
    $exception = $ErrorRecord.Exception
    while ($null -ne $exception) {
        $exceptionTypes.Add($exception.GetType().FullName)
        if ($exception -is [System.Data.SqlClient.SqlException]) {
            foreach ($sqlError in $exception.Errors) { $sqlErrorNumbers.Add([int]$sqlError.Number) }
        }
        $exception = $exception.InnerException
    }
    return [pscustomobject]@{
        ExceptionTypes = @($exceptionTypes | Select-Object -Unique)
        SqlErrorNumbers = @($sqlErrorNumbers | Select-Object -Unique)
    }
}

# Written after the restore, so it survives it; a reset that failed before restoring
# writes into the database as it stood. Never allowed to hide the reset's own outcome.
function Write-DemoResetOutcome {
    param(
        [string]$Server, [string]$Token, [Guid]$RequestId, [int]$ActorUserId,
        [string]$Trigger, [bool]$Succeeded, [string]$Stage, [double]$Seconds, $Detail
    )
    if ([string]::IsNullOrWhiteSpace($Token)) {
        Write-Warning "Demo reset outcome not recorded: no database token. RequestId=$RequestId"
        return
    }
    try {
        $connection = New-DemoConnection $Server $Token
        try {
            $metadata = [ordered]@{
                trigger = $Trigger
                stage = $Stage
                durationSeconds = [Math]::Round($Seconds)
            }
            if ($null -ne $Detail) {
                $metadata.exceptionTypes = $Detail.ExceptionTypes
                $metadata.sqlErrorNumbers = $Detail.SqlErrorNumbers
            }
            $command = $connection.CreateCommand()
            $command.CommandText = @'
IF DB_NAME() <> N'SatiDemo' OR NOT EXISTS
   (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id=1 AND EnvironmentName=N'Demo')
    THROW 51000, 'Refusing to record a Demo reset outside the validated Demo database.', 1;
INSERT dbo.AuditEvents
    (EventId, AgencyId, ActorUserId, Action, ResourceType, ResourceId, OccurredAtUtc, CorrelationId, MetadataJson)
VALUES
    (NEWID(), COALESCE((SELECT AgencyId FROM dbo.Users WHERE Id=@ActorUserId), 2), @ActorUserId,
     @Action, N'DemoEnvironment', @RequestId, SYSUTCDATETIME(), @RequestId, @Metadata);
'@
            [void]$command.Parameters.AddWithValue('@ActorUserId', $ActorUserId)
            [void]$command.Parameters.AddWithValue('@Action', $(if ($Succeeded) { 'demo.reset.completed' } else { 'demo.reset.failed' }))
            [void]$command.Parameters.AddWithValue('@RequestId', $RequestId.ToString())
            [void]$command.Parameters.AddWithValue('@Metadata', ($metadata | ConvertTo-Json -Compress -Depth 4))
            [void]$command.ExecuteNonQuery()
        }
        finally {
            $connection.Dispose()
        }
    }
    catch {
        $detail = Get-SafeFailureDetail $_
        Write-Warning "Demo reset outcome not recorded. RequestId=$RequestId ExceptionTypes=$($detail.ExceptionTypes -join ',')"
    }
}

function Invoke-DemoFullReset {
    param(
        [Parameter(Mandatory = $true)][Guid]$RequestId,
        [Parameter(Mandatory = $true)][int]$ActorUserId,
        [Parameter(Mandatory = $true)][ValidateSet('Manual', 'Scheduled')][string]$Trigger
    )

    $server = $env:SATI_DEMO_SQL_SERVER
    if ([string]::IsNullOrWhiteSpace($server)) { throw 'SATI_DEMO_SQL_SERVER is required.' }

    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $stage = 'AcquireManagedIdentityToken'
    $token = $null
    try {
        $token = Get-DemoSqlToken
        $stage = 'OpenDemoDatabase'
        $connection = New-DemoConnection $server $token
        try {
            $stage = 'AcquireExclusiveResetLock'
            $lock = $connection.CreateCommand()
            $lock.CommandTimeout = 70
            $lock.CommandText = @'
DECLARE @result int;
EXEC @result=sys.sp_getapplock @Resource=N'SatiDemo.FullReset',
    @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=60000;
SELECT @result;
'@
            if ([int]$lock.ExecuteScalar() -lt 0) { throw 'The Demo is busy; reset did not begin.' }

            $stage = 'RestoreCanonicalBaseline'
            $command = $connection.CreateCommand()
            $command.CommandTimeout = 900
            $command.CommandText = 'EXEC dbo.SatiResetToCanonicalBaseline @RequestId, @ActorUserId;'
            [void]$command.Parameters.AddWithValue('@RequestId', $RequestId)
            [void]$command.Parameters.AddWithValue('@ActorUserId', $ActorUserId)
            [void]$command.ExecuteNonQuery()

            $stage = 'RollShowcaseDates'
            $seed = Join-Path $PSScriptRoot 'Seed-DemoShowcaseData.ps1'
            if (-not (Test-Path -LiteralPath $seed -PathType Leaf)) {
                throw "The versioned Demo seed is missing at '$seed'."
            }
            & $seed -SqlServer $server -Database 'SatiDemo' -AccessToken $token -AsOfDate ([DateTime]::Today)

            $stage = 'CompleteComplianceHistory'
            Invoke-DemoComplianceSeed -Server $server -Token $token -AsOfDate ([DateTime]::Today)
            $stage = 'Completed'
        }
        finally {
            if ($connection.State -eq [System.Data.ConnectionState]::Open) {
                $release = $connection.CreateCommand()
                $release.CommandText = "EXEC sys.sp_releaseapplock @Resource=N'SatiDemo.FullReset', @LockOwner=N'Session';"
                [void]$release.ExecuteNonQuery()
            }
            $connection.Dispose()
        }
    }
    catch {
        $detail = Get-SafeFailureDetail $_
        Write-DemoResetOutcome -Server $server -Token $token -RequestId $RequestId -ActorUserId $ActorUserId `
            -Trigger $Trigger -Succeeded $false -Stage $stage -Seconds $clock.Elapsed.TotalSeconds -Detail $detail
        throw "Demo reset failed. RequestId=$RequestId Trigger=$Trigger Stage=$stage ExceptionTypes=$($detail.ExceptionTypes -join ',') SqlErrorNumbers=$($detail.SqlErrorNumbers -join ',')"
    }

    Write-DemoResetOutcome -Server $server -Token $token -RequestId $RequestId -ActorUserId $ActorUserId `
        -Trigger $Trigger -Succeeded $true -Stage $stage -Seconds $clock.Elapsed.TotalSeconds -Detail $null
    Write-Host "Full Demo reset completed. RequestId=$RequestId Trigger=$Trigger ActorUserId=$ActorUserId Seconds=$([Math]::Round($clock.Elapsed.TotalSeconds))"
}
