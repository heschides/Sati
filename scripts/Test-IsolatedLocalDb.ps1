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
#>
[CmdletBinding()]
param(
    [switch]$ApiOnly,
    [switch]$DesktopOnly
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ($ApiOnly -and $DesktopOnly) { throw 'Choose at most one test-project filter.' }
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
    $created = $true

    & $localDb.Source start $instance | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Could not start isolated LocalDB instance $instance." }
    $started = $true

    [Environment]::SetEnvironmentVariable('SATI_RUN_SQLSERVER_TESTS', '1', 'Process')
    [Environment]::SetEnvironmentVariable('SATI_SQL_TEST_LOCALDB_INSTANCE', $instance, 'Process')

    if (-not $DesktopOnly) {
        $apiFilter = 'FullyQualifiedName~JoinedBillingPipeline|FullyQualifiedName~ServiceTimeSqlServerConcurrencyTests|FullyQualifiedName~BillingSubmissionSqlServerConcurrencyTests|FullyQualifiedName~ClaimMdSandboxCoordinationTests|FullyQualifiedName~SyntheticPipelineSafetyTests'
        $apiArgs = @('test', $apiProject, '--configuration', 'Release', '--filter', $apiFilter,
            '--logger', 'trx', '--results-directory', (Join-Path $repository 'TestResults/IsolatedSqlServer/Api'),
            '-v', 'minimal')
        & dotnet @apiArgs
        if ($LASTEXITCODE -ne 0) { throw "API SQL test project failed (exit $LASTEXITCODE)." }
    }

    if (-not $ApiOnly) {
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
