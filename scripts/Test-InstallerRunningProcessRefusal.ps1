[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$LocalInstallerPath,

    [Parameter(Mandatory)]
    [string]$DemoInstallerPath,

    [string]$WorkingRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$processGuardScript = Join-Path $repoRoot 'installer\InstallerProcessGuard.ps1'
. $processGuardScript

if ([string]::IsNullOrWhiteSpace($WorkingRoot)) {
    $WorkingRoot = Join-Path $repoRoot 'artifacts\SatiInstallerRunningProcessAcceptance'
}

$localInstaller = [System.IO.Path]::GetFullPath($LocalInstallerPath)
$demoInstaller = [System.IO.Path]::GetFullPath($DemoInstallerPath)
if (-not (Test-Path -LiteralPath $localInstaller -PathType Leaf)) {
    throw "Local installer not found: $localInstaller"
}
if (-not (Test-Path -LiteralPath $demoInstaller -PathType Leaf)) {
    throw "Demo installer not found: $demoInstaller"
}

$localInstallerName = [System.IO.Path]::GetFileName($localInstaller)
$demoInstallerName = [System.IO.Path]::GetFileName($demoInstaller)
if ($localInstallerName -notmatch '^SatiLocalSetup-(\d+\.\d+\.\d+)\.exe$') {
    throw "Local installer name must match SatiLocalSetup-x.y.z.exe: $localInstallerName"
}
$localVersion = $Matches[1]
if ($demoInstallerName -notmatch '^SatiDemoSetup-(\d+\.\d+\.\d+)\.exe$') {
    throw "Demo installer name must match SatiDemoSetup-x.y.z.exe: $demoInstallerName"
}
$demoVersion = $Matches[1]
if ($localVersion -cne $demoVersion) {
    throw "Installer versions do not match: Local $localVersion; Demo $demoVersion."
}

$acceptanceRoot = [System.IO.Path]::GetFullPath($WorkingRoot)
if ($acceptanceRoot -eq [System.IO.Path]::GetPathRoot($acceptanceRoot)) {
    throw 'WorkingRoot cannot be a drive root.'
}

$alreadyRunning = @(Get-SatiInstallerRunningProcesses -ProcessNames @('Sati', 'Sati.Demo'))
if ($alreadyRunning.Count -ne 0) {
    throw 'Close every real Sati and Sati Demo window before running the packaged-installer refusal test.'
}

$pingPath = Join-Path $env:SystemRoot 'System32\ping.exe'
if (-not (Test-Path -LiteralPath $pingPath -PathType Leaf)) {
    throw 'The controlled-process test requires Windows ping.exe.'
}

$runRoot = Join-Path $acceptanceRoot ('run-' + [Guid]::NewGuid().ToString('N'))
$priorEnvironment = @{}
foreach ($name in @(
    'SATI_LOCAL_INSTALLER_TEST',
    'SATI_LOCAL_INSTALL_ROOT',
    'SATI_DEMO_INSTALLER_TEST',
    'SATI_DEMO_INSTALL_ROOT',
    'SATI_INSTALLER_TEST_RESULT_PATH')) {
    $priorEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}

$dummyProcess = $null
$installerProcess = $null

try {
    [System.IO.Directory]::CreateDirectory($runRoot) | Out-Null
    $cases = @(
        [pscustomobject]@{ Edition = 'Local'; ProcessName = 'Sati'; Installer = $localInstaller },
        [pscustomobject]@{ Edition = 'Local'; ProcessName = 'Sati.Demo'; Installer = $localInstaller },
        [pscustomobject]@{ Edition = 'Demo'; ProcessName = 'Sati'; Installer = $demoInstaller },
        [pscustomobject]@{ Edition = 'Demo'; ProcessName = 'Sati.Demo'; Installer = $demoInstaller })

    foreach ($case in $cases) {
        $caseName = ($case.Edition + '-' + $case.ProcessName).Replace('.', '-')
        $destination = Join-Path $runRoot ($caseName + '-destination')
        [System.IO.Directory]::CreateDirectory($destination) | Out-Null
        $sentinelPath = Join-Path $destination 'preexisting-sentinel.txt'
        $sentinelValue = 'unchanged-' + [Guid]::NewGuid().ToString('N')
        Set-Content -LiteralPath $sentinelPath -Value $sentinelValue -Encoding ASCII
        $sentinelHash = (Get-FileHash -LiteralPath $sentinelPath -Algorithm SHA256).Hash

        $resultPath = Join-Path $runRoot ($caseName + '-installer-exit-code.txt')
        $dummyPath = Join-Path $runRoot ($case.ProcessName + '.exe')
        Copy-Item -LiteralPath $pingPath -Destination $dummyPath -Force

        if ($case.Edition -eq 'Local') {
            $env:SATI_LOCAL_INSTALLER_TEST = '1'
            $env:SATI_LOCAL_INSTALL_ROOT = $destination
            Remove-Item Env:SATI_DEMO_INSTALLER_TEST -ErrorAction SilentlyContinue
            Remove-Item Env:SATI_DEMO_INSTALL_ROOT -ErrorAction SilentlyContinue
        }
        else {
            $env:SATI_DEMO_INSTALLER_TEST = '1'
            $env:SATI_DEMO_INSTALL_ROOT = $destination
            Remove-Item Env:SATI_LOCAL_INSTALLER_TEST -ErrorAction SilentlyContinue
            Remove-Item Env:SATI_LOCAL_INSTALL_ROOT -ErrorAction SilentlyContinue
        }
        $env:SATI_INSTALLER_TEST_RESULT_PATH = $resultPath

        try {
            $dummyProcess = Start-Process `
                -FilePath $dummyPath `
                -ArgumentList @('-n', '600', '127.0.0.1') `
                -WindowStyle Hidden `
                -PassThru
            Start-Sleep -Milliseconds 250
            $dummyProcess.Refresh()
            if ($dummyProcess.HasExited -or $dummyProcess.ProcessName -cne $case.ProcessName) {
                throw "The controlled '$($case.ProcessName)' process did not remain available for the refusal test."
            }
            $detectedIds = @(Get-SatiInstallerRunningProcesses `
                -ProcessNames @('Sati', 'Sati.Demo') |
                Select-Object -ExpandProperty Id)
            if ($detectedIds -notcontains $dummyProcess.Id) {
                throw "The installer guard did not detect controlled process $($dummyProcess.Id)."
            }

            $installStartedAt = (Get-Date).AddSeconds(-5)
            $startParameters = @{
                FilePath = $case.Installer
                WindowStyle = 'Hidden'
                PassThru = $true
            }
            if ($case.Edition -eq 'Demo') {
                $startParameters.ArgumentList = @('/Q')
            }
            $installerProcess = Start-Process @startParameters
            if (-not $installerProcess.WaitForExit(120000)) {
                Stop-Process -Id $installerProcess.Id -Force -ErrorAction SilentlyContinue
                throw "$($case.Edition) installer did not refuse the running process within 120 seconds."
            }
            $installerProcess.Refresh()
            $outerExitCode = $installerProcess.ExitCode

            $installerExitCode = $outerExitCode
            if ($case.Edition -eq 'Demo') {
                # The IExpress stub can detach before its wextract/wscript chain completes.
                # Run-PowerShellHidden.vbs records the authoritative script exit code in
                # test mode, so wait for that result rather than accepting the stub's 0.
                $resultDeadline = (Get-Date).AddSeconds(120)
                $resultRead = $false
                $installerExitCodeText = ''
                do {
                    if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
                        try {
                            $installerExitCodeText =
                                (Get-Content -LiteralPath $resultPath -Raw).Trim()
                            $parsedExitCode = 0
                            if ([int]::TryParse($installerExitCodeText, [ref]$parsedExitCode)) {
                                $installerExitCode = $parsedExitCode
                                $resultRead = $true
                                break
                            }
                        }
                        catch {
                            # The writer may be between its atomic temporary write and rename.
                        }
                    }
                    Start-Sleep -Milliseconds 200
                } while ((Get-Date) -lt $resultDeadline)
                if (-not $resultRead) {
                    throw "The Demo installer did not report a valid launched installer exit code. Last value: '$installerExitCodeText'."
                }

                $settleDeadline = (Get-Date).AddSeconds(120)
                do {
                    $helpers = @(Get-Process -Name 'wextract' -ErrorAction SilentlyContinue |
                        Where-Object { $_.StartTime -ge $installStartedAt })
                    if ($helpers.Count -eq 0) { break }
                    Start-Sleep -Milliseconds 200
                } while ((Get-Date) -lt $settleDeadline)
                if ($helpers.Count -ne 0) {
                    throw 'The Demo installer extraction helper did not settle after process refusal.'
                }
            }

            if ($installerExitCode -ne 2) {
                throw "$($case.Edition) installer returned $installerExitCode instead of refusal exit code 2."
            }
            if ((Get-FileHash -LiteralPath $sentinelPath -Algorithm SHA256).Hash -cne $sentinelHash) {
                throw "$($case.Edition) installer changed the preexisting sentinel after refusing to run."
            }
            $destinationEntries = @(Get-ChildItem -LiteralPath $destination -Force)
            if ($destinationEntries.Count -ne 1 -or
                $destinationEntries[0].Name -cne 'preexisting-sentinel.txt') {
                throw "$($case.Edition) installer changed its isolated destination before refusing to run."
            }

            Write-Output (
                "PACKAGED_INSTALLER_REFUSAL_PASSED edition=$($case.Edition) " +
                "runningProcess=$($case.ProcessName) installerExitCode=2 " +
                "outerExitCode=$outerExitCode destinationUnchanged=True")
        }
        finally {
            if ($null -ne $installerProcess -and -not $installerProcess.HasExited) {
                Stop-Process -Id $installerProcess.Id -Force -ErrorAction SilentlyContinue
                [void]$installerProcess.WaitForExit(10000)
            }
            $installerProcess = $null

            # This is the exact controlled ping.exe copy created above; the installers
            # themselves never stop a user's process.
            if ($null -ne $dummyProcess -and -not $dummyProcess.HasExited) {
                Stop-Process -Id $dummyProcess.Id -Force -ErrorAction SilentlyContinue
                [void]$dummyProcess.WaitForExit(10000)
            }
            $dummyProcess = $null
        }
    }

    Write-Output "PACKAGED_INSTALLER_RUNNING_PROCESS_ACCEPTANCE_PASSED version=$localVersion cases=4"
}
finally {
    if ($null -ne $installerProcess -and -not $installerProcess.HasExited) {
        Stop-Process -Id $installerProcess.Id -Force -ErrorAction SilentlyContinue
        [void]$installerProcess.WaitForExit(10000)
    }
    if ($null -ne $dummyProcess -and -not $dummyProcess.HasExited) {
        Stop-Process -Id $dummyProcess.Id -Force -ErrorAction SilentlyContinue
        [void]$dummyProcess.WaitForExit(10000)
    }

    foreach ($name in $priorEnvironment.Keys) {
        $priorValue = $priorEnvironment[$name]
        if ($null -eq $priorValue) {
            Remove-Item ("Env:" + $name) -ErrorAction SilentlyContinue
        }
        else {
            [Environment]::SetEnvironmentVariable($name, $priorValue)
        }
    }

    if (Test-Path -LiteralPath $runRoot -PathType Container) {
        $resolvedRunRoot = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $runRoot))
        if ([System.IO.Directory]::GetParent($resolvedRunRoot).FullName -cne $acceptanceRoot -or
            [System.IO.Path]::GetFileName($resolvedRunRoot) -notlike 'run-*') {
            throw "Refusing to clean unexpected acceptance path: $resolvedRunRoot"
        }
        $reparsePoints = @(Get-ChildItem -LiteralPath $resolvedRunRoot -Recurse -Force | Where-Object {
            $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint
        })
        if ($reparsePoints.Count -ne 0) {
            throw 'Refusing cleanup because the packaged-installer acceptance folder contains a reparse point.'
        }
        Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
        Write-Output 'PACKAGED_INSTALLER_RUNNING_PROCESS_ACCEPTANCE_CLEANUP_PASSED'
    }
}
