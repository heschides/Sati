<#
.SYNOPSIS
Runs the synthetic joined billing pipeline and optional local SQL Server race/restart proofs.
.DESCRIPTION
Does not read application configuration, deployment credentials, Demo or Production data.
The SQL fixture can create/drop only a fresh SatiSyntheticPipeline_<guid> database on
(localdb)\MSSQLLocalDB using Windows authentication. It never accepts a connection string.
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
if ($IncludeSqlServer -and -not (Get-Command SqlLocalDB -ErrorAction SilentlyContinue)) {
    throw 'SQL Server LocalDB is required for the explicitly requested SQL Server tests.'
}

$previousOptIn = [Environment]::GetEnvironmentVariable('SATI_RUN_SQLSERVER_TESTS', 'Process')
try {
    [Environment]::SetEnvironmentVariable('SATI_RUN_SQLSERVER_TESTS', $(if ($IncludeSqlServer) { '1' } else { $null }), 'Process')
    & dotnet test $testProject --configuration Release --filter `
        'FullyQualifiedName~JoinedBillingPipeline|FullyQualifiedName~ServiceTimeSqlServerConcurrencyTests|FullyQualifiedName~BillingSubmissionSqlServerConcurrencyTests|FullyQualifiedName~SyntheticPipelineSafetyTests' `
        --logger trx --results-directory (Join-Path $repositoryPath 'TestResults/SyntheticPipeline') -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Synthetic pipeline verification failed (exit $LASTEXITCODE)." }
}
finally {
    [Environment]::SetEnvironmentVariable('SATI_RUN_SQLSERVER_TESTS', $previousOptIn, 'Process')
}
