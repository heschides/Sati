[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('PackageOnly', 'ReviewedDeploy', 'WrongTarget', 'WrongRuntime', 'MissingWatchdogSchedule', 'ChangedPackage', 'DirtySource', 'PrivateConfig')]
    [string]$Case,
    [string]$PublisherPath = (Join-Path $PSScriptRoot 'Publish-DemoRefresh.ps1')
)

# Exercise the real publisher with fake git, dotnet, and az commands. No network,
# resource changes, or real compilation occurs; every package belongs to this fixture.
$ErrorActionPreference = 'Stop'
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('SatiDemoRefreshPublish_' + [Guid]::NewGuid().ToString('N'))))
$global:SatiDemoRefreshPublishTestCalls = [Collections.Generic.List[string]]::new()
$global:SatiDemoRefreshPublishTestSubscription = '253e5008-51c0-434b-80b9-ae3ac94bd66b'

function Assert-Test([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Demo publisher assertion failed: $Message" }
}

function git {
    $global:LASTEXITCODE = 0
    if ($args -contains 'rev-parse') { 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
    elseif ($Case -eq 'DirtySource') { ' M synthetic-source.ps1' }
}

function dotnet {
    $arguments = @($args)
    $output = $arguments[[Array]::IndexOf($arguments, '--output') + 1]
    [IO.File]::WriteAllText((Join-Path $output 'SatiComplianceSeed.exe'), 'FAKE_EXECUTABLE')
    $global:LASTEXITCODE = 0
}

function az {
    $arguments = @($args)
    $command = $arguments -join ' '
    $global:SatiDemoRefreshPublishTestCalls.Add($command)
    Assert-Test ($command.Contains("--subscription $global:SatiDemoRefreshPublishTestSubscription")) 'Every Azure call must pin the subscription.'
    $global:LASTEXITCODE = 0
    $resourcePrefix = "/subscriptions/$global:SatiDemoRefreshPublishTestSubscription/resourceGroups/rg-sati-demo/providers"
    if ($command.StartsWith('functionapp show ')) {
        @{ id = "$resourcePrefix/Microsoft.Web/sites/sati-demo-refresh-satilogica"; reserved = $false;
            kind = 'functionapp'; identity = @{ principalId = 'FAKE_IDENTITY' } } | ConvertTo-Json
    }
    elseif ($command.StartsWith('functionapp config show ')) {
        @{ powerShellVersion = '7.6'; use32BitWorkerProcess = ($Case -eq 'WrongRuntime') } | ConvertTo-Json
    }
    elseif ($command.StartsWith('functionapp config appsettings list ')) {
        $items = @(@{ name = 'FUNCTIONS_WORKER_RUNTIME'; value = 'powershell' })
        if ($Case -ne 'MissingWatchdogSchedule') {
            $items += @{ name = 'DemoWatchdogSchedule'; value = '0 0 4 * * *' }
        }
        $items | ConvertTo-Json -AsArray
    }
    elseif ($command.StartsWith('storage account show ')) {
        @{ id = "$resourcePrefix/Microsoft.Storage/storageAccounts/satidemorefreshst" } | ConvertTo-Json
    }
    elseif ($command.StartsWith('functionapp deployment source config-zip ')) {
        @{ id = 'FAKE_DEPLOYMENT'; status = 4; complete = $true } | ConvertTo-Json
    }
    else { throw "Unexpected Azure command: $command" }
}

try {
    foreach ($folder in @('scripts', 'Sati.DemoRefresh\Shared', 'Sati.DemoRefresh\ComplianceSeed')) {
        [void][IO.Directory]::CreateDirectory((Join-Path $temporaryRoot $folder))
    }
    $publisher = Join-Path $temporaryRoot 'scripts\Publish-DemoRefresh.ps1'
    Copy-Item -LiteralPath $PublisherPath -Destination $publisher
    [IO.File]::WriteAllText((Join-Path $temporaryRoot 'scripts\Seed-DemoShowcaseData.ps1'), '# FAKE_SEED')
    [IO.File]::WriteAllText((Join-Path $temporaryRoot 'Sati.DemoRefresh\host.json'), '{"version":"2.0"}')
    [IO.File]::WriteAllText((Join-Path $temporaryRoot 'Sati.DemoRefresh\Shared\DemoReset.ps1'), '# FAKE_RESET')

    if ($Case -in @('WrongTarget', 'PrivateConfig')) {
        $parameters = @{ ExistingAppOnly = $true; PackageOnly = $true }
        if ($Case -eq 'WrongTarget') { $parameters.FunctionApp = 'some-other-app' }
        else { [IO.File]::WriteAllText((Join-Path $temporaryRoot 'Sati.DemoRefresh\local.settings.json'), '{"synthetic":"secret"}') }
        $failed = $false
        try { & $publisher @parameters | Out-Null }
        catch { $failed = $true }
        Assert-Test $failed 'A different target or private configuration must fail.'
        Assert-Test ($global:SatiDemoRefreshPublishTestCalls.Count -eq 0) 'Invalid inputs must fail before Azure is called.'
    }
    else {
        & $publisher -ExistingAppOnly -PackageOnly | Out-Null
        Assert-Test ($global:SatiDemoRefreshPublishTestCalls.Count -eq 0) 'PackageOnly must make no Azure calls.'
        $artifactRoot = Join-Path $temporaryRoot 'artifacts'
        $zip = @(Get-ChildItem -LiteralPath $artifactRoot -Filter '*.zip')
        Assert-Test ($zip.Count -eq 1) 'Exactly one retained ZIP is expected.'
        $zip = $zip[0].FullName
        $manifestPath = [IO.Path]::ChangeExtension($zip, '.json')
        $manifestText = [IO.File]::ReadAllText($manifestPath)
        $manifest = $manifestText | ConvertFrom-Json
        Assert-Test ($manifest.PackageSHA256 -ceq (Get-FileHash -LiteralPath $zip).Hash) 'Manifest hash must match the retained ZIP.'
        Assert-Test ($manifest.SourceCommit -ceq 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa') 'Source commit must be recorded.'
        Assert-Test ($manifest.DeploymentResult.Status -ceq 'NotRequested') 'PackageOnly must remain undeployed.'

        if ($Case -ne 'PackageOnly') {
            if ($Case -eq 'ChangedPackage') { [IO.File]::AppendAllText($zip, 'ALTERED') }
            $failed = $false
            $failureDetail = ''
            try { & $publisher -ExistingAppOnly -PackagePath $zip | Out-Null }
            catch { $failed = $true; $failureDetail = $_.ToString() + ' ' + $_.ScriptStackTrace }
            Assert-Test (Test-Path -LiteralPath $zip) 'An input package must never be deleted.'
            Assert-Test ([IO.File]::ReadAllText($manifestPath) -ceq $manifestText) 'The reviewed manifest must never be overwritten.'
            if ($Case -in @('ChangedPackage', 'DirtySource')) {
                Assert-Test $failed 'Changed package bytes or dirty source must fail.'
                Assert-Test ($global:SatiDemoRefreshPublishTestCalls.Count -eq 0) 'Unreviewed packages must fail before Azure is called.'
            }
            elseif ($Case -in @('WrongRuntime', 'MissingWatchdogSchedule')) {
                Assert-Test $failed 'An unreviewed runtime or missing watchdog schedule must fail.'
                Assert-Test (-not @($global:SatiDemoRefreshPublishTestCalls | Where-Object { $_.Contains('config-zip') }).Count) 'Runtime mismatch must prevent deployment.'
            }
            else {
                Assert-Test (-not $failed) "The reviewed package should deploy through the fake target. $failureDetail"
                Assert-Test ($global:SatiDemoRefreshPublishTestCalls.Count -eq 5) 'Only four target reads and one ZIP deployment are allowed.'
                Assert-Test (-not @($global:SatiDemoRefreshPublishTestCalls | Where-Object { $_ -match ' appsettings set | identity assign | account create | functionapp create ' }).Count) 'Code-only deployment must not configure resources.'
                $report = @(Get-ChildItem -LiteralPath $artifactRoot -Filter '*-deploy-*.json')
                Assert-Test ($report.Count -eq 1) 'A separate deployment report must be retained.'
                $result = Get-Content -LiteralPath $report[0].FullName -Raw | ConvertFrom-Json
                Assert-Test ($result.DeploymentResult.DeploymentId -ceq 'FAKE_DEPLOYMENT') 'Deployment result must be recorded.'
            }
        }
    }
    Write-Output "DEMO_REFRESH_PUBLISH_TEST_PASSED Case=$Case"
}
finally {
    Remove-Variable SatiDemoRefreshPublishTestCalls, SatiDemoRefreshPublishTestSubscription -Scope Global -ErrorAction SilentlyContinue
    $expectedPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\SatiDemoRefreshPublish_'
    if (-not $temporaryRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing fixture cleanup outside its temporary root.'
    }
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
