$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('SatiDattPreflight_' + [Guid]::NewGuid().ToString('N'))
try {
    $owners=@('.gitattributes','Sati.csproj','Sati.Api/Sati.Api.csproj','Services/ProductReleaseNotes.cs',
        'Sati.Tests/StabilizationTests.cs','Sati.Api.Tests/TenantAuthorizationTests.cs',
        'installer/Build-DemoInstaller.ps1','installer/Build-LocalInstaller.ps1','installer/Build-LocalDbDiagnostic.ps1',
        'scripts/Test-DemoReadiness.ps1','scripts/Test-DemoGlobalAdmin.ps1','scripts/Configure-DemoSignatureRehearsal.ps1',
        'scripts/Enable-DemoAgencySignatureRehearsal.ps1','AGENDA.md','installer/README.md')
    # The real preflight now includes documentation/readiness gates; fixtures retain their owners.
    $owners += @(Get-ChildItem -LiteralPath $repo -Filter '*.md' -File | ForEach-Object Name)
    foreach ($folder in @('docs','archive','karuna','Sati.Portal')) {
        $folderPath = Join-Path $repo $folder
        foreach ($file in Get-ChildItem -LiteralPath $folderPath -Recurse -File | Where-Object {
            $_.Extension -eq '.md' -or ($folder -eq 'docs' -and $_.Extension -in @('.json','.txt'))
        }) { $owners += [IO.Path]::GetRelativePath($repo, $file.FullName) }
    }
    # Evidence references validate public source paths, not private settings or real database contents.
    $ledger = [IO.File]::ReadAllText((Join-Path $repo 'docs/readiness/readiness.json')) | ConvertFrom-Json
    $references = @($ledger.rubrics | ForEach-Object criteria | ForEach-Object references) +
        @($ledger.snapshots | ForEach-Object assessments | ForEach-Object evidence | ForEach-Object reference)
    foreach ($reference in $references) {
        if ($reference -notmatch '^https://') {
            $relative = ($reference -split '#', 2)[0] -replace ':\d+(?:[-,]\d+)*$', ''
            $owners += $relative
        }
    }
    $owners = @($owners | Select-Object -Unique)
    foreach ($owner in $owners) {
        $to=Join-Path $fixture $owner
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to))
        Copy-Item -LiteralPath (Join-Path $repo $owner) -Destination $to
    }
    $migrationRoot=Join-Path $fixture 'Sati.Persistence/Migrations'
    [void][IO.Directory]::CreateDirectory($migrationRoot)
    [IO.File]::WriteAllText((Join-Path $migrationRoot 'One.cs'), '[Migration("20260101000000_One")]')
    [IO.File]::WriteAllText((Join-Path $migrationRoot 'Two.cs'), '[Migration("20260102000000_Two")]')
    $boundary=Join-Path $fixture 'Sati.Tests/PersistenceAssemblyBoundaryTests.cs'
    [IO.File]::WriteAllText($boundary, 'Assert.Equal(2, migrationIds.Count); Assert.Contains("20260102000000_Two", migrationIds);')
    $checker=Join-Path $PSScriptRoot 'Test-DattPreflight.ps1'
    $baseline = & $checker -RepositoryRoot $fixture
    $version = $baseline.ReleaseVersion
    $name = $baseline.ReleaseName | ConvertTo-Json -Compress
    $staleVersion = if ($version -ceq '0.0.0') { '0.0.1' } else { '0.0.0' }
    $cases=@(
        @{Name='BuilderVersion';File='installer/Build-DemoInstaller.ps1';Old="'$version'";New="'$staleVersion'"},
        @{Name='ReadinessVersion';File='scripts/Test-DemoReadiness.ps1';Old="'$version'";New="'$staleVersion'"},
        @{Name='AssemblyVersion';File='Sati.Api/Sati.Api.csproj';Old="<AssemblyVersion>$version.0</AssemblyVersion>";New="<AssemblyVersion>$staleVersion.0</AssemblyVersion>"},
        @{Name='StaleVersionAssertion';File='Sati.Api.Tests/TenantAuthorizationTests.cs';Old=('Assert.Equal("' + $version + '", release["releaseVersion"]);');New=('Assert.Equal("' + $staleVersion + '", release["releaseVersion"]);')},
        @{Name='StaleReleaseName';File='Sati.Tests/StabilizationTests.cs';Old=('Assert.Equal(' + $name + ', ProductReleaseNotes.ReleaseName);');New='Assert.Equal("Stale release", ProductReleaseNotes.ReleaseName);'},
        @{Name='MigrationCount';File='Sati.Tests/PersistenceAssemblyBoundaryTests.cs';Old='Assert.Equal(2, migrationIds.Count);';New='Assert.Equal(1, migrationIds.Count);'},
        @{Name='MissingLatestMigration';File='Sati.Tests/PersistenceAssemblyBoundaryTests.cs';Old='Assert.Contains("20260102000000_Two", migrationIds);';New=''},
        @{Name='InstallerExample';File='installer/README.md';Old="Setup-$version.exe";New="Setup-$staleVersion.exe"}
    )
    foreach($case in $cases) {
        $path=Join-Path $fixture $case.File
        $original=[IO.File]::ReadAllText($path)
        if(-not $original.Contains($case.Old)) { throw "Fixture mutation did not apply: $($case.Name)" }
        try {
            [IO.File]::WriteAllText($path,$original.Replace($case.Old,$case.New))
            $failed=$false
            try { & $checker -RepositoryRoot $fixture | Out-Null } catch { $failed=$true }
            if(-not $failed) { throw "Stale release metadata was accepted: $($case.Name)" }
            Write-Output "DATT_PREFLIGHT_CHECK_TEST_PASSED Case=$($case.Name)"
        } finally { [IO.File]::WriteAllText($path,$original) }
    }
    & $checker -RepositoryRoot $fixture | Out-Null
    Write-Output 'DATT_PREFLIGHT_CHECK_TEST_PASSED Case=ConsistentSource'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if([IO.Path]::GetDirectoryName($resolved) -ine [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') -or [IO.Path]::GetFileName($resolved) -cnotmatch '^SatiDattPreflight_[0-9a-f]{32}$') { throw 'Unexpected preflight fixture path.' }
    if(Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
