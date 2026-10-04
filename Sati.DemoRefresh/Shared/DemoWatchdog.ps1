# Read-only operational watchdog. The existing reset helper supplies only the
# managed-identity token and validated SatiDemo SQL connection. Nothing here
# invokes a reset, reads queue bodies, or writes to SQL.
. (Join-Path $PSScriptRoot 'DemoReset.ps1')

function New-DemoWatchdogCommand {
    param($Connection, [string]$Text)
    $command = $Connection.CreateCommand()
    $command.CommandTimeout = 20
    $command.CommandText = $Text
    return $command
}

function Assert-DemoWatchdogDatabase {
    param($Connection)
    $command = New-DemoWatchdogCommand $Connection @'
SELECT CASE WHEN DB_NAME() = N'SatiDemo' AND EXISTS
    (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id = 1 AND EnvironmentName = N'Demo')
    THEN 1 ELSE 0 END;
'@
    if ([int]$command.ExecuteScalar() -ne 1) {
        throw 'The watchdog is not connected to the validated SatiDemo database.'
    }
}

function Read-DemoWatchdogResetState {
    param($Connection, [DateTime]$ScheduledStartUtc)
    $command = New-DemoWatchdogCommand $Connection @'
SELECT
    (SELECT COUNT_BIG(*) FROM dbo.AuditEvents
     WHERE Action IN (N'demo.reset.completed', N'demo.reset.failed')
       AND OccurredAtUtc >= @ScheduledStartUtc) AS RecentOutcomeCount,
    (SELECT TOP (1) Action FROM dbo.AuditEvents
     WHERE Action IN (N'demo.reset.completed', N'demo.reset.failed')
     ORDER BY OccurredAtUtc DESC, Id DESC) AS LatestAction;
'@
    [void]$command.Parameters.Add('@ScheduledStartUtc', [Data.SqlDbType]::DateTime2)
    $command.Parameters['@ScheduledStartUtc'].Value = $ScheduledStartUtc
    $reader = $command.ExecuteReader()
    try {
        if (-not $reader.Read()) { throw 'The Demo reset audit query returned no result.' }
        return [pscustomobject]@{
            RecentOutcomeCount = [long]$reader.GetInt64(0)
            LatestAction = if ($reader.IsDBNull(1)) { $null } else { $reader.GetString(1) }
        }
    }
    finally { $reader.Dispose() }
}

function Read-DemoWatchdogOverdueCount {
    param($Connection, [DateTime]$Today)
    # This is a conservative backlog signal, not a status-changing rule. The
    # worker owns the authoritative Contracts decision and gets two extra days.
    $command = New-DemoWatchdogCommand $Connection @'
SELECT COUNT_BIG(*)
FROM dbo.Notes AS n
JOIN dbo.People AS p ON p.Id = n.PersonId AND p.AgencyId = n.AgencyId
LEFT JOIN dbo.Settings AS s ON s.AgencyId = p.AgencyId
WHERE n.Status = 1 AND n.EventDate IS NOT NULL
  AND n.EventDate < DATEADD(day,
      -(CASE WHEN s.AbandonedAfterDays > 0 THEN s.AbandonedAfterDays ELSE 7 END + 2), @Today);
'@
    [void]$command.Parameters.Add('@Today', [Data.SqlDbType]::Date)
    $command.Parameters['@Today'].Value = $Today.Date
    return [long]$command.ExecuteScalar()
}

function Read-DemoWatchdogBillingState {
    param($Connection, [DateTime]$NowUtc)
    # The Function expectation flag is off until the source-only dispatch
    # feature and its migration are deliberately activated in Demo.
    $command = New-DemoWatchdogCommand $Connection @'
SELECT
    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.ClearinghouseAccounts
        WHERE IsEnabled = 1 AND IsTest = 1) THEN 1 ELSE 0 END AS bit) AS Enabled,
    (SELECT COUNT_BIG(*) FROM dbo.ClearinghouseDispatches AS d
     JOIN dbo.ClearinghouseAccounts AS a ON a.Id = d.AccountId AND a.AgencyId = d.AgencyId
     WHERE a.IsEnabled = 1 AND a.IsTest = 1 AND d.State IN (1, 2)
       AND d.RequestedAtUtc < DATEADD(hour, -1, @NowUtc)) AS StaleDispatches,
    (SELECT COUNT_BIG(*) FROM dbo.ClearinghouseDispatches AS d
     JOIN dbo.ClearinghouseAccounts AS a ON a.Id = d.AccountId AND a.AgencyId = d.AgencyId
     WHERE a.IsEnabled = 1 AND a.IsTest = 1 AND d.State = 5
       AND d.RequestedAtUtc < DATEADD(hour, -1, @NowUtc)) AS UnknownOutcomes;
