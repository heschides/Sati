<#
.SYNOPSIS
    Lists the version and timestamp of every Sati file actually installed. Read-only.

.DESCRIPTION
    Sati.exe reports 1.3.13, the database is clean, and the startup check run directly
    against that database reports NotApplied with nothing present - yet startup still
    refuses. The startup path and that report call the same analyzer over the same
    context, so if their verdicts differ, the code reaching the verdict differs.

    Sati installs as a folder of assemblies. The analyzer is not in Sati.exe; it is in
    Sati.Persistence.dll. An install that replaced the executable but left an older
    Sati.Persistence.dll beside it would report 1.3.13 and still judge with the old
    rules, which is exactly the symptom.

    This lists every Sati assembly in the install folder with its file version and write
    time. Mixed versions or mixed timestamps are the answer.

    Nothing is written, and the database is not opened.
#>
[CmdletBinding()]
param(
    [string]$InstallRoot
)

$ErrorActionPreference = 'Stop'

"Sati installed-files check"
"Run at : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
"This check makes no changes and does not open the database."
""

if (-not $InstallRoot) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Satilogica\Sati'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Sati'),
        (Join-Path $env:LOCALAPPDATA 'Sati'),
        (Join-Path ${env:ProgramFiles} 'Sati'),
        (Join-Path ${env:ProgramFiles(x86)} 'Sati')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ 'Sati.exe')) }
    $InstallRoot = $candidates | Select-Object -First 1
}

if (-not $InstallRoot -or -not (Test-Path -LiteralPath $InstallRoot)) {
    "Could not find the Sati install folder. Run this with -InstallRoot <path>."
    return
}

"INSTALL FOLDER"
"--------------"
"  $InstallRoot"
""

$files = Get-ChildItem -LiteralPath $InstallRoot -File |
    Where-Object { $_.Extension -in '.exe', '.dll', '.json' } |
    Sort-Object Name

"FILES"
"-----"
foreach ($file in $files) {
    $version = ''
    if ($file.Extension -ne '.json') {
        $version = $file.VersionInfo.FileVersion
        if (-not $version) { $version = '(no version)' }
    }
    "  {0,-42} {1,-16} {2}" -f $file.Name, $version, $file.LastWriteTime
}
""

"THE TWO THAT DECIDE THIS"
"------------------------"
foreach ($name in @('Sati.exe', 'Sati.Persistence.dll')) {
    $path = Join-Path $InstallRoot $name
    if (Test-Path -LiteralPath $path) {
        $item = Get-Item -LiteralPath $path
        "  {0}" -f $name
        "    FileVersion : {0}" -f $item.VersionInfo.FileVersion
        "    Product     : {0}" -f $item.VersionInfo.ProductVersion
        "    Written     : {0}" -f $item.LastWriteTime
        "    SHA-256     : {0}" -f (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    }
    else { "  {0}: NOT FOUND" -f $name }
}
""

$connectionFile = Join-Path $InstallRoot 'appsettings.json'
if (Test-Path -LiteralPath $connectionFile) {
    "CONFIGURED PRODUCTION DATABASE"
    "------------------------------"
    # Which database Sati actually opens, so it can be compared with the one the
    # readiness and report tools were pointed at.
    try {
        $settings = Get-Content -Raw -LiteralPath $connectionFile | ConvertFrom-Json
        "  $($settings.ConnectionStrings.SatiProduction)"
    }
    catch { "  appsettings.json could not be read as JSON." }
    ""
}

'Check complete. Send this file to Josh. Nothing was changed.'
