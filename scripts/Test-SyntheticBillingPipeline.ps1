<#
.SYNOPSIS
Runs the synthetic joined billing pipeline and optional local SQL Server race/restart proofs.
.DESCRIPTION
Does not read application configuration, deployment credentials, Demo or Production data.
With -IncludeSqlServer, delegates to Test-IsolatedLocalDb.ps1, which owns a
fresh named LocalDB instance and synthetic databases. No connection string is accepted.
Uses the current EF model; this is not a migration, backup/restore or vendor acceptance test.
.EXAMPLE
pwsh -File scripts/Test-SyntheticBillingPipeline.ps1 -IncludeSqlServer
#>
[CmdletBinding()]
param([switch]$IncludeSqlServer)

$ErrorActionPreference = 'Stop'
$repositoryPath = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repositoryPath 'Sati.Api.Tests/Sati.Api.Tests.csproj'
if (-not (Test-Path -LiteralPath $testProject -PathType Leaf)) {
    throw 'The synthetic pipeline test project is missing.'
}
if ($IncludeSqlServer) {
    & (Join-Path $PSScriptRoot 'Test-IsolatedLocalDb.ps1') -ApiOnly
    return
}

$previousOptIn = [Environment]::GetEnvironmentVariable('SATI_RUN_SQLSERVER_TESTS', 'Process')
try {
    [Environment]::SetEnvironmentVariable('SATI_RUN_SQLSERVER_TESTS', $null, 'Process')
    & dotnet test $testProject --configuration Release --filter `
        'FullyQualifiedName~JoinedBillingPipeline|FullyQualifiedName~ServiceTimeSqlServerConcurrencyTests|FullyQualifiedName~BillingSubmissionSqlServerConcurrencyTests|FullyQualifiedName~SyntheticPipelineSafetyTests' `
        --logger trx --results-directory (Join-Path $repositoryPath 'TestResults/SyntheticPipeline') -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Synthetic pipeline verification failed (exit $LASTEXITCODE)." }
}
finally {
    [Environment]::SetEnvironmentVariable('SATI_RUN_SQLSERVER_TESTS', $previousOptIn, 'Process')
}