'@
    [void]$command.Parameters.Add('@NowUtc', [Data.SqlDbType]::DateTime2)
    $command.Parameters['@NowUtc'].Value = $NowUtc
    $reader = $command.ExecuteReader()
    try {
        if (-not $reader.Read()) { throw 'The Demo billing query returned no result.' }
        return [pscustomobject]@{
            Enabled = $reader.GetBoolean(0)
            StaleDispatches = [long]$reader.GetInt64(1)
            UnknownOutcomes = [long]$reader.GetInt64(2)
        }
    }
    finally { $reader.Dispose() }
}

function Read-DemoWatchdogSignatureState {
    param($Connection, [DateTime]$NowUtc)
    $command = New-DemoWatchdogCommand $Connection @'
SELECT
    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Settings
        WHERE IsInternalElectronicSignatureEnabled = 1) THEN 1 ELSE 0 END AS bit) AS Enabled,
    (SELECT COUNT_BIG(*) FROM dbo.SignatureCompletions AS c
     JOIN dbo.Settings AS s ON s.AgencyId = c.AgencyId
     WHERE s.IsInternalElectronicSignatureEnabled = 1
       AND c.SignedAtUtc < DATEADD(hour, -1, @NowUtc)
       AND NOT EXISTS (SELECT 1 FROM dbo.SignaturePackages AS p
           WHERE p.AgencyId = c.AgencyId AND p.CompletionId = c.Id)) AS UnpreparedPackages,
    (SELECT COUNT_BIG(*) FROM dbo.SignatureOutbox AS o
     JOIN dbo.Settings AS s ON s.AgencyId = o.AgencyId
     WHERE s.IsInternalElectronicSignatureEnabled = 1
       AND o.Purpose IN (N'Invitation', N'Receipt')
       AND o.State IN (N'NeedsReview', N'Failed')) AS FailedNotifications;
'@
    [void]$command.Parameters.Add('@NowUtc', [Data.SqlDbType]::DateTime2)
    $command.Parameters['@NowUtc'].Value = $NowUtc
    $reader = $command.ExecuteReader()
    try {
        if (-not $reader.Read()) { throw 'The Demo signature query returned no result.' }
        return [pscustomobject]@{
            Enabled = $reader.GetBoolean(0)
            UnpreparedPackages = [long]$reader.GetInt64(1)
            FailedNotifications = [long]$reader.GetInt64(2)
        }
    }
    finally { $reader.Dispose() }
}

