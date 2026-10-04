<#
.SYNOPSIS
    Runs opt-in SQL tests in a fresh, private LocalDB instance.
.DESCRIPTION
    Creates only a new SatiSqlTests_<guid> named instance owned by this run.
    Test fixtures create their own uniquely named synthetic databases there.
    The shared MSSQLLocalDB instance and SatiProduction are never connected to.
    Cleanup stops and deletes only the instance this script successfully created.
.EXAMPLE
    pwsh -File scripts/Test-IsolatedLocalDb.ps1
.EXAMPLE
    pwsh -File scripts/Test-IsolatedLocalDb.ps1 -ApiOnly
.EXAMPLE
    pwsh -File scripts/Test-IsolatedLocalDb.ps1 -NoteAbandonmentOnly
.EXAMPLE
    pwsh -File scripts/Test-IsolatedLocalDb.ps1 -FullSolution
.EXAMPLE
    pwsh -File scripts/Test-IsolatedLocalDb.ps1 -ClaimMdPreparationOnly
.EXAMPLE
    pwsh -File scripts/Test-IsolatedLocalDb.ps1 -FullApi
#>
[CmdletBinding()]
param(
    [switch]$ApiOnly,
    [switch]$DesktopOnly,
    [switch]$NoteAbandonmentOnly,
    [switch]$ClaimMdPreparationOnly,
    [switch]$NoteAmendmentsOnly,
    [switch]$FullApi,
    [switch]$FullSolution
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (@(@($ApiOnly, $DesktopOnly, $NoteAbandonmentOnly, $ClaimMdPreparationOnly, $NoteAmendmentsOnly, $FullApi, $FullSolution) | Where-Object { $_ }).Count -gt 1) {
    throw 'Choose at most one test-project filter.'
}
if ($env:OS -cne 'Windows_NT') { throw 'Isolated LocalDB tests require Windows.' }

$localDb = Get-Command SqlLocalDB -CommandType Application -ErrorAction Stop
$repository = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repository 'Sati.Api.Tests/Sati.Api.Tests.csproj'
$desktopProject = Join-Path $repository 'Sati.Tests/Sati.Tests.csproj'
if (-not (Test-Path -LiteralPath $apiProject -PathType Leaf) -or
    -not (Test-Path -LiteralPath $desktopProject -PathType Leaf)) {
    throw 'The SQL test projects are missing.'
}

$instance = 'SatiSqlTests_' + [Guid]::NewGuid().ToString('N')
if ($instance -cnotmatch '\ASatiSqlTests_[0-9a-f]{32}\z') {
    throw 'The generated LocalDB instance name is invalid.'
}
$existingInstances = @(& $localDb.Source info)
if ($LASTEXITCODE -ne 0) { throw 'Could not verify existing LocalDB instance names.' }
if ($existingInstances | Where-Object { $_.ToString().Trim() -ceq $instance }) {
    throw 'The generated LocalDB instance already exists; refusing to reuse it.'
}

$priorOptIn = [Environment]::GetEnvironmentVariable('SATI_RUN_SQLSERVER_TESTS', 'Process')
$priorInstance = [Environment]::GetEnvironmentVariable('SATI_SQL_TEST_LOCALDB_INSTANCE', 'Process')
$created = $false
$started = $false
$runFailure = $null
$cleanupFailures = [System.Collections.Generic.List[string]]::new()
try {
    & $localDb.Source create $instance | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Could not create isolated LocalDB instance $instance." }
    $afterCreate = @(& $localDb.Source info)
    if ($LASTEXITCODE -ne 0 -or
        -not @($afterCreate | Where-Object { $_.ToString().Trim() -ceq $instance }).Count) {
        throw "LocalDB did not list the newly created isolated instance $instance."
    }
    $created = $true

    & $localDb.Source start $instance | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Could not start isolated LocalDB instance $instance." }
    $started = $true

    [Environment]::SetEnvironmentVariable('SATI_RUN_SQLSERVER_TESTS', '1', 'Process')
    [Environment]::SetEnvironmentVariable('SATI_SQL_TEST_LOCALDB_INSTANCE', $instance, 'Process')

    if ($FullSolution) {
        $solutionArgs = @('test', (Join-Path $repository 'SatiLogica.slnx'),
            '--configuration', 'Release', '--no-restore', '--logger', 'trx',
            '--results-directory', (Join-Path $repository 'TestResults/BackgroundWorkersFullSql'),
            '-v', 'minimal')
        & dotnet @solutionArgs
        if ($LASTEXITCODE -ne 0) { throw "Full solution test run failed (exit $LASTEXITCODE)." }
    }

    if (-not $DesktopOnly -and -not $FullSolution) {
        $apiFilter = if ($NoteAmendmentsOnly) {
            'FullyQualifiedName~NoteAmendmentMigrationSqlTests'
        }
        elseif ($ClaimMdPreparationOnly) {
            'FullyQualifiedName~DemoWorkerResetCoordinationTests|FullyQualifiedName~DemoExternalClearinghouseGuardSqlTests|FullyQualifiedName~ClaimMdOnboardingTests.SqlGlobalLeaseSerializesTwoHostsAndExactReplayAfterRelease'
        }
        elseif ($NoteAbandonmentOnly) {
            'FullyQualifiedName~NoteAbandonmentWorkerTests.DemoResetExclusiveLeasePreventsWorkerSweep|FullyQualifiedName~NoteAbandonmentWorkerTests.SeparateApiHostsCannotSweepAtTheSameTime'
        }
        else {
            'FullyQualifiedName~NoteAmendmentMigrationSqlTests|FullyQualifiedName~JoinedBillingPipeline|FullyQualifiedName~ServiceTimeSqlServerConcurrencyTests|FullyQualifiedName~BillingSubmissionSqlServerConcurrencyTests|FullyQualifiedName~ClaimMdSandboxCoordinationTests|FullyQualifiedName~SyntheticPipelineSafetyTests|FullyQualifiedName~DemoWatchdogSchemaTests.WatchdogSelectsCompileAgainstIsolatedSqlServerSchema|FullyQualifiedName~NoteAbandonmentWorkerTests.DemoResetExclusiveLeasePreventsWorkerSweep|FullyQualifiedName~NoteAbandonmentWorkerTests.SeparateApiHostsCannotSweepAtTheSameTime|FullyQualifiedName~DemoWorkerResetCoordinationTests'
        }
        $apiArgs = @('test', $apiProject, '--configuration', 'Release')
        if (-not $FullApi) { $apiArgs += @('--filter', $apiFilter) }
        $apiResults = if ($FullApi) { 'TestResults/ClaimMdPreparationFinalApi' } else { 'TestResults/IsolatedSqlServer/Api' }
        $apiArgs += @('--logger', 'trx', '--results-directory', (Join-Path $repository $apiResults), '-v', 'minimal')
        & dotnet @apiArgs
        if ($LASTEXITCODE -ne 0) { throw "API SQL test project failed (exit $LASTEXITCODE)." }
    }

    if (-not $ApiOnly -and -not $NoteAbandonmentOnly -and -not $ClaimMdPreparationOnly -and -not $NoteAmendmentsOnly -and -not $FullApi -and -not $FullSolution) {
        $desktopFilter = 'FullyQualifiedName~LocalServiceTimeSqlServerTests|FullyQualifiedName~MigrationEffectAnalyzerAgainstLiveSchemaTests|FullyQualifiedName~WorkAgendaMigrationTests.SystemDataSqlClientSessionTempTableSurvivesParameterizedCommands|FullyQualifiedName~WorkAgendaMigrationTests.DuplicateRepairKeepsLowestExactRowCancelsSafeFanOutAndSkipsUnsafeGroups'
        $desktopArgs = @('test', $desktopProject, '--configuration', 'Release', '--filter', $desktopFilter,
            '--logger', 'trx', '--results-directory', (Join-Path $repository 'TestResults/IsolatedSqlServer/Desktop'),
            '-v', 'minimal')
        & dotnet @desktopArgs
        if ($LASTEXITCODE -ne 0) { throw "Desktop SQL test project failed (exit $LASTEXITCODE)." }
    }
}
catch {
    $runFailure = $_
}
finally {
    [Environment]::SetEnvironmentVariable('SATI_RUN_SQLSERVER_TESTS', $priorOptIn, 'Process')
    [Environment]::SetEnvironmentVariable('SATI_SQL_TEST_LOCALDB_INSTANCE', $priorInstance, 'Process')

    if ($created) {
        if ($started) {
            try {
                & $localDb.Source stop $instance | Out-Host
                if ($LASTEXITCODE -ne 0) {
                    $cleanupFailures.Add("Could not stop isolated LocalDB instance $instance.")
                }
            }
            catch {
                $cleanupFailures.Add("Could not stop isolated LocalDB instance $instance.")
            }
        }
        try {
            & $localDb.Source delete $instance | Out-Host
            if ($LASTEXITCODE -ne 0) {
                $cleanupFailures.Add("Could not delete isolated LocalDB instance $instance.")
            }
        }
        catch {
            $cleanupFailures.Add("Could not delete isolated LocalDB instance $instance.")
        }
    }
}

if ($runFailure) {
    if ($cleanupFailures.Count -gt 0) {
        throw "$($runFailure.Exception.Message) Cleanup also failed: $($cleanupFailures -join ' ')"
    }
    throw $runFailure
}
if ($cleanupFailures.Count -gt 0) { throw ($cleanupFailures -join ' ') }
