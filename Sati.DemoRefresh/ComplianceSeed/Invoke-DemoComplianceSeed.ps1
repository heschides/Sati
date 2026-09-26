# Shared by ResetDemo and RefreshCaseload. Runs after the canonical baseline is restored
# and Seed-DemoShowcaseData.ps1 has rolled it to today: SatiComplianceSeed --demo records
# every past-due compliance item as done on its own due date through Sati's own rules,
# leaves one recent quarterly review per caseload overdue as a teaching exception, and
# exits non-zero if anything else still blocks billing. Publish-DemoRefresh.ps1 places the
# published executable beside this file; it is not checked in.

function Invoke-DemoComplianceSeed {
    param(
        [Parameter(Mandatory = $true)][string]$Server,
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][DateTime]$AsOfDate
    )

    $tool = Join-Path $PSScriptRoot 'SatiComplianceSeed.exe'
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
        throw "The Demo compliance seed is missing at '$tool'. Republish with Publish-DemoRefresh.ps1."
    }

    # The token reaches the child process through its environment, never its command line.
    $env:SATI_SQL_ACCESS_TOKEN = $Token
    try {
        $output = & $tool --demo --apply --server $Server --as-of $AsOfDate.ToString('yyyy-MM-dd') 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Remove-Item Env:SATI_SQL_ACCESS_TOKEN -ErrorAction SilentlyContinue
    }

    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) {
        throw "The Demo compliance seed stopped with exit code $exitCode; see the lines above."
    }
}