function Get-DemoPoisonQueueCount {
    # AzureWebJobsStorage belongs to this existing Function App. Only the queue
    # metadata is fetched; message bodies are never peeked, received, or logged.
    $raw = $env:AzureWebJobsStorage
    if ([string]::IsNullOrWhiteSpace($raw)) { throw 'The Function storage connection is unavailable.' }
    $parts = @{}
    foreach ($part in $raw.Split(';', [StringSplitOptions]::RemoveEmptyEntries)) {
        $separator = $part.IndexOf('=')
        if ($separator -gt 0) { $parts[$part.Substring(0, $separator)] = $part.Substring($separator + 1) }
    }
    $account = [string]$parts['AccountName']
    $key = [string]$parts['AccountKey']
    if (($account -cne 'satidemorefreshst') -or
        ($parts['DefaultEndpointsProtocol'] -cne 'https') -or
        ((-not [string]::IsNullOrWhiteSpace([string]$parts['EndpointSuffix'])) -and
            ($parts['EndpointSuffix'] -cne 'core.windows.net')) -or
        [string]::IsNullOrWhiteSpace($key)) {
        throw 'The Function storage connection is not the reviewed Demo account.'
    }
    $date = [DateTime]::UtcNow.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
    $version = '2023-11-03'
    $canonical = @('GET', '', '', '', '', '', '', '', '', '', '', '',
        "x-ms-date:$date", "x-ms-version:$version",
        "/$account/demo-reset-requests-poison", 'comp:metadata') -join "`n"
    $hmac = [Security.Cryptography.HMACSHA256]::new([Convert]::FromBase64String($key))
    try {
        $signature = [Convert]::ToBase64String($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical)))
    }
    finally { $hmac.Dispose() }
    try {
        $response = Invoke-WebRequest -Method Get -Uri "https://$account.queue.core.windows.net/demo-reset-requests-poison?comp=metadata" `
            -Headers @{
                'x-ms-date' = $date
                'x-ms-version' = $version
                'Authorization' = "SharedKey ${account}:$signature"
            } -TimeoutSec 15 -ErrorAction Stop
    }
    catch {
        # A never-created poison queue has no messages. Authentication failures,
        # outages, and other HTTP statuses must still fail the watchdog check.
        $statusCode = $_.Exception.StatusCode
        if ($null -eq $statusCode -and $null -ne $_.Exception.Response) {
            $statusCode = $_.Exception.Response.StatusCode
        }
        if ([int]$statusCode -eq 404) { return 0 }
        throw
    }
    $count = 0
    if (-not [int]::TryParse([string]$response.Headers['x-ms-approximate-messages-count'], [ref]$count) -or $count -lt 0) {
        throw 'Poison queue metadata did not contain a valid approximate message count.'
    }
    return $count
}

function Test-DemoWatchdogExpectation([string]$Name) {
    return [string]::Equals([Environment]::GetEnvironmentVariable($Name, 'Process'),
        'true', [StringComparison]::OrdinalIgnoreCase)
}

function Invoke-DemoWatchdog {
    param([Parameter(Mandatory = $true)][DateTime]$NowUtc)

    $findings = [Collections.Generic.List[string]]::new()
    $connection = $null
    try {
        $server = $env:SATI_DEMO_SQL_SERVER
        if ([string]::IsNullOrWhiteSpace($server)) { throw 'The Demo SQL server is unavailable.' }
        $eastern = [TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time')
        $utc = $NowUtc.ToUniversalTime()
        $local = [TimeZoneInfo]::ConvertTimeFromUtc($utc, $eastern)
        $scheduledDay = $local.Date
        if ($local.TimeOfDay -lt [TimeSpan]::FromHours(3.25)) { $scheduledDay = $scheduledDay.AddDays(-1) }
        $scheduledLocal = [DateTime]::SpecifyKind($scheduledDay.AddHours(3.25), [DateTimeKind]::Unspecified)
        $scheduledStartUtc = [TimeZoneInfo]::ConvertTimeToUtc($scheduledLocal, $eastern)

        $token = Get-DemoSqlToken
        $connection = New-DemoConnection $server $token
        Assert-DemoWatchdogDatabase $connection
        $reset = Read-DemoWatchdogResetState $connection $scheduledStartUtc
        # During a reviewed sandbox window the Function timer itself is disabled.
        # Its actual Azure Functions setting suspends only the scheduled-outcome
        # expectation; failed outcomes and all other checks remain actionable.
        if ([long]$reset.RecentOutcomeCount -eq 0 -and
            -not (Test-DemoWatchdogExpectation 'AzureWebJobs.RefreshCaseload.Disabled')) {
            $findings.Add('ResetOutcomeMissing=1')
        }
        if ($reset.LatestAction -ceq 'demo.reset.failed') { $findings.Add('ResetOutcomeFailed=1') }

        $poison = [int](Get-DemoPoisonQueueCount)
        if ($poison -gt 0) { $findings.Add("PoisonQueueMessages=$poison") }
        if (Test-DemoWatchdogExpectation 'SATI_WATCHDOG_EXPECT_NOTE_ABANDONMENT') {
            $overdue = [long](Read-DemoWatchdogOverdueCount $connection $local.Date)
            if ($overdue -gt 0) { $findings.Add("OverduePendingNotes=$overdue") }
        }
        if (Test-DemoWatchdogExpectation 'SATI_WATCHDOG_EXPECT_CLEARINGHOUSE') {
            $billing = Read-DemoWatchdogBillingState $connection $utc
            if ($billing.Enabled) {
                if ([long]$billing.StaleDispatches -gt 0) { $findings.Add("BillingDispatchStale=$($billing.StaleDispatches)") }
                if ([long]$billing.UnknownOutcomes -gt 0) { $findings.Add("BillingOutcomeUnknown=$($billing.UnknownOutcomes)") }
            }
        }
        if (Test-DemoWatchdogExpectation 'SATI_WATCHDOG_EXPECT_SIGNATURE') {
            $signature = Read-DemoWatchdogSignatureState $connection $utc
            if ($signature.Enabled) {
                if ([long]$signature.UnpreparedPackages -gt 0) { $findings.Add("SignaturePackageUnprepared=$($signature.UnpreparedPackages)") }
                if ([long]$signature.FailedNotifications -gt 0) { $findings.Add("SignatureNotificationFailed=$($signature.FailedNotifications)") }
            }
        }
    }
    catch {
        # Exception messages can contain connection data or protected SQL values.
        # Preserve conditions already found; the failed check is also actionable.
        $findings.Add('WatchdogCheckFailed=1')
        $findings.Add("FailureType=$($_.Exception.GetType().Name)")
    }
    finally {
        if ($null -ne $connection) { try { $connection.Dispose() } catch { } }
    }
    if ($findings.Count -eq 0) { Write-Host 'SATI_WATCHDOG_OK' }
    else { Write-Host "SATI_WATCHDOG_FINDING $($findings -join ' ')" }
}
