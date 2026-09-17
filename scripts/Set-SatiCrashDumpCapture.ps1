<#
.SYNOPSIS
    Turns Windows Error Reporting crash-dump capture for Sati on or off on this workstation.

.DESCRIPTION
    A stack overflow or native fault ends Sati before any managed handler runs, so Sati's own
    log records only that the session died. A full dump is the only artifact that shows where.

    Enabling writes HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\<exe>
    (administrator required). Dumps go to %LOCALAPPDATA%\SatiLogica\Sati\CrashDumps, which
    Windows expands for whichever user's Sati crashed, so each login keeps its own dumps in its
    own profile. At most -DumpCount dumps are kept; Windows discards the oldest.

    A full dump contains process memory, which includes whatever client records were open.
    Treat every dump as protected health information: analyze it on this machine, never copy,
    email, or upload it, and delete it when the analysis is done (-Disable -RemoveDumps).
    See DECISIONS.md, 2026-09-17.

.EXAMPLE
    # Show the current setting and any dumps for the signed-in user. No elevation needed.
    ./Set-SatiCrashDumpCapture.ps1 -Status

.EXAMPLE
    # From an elevated PowerShell: capture full dumps of Sati.exe from now on.
    ./Set-SatiCrashDumpCapture.ps1 -Enable

.EXAMPLE
    # From an elevated PowerShell: stop capturing, and delete the signed-in user's dumps.
    ./Set-SatiCrashDumpCapture.ps1 -Disable -RemoveDumps
#>
[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'Status')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Enable')]
    [switch]$Enable,

    [Parameter(Mandatory, ParameterSetName = 'Disable')]
    [switch]$Disable,

    [Parameter(ParameterSetName = 'Status')]
    [switch]$Status,

    # Sati.exe is the Local (My work) build; Sati.Demo.exe is the Demo build.
    [ValidateSet('Sati.exe', 'Sati.Demo.exe')]
    [string]$Executable = 'Sati.exe',

    [Parameter(ParameterSetName = 'Enable')]
    [ValidateRange(1, 10)]
    [int]$DumpCount = 3,

    [Parameter(ParameterSetName = 'Disable')]
    [switch]$RemoveDumps
)

$ErrorActionPreference = 'Stop'

$localDumpsKey = 'HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps'
$appKey = Join-Path $localDumpsKey $Executable
# Stored unexpanded (REG_EXPAND_SZ) so WER resolves it per crashing user.
$dumpFolderSetting = '%LOCALAPPDATA%\SatiLogica\Sati\CrashDumps'
$myDumpFolder = [Environment]::ExpandEnvironmentVariables($dumpFolderSetting)
$fullDump = 2

function Assert-Administrator {
    if ($WhatIfPreference) { return }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $identity
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Changing crash-dump capture writes to HKLM. Run this from an elevated PowerShell ("Run as administrator").'
    }
}

function Show-Status {
    if (Test-Path $appKey) {
        $values = Get-ItemProperty $appKey
        $type = switch ($values.DumpType) { 0 { 'custom' } 1 { 'mini' } 2 { 'full' } default { 'unset (mini)' } }
        Write-Host "Capture for $Executable is ON: $type dumps, keeping $($values.DumpCount)."
        Write-Host "  Folder setting: $($values.DumpFolder)"
    }
    else {
        Write-Host "Capture for $Executable is OFF."
    }

    $dumps = @()
    if (Test-Path $myDumpFolder) {
        $dumps = @(Get-ChildItem $myDumpFolder -Filter "$Executable*.dmp" -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending)
    }
    if ($dumps.Count -eq 0) {
        Write-Host "No $Executable dumps for $env:USERNAME in $myDumpFolder."
    }
    else {
        Write-Host "$($dumps.Count) dump(s) for $env:USERNAME (protected health information; do not copy off this machine):"
        $dumps | ForEach-Object {
            Write-Host ("  {0:yyyy-MM-dd HH:mm}  {1,8:N0} MB  {2}" -f $_.LastWriteTime, ($_.Length / 1MB), $_.FullName)
        }
    }
    Write-Host 'Other Windows logins keep their own dumps; run -Status while signed in as that user to see them.'
}

switch ($PSCmdlet.ParameterSetName) {
    'Status' {
        Show-Status
    }

    'Enable' {
        Assert-Administrator
        if ($PSCmdlet.ShouldProcess($appKey, "Capture full dumps of $Executable, keeping $DumpCount")) {
            if (-not (Test-Path $localDumpsKey)) { New-Item -Path $localDumpsKey | Out-Null }
            if (-not (Test-Path $appKey)) { New-Item -Path $appKey | Out-Null }
            New-ItemProperty -Path $appKey -Name DumpFolder -PropertyType ExpandString -Value $dumpFolderSetting -Force | Out-Null
            New-ItemProperty -Path $appKey -Name DumpType -PropertyType DWord -Value $fullDump -Force | Out-Null
            New-ItemProperty -Path $appKey -Name DumpCount -PropertyType DWord -Value $DumpCount -Force | Out-Null
            # The folder is created in the elevated user's profile only; WER creates it for
            # any other user on their first captured crash.
            New-Item -ItemType Directory -Path $myDumpFolder -Force | Out-Null
            Show-Status
            Write-Host ''
            Write-Host 'Turn this off once a crash has been captured and analyzed: -Disable -RemoveDumps'
        }
    }

    'Disable' {
        Assert-Administrator
        if ((Test-Path $appKey) -and $PSCmdlet.ShouldProcess($appKey, "Stop capturing dumps of $Executable")) {
            Remove-Item -Path $appKey -Recurse
        }
        if ($RemoveDumps -and (Test-Path $myDumpFolder)) {
            $dumps = @(Get-ChildItem $myDumpFolder -Filter "$Executable*.dmp" -File)
            foreach ($dump in $dumps) {
                if ($PSCmdlet.ShouldProcess($dump.FullName, 'Delete crash dump')) {
                    Remove-Item -LiteralPath $dump.FullName
                }
            }
        }
        Show-Status
        if (-not $RemoveDumps) {
            Write-Host 'Existing dumps were kept. Delete them once analyzed: -Disable -RemoveDumps'
        }
    }
}
