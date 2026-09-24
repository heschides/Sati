$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$guardPath = Join-Path $repoRoot 'installer\InstallerProcessGuard.ps1'
. $guardPath

$syntheticProcesses = @(
    [pscustomobject]@{ ProcessName = 'unrelated' },
    [pscustomobject]@{ ProcessName = 'sati' },
    [pscustomobject]@{ ProcessName = 'SATI.DEMO' })
$running = @(Get-SatiInstallerRunningProcesses `
    -ProcessNames @('Sati', 'Sati.Demo') `
    -ProcessProvider { $syntheticProcesses })
if ($running.Count -ne 2) {
    throw "Expected both Sati process names to be detected; found $($running.Count)."
}

$none = @(Get-SatiInstallerRunningProcesses `
    -ProcessNames @('Sati', 'Sati.Demo') `
    -ProcessProvider { @([pscustomobject]@{ ProcessName = 'unrelated' }) })
if ($none.Count -ne 0) {
    throw 'An unrelated process was incorrectly treated as Sati.'
}

$failedClosed = $false
try {
    Get-SatiInstallerRunningProcesses `
        -ProcessNames @('Sati', 'Sati.Demo') `
        -ProcessProvider { throw 'Synthetic process enumeration failure.' } | Out-Null
}
catch {
    if ($_.Exception.Message -notlike 'Setup could not verify whether Sati is running.*') {
        throw
    }
    $failedClosed = $true
}
if (-not $failedClosed) {
    throw 'A process-enumeration failure did not stop the installer guard.'
}

Write-Output 'INSTALLER_PROCESS_GUARD_TESTS_PASSED'
