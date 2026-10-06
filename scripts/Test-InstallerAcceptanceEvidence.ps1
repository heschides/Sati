[CmdletBinding()]
param(
    [ValidateSet('DemoCleanupFailure','DemoSuccess','DemoKeepFiles','DemoExistingEvidence','LocalSuccess','LocalCleanupFailure','LocalSignatureFailure','LocalExistingEvidence')]
    [string]$Case = 'DemoCleanupFailure'
)

# Exercise the actual acceptance scripts with synthetic payload/process providers.
# Never start an installer, application, or database in this fixture.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('SatiAcceptanceEvidence_' + [Guid]::NewGuid().ToString('N'))
$isLocal = $Case.StartsWith('Local')
$name = if ($isLocal) { 'Local' } else { 'Demo' }
$script:clockTicks = 0
$script:installRuns = 0
try {
    [void][IO.Directory]::CreateDirectory((Join-Path $fixture 'scripts'))
    [void][IO.Directory]::CreateDirectory((Join-Path $fixture 'installer'))
    $target = Join-Path $fixture "scripts/Test-${name}Installer.ps1"
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Test-${name}Installer.ps1") -Destination $target
    foreach ($helper in @('InstallerAcceptanceEvidence.ps1','Test-SatiLocalConfiguration.ps1')) {
        $path = Join-Path $repo "installer/$helper"
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $fixture "installer/$helper") }
    }
    [IO.File]::WriteAllText((Join-Path $fixture 'installer/InstallerProcessGuard.ps1'), 'function Get-SatiInstallerRunningProcesses { param($ProcessNames) @() }')
    [IO.File]::WriteAllText((Join-Path $fixture 'installer/Read-LocalInstallerPrerequisite.ps1'), @'
function Test-SatiEmbeddedLocalDbPrerequisite {
    param($InstallerPath, $WorkingRoot)
    if ($Case -eq 'LocalSignatureFailure') { throw 'Synthetic unsigned embedded prerequisite.' }
    [pscustomobject]@{ Status='Valid'; Signer='Microsoft Corporation'; Sha256=('a' * 64) }
}
'@)
    $installer = Join-Path $fixture "Sati${name}Setup-1.3.35.exe"
    [IO.File]::WriteAllText($installer, 'synthetic installer')
    $work = Join-Path $fixture 'acceptance'
    $evidence = Join-Path $fixture 'evidence.json'
    if ($Case -in @('DemoExistingEvidence','LocalExistingEvidence')) { [IO.File]::WriteAllText($evidence, 'existing evidence') }

    function Get-Process { param($Name) @() }
    function Get-Date { $script:clockTicks += 10; [DateTime]::Today.AddSeconds($script:clockTicks) }
    function Start-Sleep { param($Milliseconds) }
    function Get-Item {
        param($LiteralPath)
        if ([IO.Path]::GetFileName($LiteralPath) -in @('Sati.exe','Sati.Demo.exe')) {
            [pscustomobject]@{ VersionInfo=[pscustomobject]@{FileVersion='1.3.35.0'} }
        } else { Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath }
    }
    function Start-Process {
        param($FilePath,$ArgumentList,$WindowStyle,[switch]$Wait,[switch]$PassThru,$WorkingDirectory)
        if ($FilePath -eq $installer) {
            $script:installRuns++
            $root = if ($isLocal) { $env:SATI_LOCAL_INSTALL_ROOT } else { $env:SATI_DEMO_INSTALL_ROOT }
            $exe = if ($isLocal) { 'Sati.exe' } else { 'Sati.Demo.exe' }
            $icon = if ($isLocal) { 'Sati.1.3.35.ico' } else { 'Sati.Demo.1.3.35.ico' }
            foreach ($file in @($exe,$icon,"Uninstall-Sati${name}.ps1",'Run-PowerShellHidden.vbs')) { [IO.File]::WriteAllText((Join-Path $root $file), 'synthetic payload') }
            [IO.File]::WriteAllText((Join-Path $root 'appsettings.Public.json'), '{"ApiEnvironments":{"Demo":{"BaseAddress":"https://example.test/"}},"DataEnvironments":{"Production":{"ExpectedDatabaseName":"SatiProduction"}}}')
            if ($isLocal) {
                # Reuse the real configuration guard, with a synthetic integrated-security mapping.
                [IO.File]::WriteAllText((Join-Path $root 'appsettings.json'), '{"ConnectionStrings":{"SatiProduction":"Server=(localdb)\\MSSQLLocalDB;Database=SatiProduction;Integrated Security=True;"}}')
            }
            return [pscustomobject]@{ExitCode=0}
        }
        $p=[pscustomobject]@{ HasExited=$false; Responding=$true; MainWindowTitle='Sign in'; ExitCode=0 }
        $p | Add-Member ScriptMethod Refresh {}
        $p | Add-Member ScriptMethod CloseMainWindow { $this.HasExited=$true; return $true }
        $p | Add-Member ScriptMethod WaitForExit { param($Milliseconds) return $true }
        return $p
    }
    function Remove-Item {
        [CmdletBinding()]
        param($LiteralPath,$Path,[switch]$Recurse,[switch]$Force)
        $remove = if ($LiteralPath) { $LiteralPath } else { $Path }
        if ($Recurse -and $Case -in @('DemoCleanupFailure','LocalCleanupFailure')) { throw 'Synthetic cleanup failure.' }
        Microsoft.PowerShell.Management\Remove-Item -LiteralPath $remove -Recurse:$Recurse -Force:$Force -ErrorAction $ErrorActionPreference
    }

    $parameters = @{ InstallerPath=$installer; WorkingRoot=$work; EvidencePath=$evidence }
    if (-not $isLocal) { $parameters.LaunchSeconds=3; $parameters.KeepInstalledFiles=($Case -eq 'DemoKeepFiles') }
    $failed=$false
    $failureDetail = ''
    try { & $target @parameters | Out-Null } catch { $failed=$true; $failureDetail=$_.Exception.Message }
    if ($Case -in @('DemoCleanupFailure','LocalCleanupFailure','LocalSignatureFailure')) {
        if (-not $failed) { throw 'Expected acceptance failure was not raised.' }
        if (Test-Path -LiteralPath $evidence) { throw 'Passing evidence survived failed acceptance/cleanup.' }
        if ($Case -eq 'LocalSignatureFailure' -and ($failureDetail -cne 'Synthetic unsigned embedded prerequisite.' -or $script:installRuns -ne 0)) { throw 'Signature failure did not stop acceptance before installer execution.' }
        if ($Case -in @('DemoCleanupFailure','LocalCleanupFailure') -and $failureDetail -cne 'Synthetic cleanup failure.') { throw "Acceptance failed before the intended cleanup failure: $failureDetail" }
    } elseif ($Case -in @('DemoExistingEvidence','LocalExistingEvidence')) {
        if (-not $failed -or [IO.File]::ReadAllText($evidence) -cne 'existing evidence') { throw 'Existing evidence was overwritten or accepted.' }
        if ($script:installRuns -ne 0) { throw 'Existing evidence was rejected only after running the installer.' }
    } else {
        if ($failed) { throw "Synthetic successful acceptance failed: $failureDetail" }
        $result=Get-Content -LiteralPath $evidence -Raw|ConvertFrom-Json
        if ($result.Passed -ne $true) { throw 'Passing result missing.' }
        if ($result.SchemaVersion -ne 1) { throw 'Acceptance evidence broke the existing company-demo evidence schema.' }
        if ($Case -eq 'DemoKeepFiles') {
            if ($result.CleanupPassed -ne $false -or $result.InstalledFilesRetained -ne $true) { throw 'Retained files incorrectly claimed cleanup.' }
        } elseif ($result.CleanupPassed -ne $true -or @(Get-ChildItem -LiteralPath $work -Directory -Filter 'run-*').Count -ne 0) { throw 'Evidence does not prove completed cleanup.' }
        if ($isLocal -and $result.EmbeddedPrerequisite.Status -cne 'Valid') { throw 'Embedded prerequisite evidence missing.' }
    }
    Write-Output "INSTALLER_ACCEPTANCE_EVIDENCE_TEST_PASSED Case=$Case"
} finally {
    $expectedParent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $resolved=[IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved) -ine $expectedParent -or [IO.Path]::GetFileName($resolved) -cnotmatch '^SatiAcceptanceEvidence_[0-9a-f]{32}$') { throw 'Unexpected fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Microsoft.PowerShell.Management\Remove-Item -LiteralPath $resolved -Recurse -Force }
}
