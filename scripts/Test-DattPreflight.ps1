[CmdletBinding()]
param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

# Source-only checks: no build, network, database, version edits or artifact writes.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$failures = [Collections.Generic.List[string]]::new()
function Read-Source([string]$Path) { [IO.File]::ReadAllText((Join-Path $root $Path)) }
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $failures.Add($Message) } }
function Literal([string]$Source, [string]$Pattern, [string]$Owner) {
    $matches = [regex]::Matches($Source, $Pattern)
    if ($matches.Count -ne 1) { $failures.Add("$Owner must have exactly one identifiable release literal."); return '' }
    return ($matches[0].Groups[1].Value | ConvertFrom-Json)
}
function Script-Default([string]$Path, [string]$Parameter) {
    $tokens=$null; $errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root $Path), [ref]$tokens, [ref]$errors)
    if ($errors.Count) { $failures.Add("$Path does not parse."); return '' }
    $parameterAst=@($ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -ceq $Parameter })
    if ($parameterAst.Count -ne 1 -or $null -eq $parameterAst[0].DefaultValue) { $failures.Add("$Path lacks default $Parameter."); return '' }
    try { return [string]$parameterAst[0].DefaultValue.SafeGetValue() }
    catch { $failures.Add("$Path default $Parameter must be a literal."); return '' }
}

[xml]$client=Read-Source 'Sati.csproj'
[xml]$api=Read-Source 'Sati.Api/Sati.Api.csproj'
$version=[string]@($client.Project.PropertyGroup.Version | Where-Object { $_ })[0]
Check ($version -cmatch '^\d+\.\d+\.\d+$') 'Sati.csproj release Version must be x.y.z.'
foreach ($project in @(@{Name='Sati.csproj';Xml=$client},@{Name='Sati.Api/Sati.Api.csproj';Xml=$api})) {
    foreach ($field in @('Version','AssemblyVersion','FileVersion')) {
        $values=@($project.Xml.Project.PropertyGroup.$field | Where-Object { $_ })
        $expected=if($field -eq 'Version'){$version}else{"$version.0"}
        Check ($values.Count -eq 1 -and [string]$values[0] -ceq $expected) "$($project.Name) $field differs from $expected."
    }
}
foreach ($path in @('installer/Build-DemoInstaller.ps1','installer/Build-LocalInstaller.ps1','installer/Build-LocalDbDiagnostic.ps1')) {
    Check ((Script-Default $path 'Version') -ceq $version) "$path default version differs from $version."
}
foreach ($path in @('scripts/Test-DemoReadiness.ps1','scripts/Test-DemoGlobalAdmin.ps1','scripts/Configure-DemoSignatureRehearsal.ps1','scripts/Enable-DemoAgencySignatureRehearsal.ps1')) {
    Check ((Script-Default $path 'ExpectedReleaseVersion') -ceq $version) "$path readiness version differs from $version."
}
$notes=Read-Source 'Services/ProductReleaseNotes.cs'
$name=Literal $notes 'ReleaseName\s*=\s*("(?:\\.|[^"\\])*")\s*;' 'ProductReleaseNotes.ReleaseName'
$date=Literal $notes 'ReleaseDate\s*=\s*("(?:\\.|[^"\\])*")\s*;' 'ProductReleaseNotes.ReleaseDate'
Check (-not [string]::IsNullOrWhiteSpace($name)) 'Release name is empty.'
try { [void][DateTime]::ParseExact($date, 'MMMM d, yyyy', [Globalization.CultureInfo]::InvariantCulture) }
catch { $failures.Add('Release date must be an English month/day/year date.') }
$desktopTests=Read-Source 'Sati.Tests/StabilizationTests.cs'
Check ((Literal $desktopTests 'Assert\.Equal\(("(?:\\.|[^"\\])*"),\s*version\);' 'Desktop version assertion') -ceq $version) 'Desktop version assertion is stale.'
Check ((Literal $desktopTests 'Assert\.Equal\(("(?:\\.|[^"\\])*"),\s*ProductReleaseNotes\.ReleaseName\);' 'Release-name assertion') -ceq $name) 'Release-name assertion is stale.'
Check ((Literal $desktopTests 'Assert\.Equal\(("(?:\\.|[^"\\])*"),\s*ProductReleaseNotes\.ReleaseDate\);' 'Release-date assertion') -ceq $date) 'Release-date assertion is stale.'
$apiTests=Read-Source 'Sati.Api.Tests/TenantAuthorizationTests.cs'
Check ((Literal $apiTests 'Assert\.Equal\(("(?:\\.|[^"\\])*"),\s*release\["releaseVersion"\]\);' 'API version assertion') -ceq $version) 'API version assertion is stale.'
$migrationIds=@(Get-ChildItem -LiteralPath (Join-Path $root 'Sati.Persistence/Migrations') -Filter '*.cs' -File | ForEach-Object {
    foreach ($match in [regex]::Matches([IO.File]::ReadAllText($_.FullName), '\[Migration\("([^"]+)"\)\]')) { $match.Groups[1].Value }
})
Check ($migrationIds.Count -gt 0 -and @($migrationIds | Select-Object -Unique).Count -eq $migrationIds.Count) 'Migration IDs are empty or duplicated.'
$boundary=Read-Source 'Sati.Tests/PersistenceAssemblyBoundaryTests.cs'
$count=[regex]::Matches($boundary,'Assert\.Equal\((\d+),\s*migrationIds\.Count\);')
Check ($count.Count -eq 1 -and [int]$count[0].Groups[1].Value -eq $migrationIds.Count) 'Migration-count assertion is stale.'
$latest=@($migrationIds | Sort-Object)[-1]
Check ($boundary.Contains('Assert.Contains("' + $latest + '", migrationIds);')) 'Latest migration is missing from the boundary assertion.'
$agenda=Read-Source 'AGENDA.md'
$releaseHeader=[regex]::Match($agenda, '(?m)^## Release (\d+\.\d+\.\d+)\b')
Check ($releaseHeader.Success -and $releaseHeader.Groups[1].Value -ceq $version) 'Current AGENDA release header differs from the source version.'
$examples=[regex]::Matches((Read-Source 'installer/README.md'),'Sati(?:Local|Demo)Setup-(\d+\.\d+\.\d+)\.exe')
Check ($examples.Count -gt 0) 'Installer examples have no versioned filenames.'
foreach ($example in $examples) { Check ($example.Groups[1].Value -ceq $version) 'Installer README example version is stale.' }
if ($failures.Count) { throw ("DATT preflight failed:`n- " + ($failures -join "`n- ")) }
# Documentation ownership and a truthful, version-matched report are release prerequisites.
# A low readiness score is allowed for a Demo/Local release; it does not authorize cloud Production.
& (Join-Path $PSScriptRoot 'Test-DocumentationStructure.ps1') -RepositoryRoot $root | Out-Null
& (Join-Path $PSScriptRoot 'Test-ReleaseReadiness.ps1') -RepositoryRoot $root | Out-Null
[pscustomobject]@{Gate='DattSourceConsistency';Passed=$true;ReleaseVersion=$version;ReleaseName=$name;ReleaseDate=$date;MigrationCount=$migrationIds.Count;LatestMigration=$latest}
