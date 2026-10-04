[CmdletBinding()]
param(
    [string]$ResourceGroup = 'rg-sati-demo',
    [string]$FunctionApp = 'sati-demo-refresh-satilogica',
    [string]$StorageAccount = 'satidemorefreshst',
    [string]$Location = 'centralus',
    [switch]$ExistingAppOnly,
    [switch]$PackageOnly,
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'Sati.DemoRefresh'
$seed = Join-Path $PSScriptRoot 'Seed-DemoShowcaseData.ps1'
$staging = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) "sati-demo-refresh-$([Guid]::NewGuid().ToString('N'))"))
$zip = "$staging.zip"
$subscription = '253e5008-51c0-434b-80b9-ae3ac94bd66b'
$manifest = $null
$manifestPath = $null
$retainPackage = $false

if (($PackageOnly -or $PackagePath) -and -not $ExistingAppOnly) {
    throw 'PackageOnly and PackagePath require ExistingAppOnly.'
}
if ($PackageOnly -and $PackagePath) { throw 'PackageOnly builds a new package; omit PackagePath.' }
if ($ExistingAppOnly -and ($ResourceGroup -cne 'rg-sati-demo' -or
    $FunctionApp -cne 'sati-demo-refresh-satilogica' -or
    $StorageAccount -cne 'satidemorefreshst' -or $Location -cne 'centralus')) {
    throw 'ExistingAppOnly is restricted to the reviewed Demo Function, resource group, storage account, and location.'
}

function Invoke-AzureCli([string[]]$Arguments) {
    if ($ExistingAppOnly) { $Arguments += @('--subscription', $subscription) }
    $output = & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: az $($Arguments -join ' ')"
    }
    return $output
}

function Assert-ExistingDemoTarget {
    $app = Invoke-AzureCli @('functionapp','show','-g',$ResourceGroup,'-n',$FunctionApp,'-o','json') |
        Out-String | ConvertFrom-Json
    $expectedAppId = "/subscriptions/$subscription/resourceGroups/$ResourceGroup/providers/Microsoft.Web/sites/$FunctionApp"
    if ($app.id -ine $expectedAppId -or $app.reserved -ne $false -or
        @($app.kind -split ',') -notcontains 'functionapp' -or @($app.kind -split ',') -contains 'linux') {
        throw 'ExistingAppOnly requires the exact existing Windows Demo Function.'
    }
    $configuration = Invoke-AzureCli @('functionapp','config','show','-g',$ResourceGroup,'-n',$FunctionApp,'-o','json') |
        Out-String | ConvertFrom-Json
    if ($configuration.powerShellVersion -cne '7.6' -or $configuration.use32BitWorkerProcess -ne $false) {
        throw 'The existing Demo Function must already use PowerShell 7.6 and a 64-bit worker.'
    }
    $settings = Invoke-AzureCli @('functionapp','config','appsettings','list','-g',$ResourceGroup,'-n',$FunctionApp,'-o','json') |
        Out-String | ConvertFrom-Json
    try {
        $workerRuntime = @($settings | Where-Object name -eq 'FUNCTIONS_WORKER_RUNTIME')
        if ($workerRuntime.Count -ne 1 -or $workerRuntime[0].value -cne 'powershell') {
            throw 'The existing Demo Function worker runtime must already be PowerShell.'
        }
        $watchdogSchedule = @($settings | Where-Object name -eq 'DemoWatchdogSchedule')
        if ($watchdogSchedule.Count -ne 1 -or $watchdogSchedule[0].value -cne '0 0 4 * * *') {
            throw 'Set DemoWatchdogSchedule=0 0 4 * * * on the existing Demo Function before publishing this package.'
        }
    }
    finally { $settings = $null }
    $storage = Invoke-AzureCli @('storage','account','show','-g',$ResourceGroup,'-n',$StorageAccount,'-o','json') |
        Out-String | ConvertFrom-Json
    $expectedStorageId = "/subscriptions/$subscription/resourceGroups/$ResourceGroup/providers/Microsoft.Storage/storageAccounts/$StorageAccount"
    if ($storage.id -ine $expectedStorageId) { throw 'The reviewed Demo storage account must already exist.' }
    return $app.identity
}

try {
    if ($ExistingAppOnly) {
        $retainPackage = $true
        $artifactRoot = Join-Path $repo 'artifacts'
        New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
        $artifactStamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
        $artifactSuffix = [Guid]::NewGuid().ToString('N').Substring(0, 8)
        if ($PackagePath) {
            $zip = (Resolve-Path -LiteralPath $PackagePath).Path
            $artifactPrefix = [IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
            if (-not $zip.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'PackagePath must identify a retained package inside this repository artifacts directory.'
            }
            $priorManifestPath = [IO.Path]::ChangeExtension($zip, '.json')
            $manifest = Get-Content -LiteralPath $priorManifestPath -Raw | ConvertFrom-Json
            if ($manifest.Subscription -cne $subscription -or $manifest.ResourceGroup -cne $ResourceGroup -or
                $manifest.FunctionApp -cne $FunctionApp -or $manifest.StorageAccount -cne $StorageAccount -or
                $manifest.SourceCommit -notmatch '^[0-9a-f]{40}$' -or
                $manifest.PackageSHA256 -cne (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -or
                $manifest.PackageBytes -ne (Get-Item -LiteralPath $zip).Length) {
                $manifest = $null
                throw 'The retained package does not match its reviewed Demo manifest.'
            }
            $manifestPath = Join-Path $artifactRoot "$([IO.Path]::GetFileNameWithoutExtension($zip))-deploy-$artifactStamp-$artifactSuffix.json"
            $manifest.DeploymentResult = [pscustomobject]@{ Status = 'NotRequested' }
        }
        else {
            $sourceCommit = (& git -C $repo rev-parse HEAD | Out-String).Trim()
            if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Could not identify the source commit.' }
            $sourceStatus = & git -C $repo status --porcelain
            if ($LASTEXITCODE -ne 0) { throw 'Could not inspect source changes.' }
            $artifactName = "SatiDemoRefresh-$artifactStamp-$($sourceCommit.Substring(0, 12))-$artifactSuffix"
            $zip = Join-Path $artifactRoot "$artifactName.zip"
            $manifestPath = Join-Path $artifactRoot "$artifactName.json"
        }
    }

    if (-not $PackagePath) {
    New-Item -ItemType Directory -Path $staging | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $staging -Recurse
    # One copy beside Shared\DemoReset.ps1, which both the nightly timer and the queued
    # Admin reset run.
    Copy-Item -LiteralPath $seed -Destination (Join-Path $staging 'Shared\Seed-DemoShowcaseData.ps1')

    # Both functions finish the reset with SatiComplianceSeed --demo, which applies Sati's
    # own attestation and release rules. Self-contained, so the PowerShell worker needs no
    # .NET runtime of its own.
    $toolProject = Join-Path $repo 'tools\SatiComplianceSeed\SatiComplianceSeed.csproj'
    $toolOutput = Join-Path $staging 'ComplianceSeed'
    & dotnet publish $toolProject --configuration Release --runtime win-x64 --self-contained true `
        -p:PublishSingleFile=true --output $toolOutput --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Publishing SatiComplianceSeed failed.' }
    Get-ChildItem -LiteralPath $toolOutput -Filter '*.pdb' | Remove-Item -Force
    if (-not (Test-Path -LiteralPath (Join-Path $toolOutput 'SatiComplianceSeed.exe') -PathType Leaf)) {
        throw 'SatiComplianceSeed.exe was not produced.'
    }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -CompressionLevel Optimal
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $packageEntries = $archive.Entries.Count
        foreach ($entry in $archive.Entries) {
            $entryName = $entry.FullName.Replace('\', '/')
            if ($entryName -match '(?i)(^|/)(local\.settings\.json|appsettings[^/]*\.json|\.env(?:\.[^/]*)?|[^/]*\.(?:pfx|p12|pem|key|config))$') {
                throw 'The Demo Function package contains a private configuration or credential file.'
            }
        }
    }
    finally {
        $archive.Dispose()
    }
    $packageBytes = (Get-Item -LiteralPath $zip).Length
    $packageSha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash

    if ($ExistingAppOnly) {
        $manifest = [pscustomobject]@{
            CreatedAtUtc = [DateTime]::UtcNow.ToString('o')
            Subscription = $subscription
            ResourceGroup = $ResourceGroup
            FunctionApp = $FunctionApp
            StorageAccount = $StorageAccount
            SourceCommit = $sourceCommit
            SourceTreeDirty = @($sourceStatus).Count -gt 0
            PackagePath = $zip
            PackageBytes = $packageBytes
            PackageSHA256 = $packageSha256
            PackageEntries = $packageEntries
            DeploymentResult = [pscustomobject]@{ Status = 'NotRequested' }
        }
    }
    }
    else {
        $packageBytes = $manifest.PackageBytes
        $packageSha256 = $manifest.PackageSHA256
        $packageEntries = $manifest.PackageEntries
    }

    if ($PackageOnly) {
        $manifest | Format-List
        return
    }

    if ($ExistingAppOnly) {
        # This branch only reads target configuration and publishes the reviewed ZIP.
        # Provisioning, identity assignment, and settings writes remain in the default path.
        if ($manifest.SourceTreeDirty -ne $false) {
            throw 'Code-only deployment requires a package built from a clean committed source tree.'
        }
        $identity = Assert-ExistingDemoTarget
    }
    else {
    $storageExists = [int](Invoke-AzureCli @('storage','account','list','-g',$ResourceGroup,'--query',"[?name=='$StorageAccount'] | length(@)",'-o','tsv'))
    if ($storageExists -eq 0) {
        Invoke-AzureCli @('storage','account','create','-g',$ResourceGroup,'-n',$StorageAccount,'-l',$Location,'--sku','Standard_LRS','--kind','StorageV2','--https-only','true','--min-tls-version','TLS1_2') | Out-Null
    }
    $appExists = [int](Invoke-AzureCli @('functionapp','list','-g',$ResourceGroup,'--query',"[?name=='$FunctionApp'] | length(@)",'-o','tsv'))
    if ($appExists -eq 0) {
        Invoke-AzureCli @('functionapp','create','-g',$ResourceGroup,'-n',$FunctionApp,'-s',$StorageAccount,
            '--consumption-plan-location',$Location,'--runtime','powershell','--runtime-version','7.6',
            '--functions-version','4','--os-type','Windows') | Out-Null
    }
    $identity = (Invoke-AzureCli @('functionapp','identity','assign','-g',$ResourceGroup,'-n',$FunctionApp,'-o','json') | Out-String | ConvertFrom-Json)
    Invoke-AzureCli @('functionapp','config','appsettings','set','-g',$ResourceGroup,'-n',$FunctionApp,'--settings',
        'FUNCTIONS_WORKER_RUNTIME=powershell','FUNCTIONS_EXTENSION_VERSION=~4',
        'WEBSITE_TIME_ZONE=Eastern Standard Time','DemoRefreshSchedule=0 15 3 * * *',
        'DemoWatchdogSchedule=0 0 4 * * *',
        'SATI_DEMO_SQL_SERVER=sati-demo-satilogica-central.database.windows.net') | Out-Null
    }
    if ($null -ne $manifest) { $manifest.DeploymentResult = [pscustomobject]@{ Status = 'Started' } }
    $deploymentJson = Invoke-AzureCli @('functionapp','deployment','source','config-zip','-g',$ResourceGroup,'-n',$FunctionApp,'--src',$zip,'-o','json') | Out-String
    $deployment = if ([string]::IsNullOrWhiteSpace($deploymentJson)) { $null } else { $deploymentJson | ConvertFrom-Json }
    if ($null -ne $manifest) {
        $manifest.DeploymentResult = [pscustomobject]@{
            Status = 'Returned'
            DeploymentId = $deployment.id
            DeploymentStatus = $deployment.status
            Complete = $deployment.complete
            RecordedAtUtc = [DateTime]::UtcNow.ToString('o')
        }
    }

    [pscustomobject]@{
        FunctionApp = $FunctionApp
        PrincipalId = $identity.principalId
        Schedule = '3:15 AM America/New_York daily'
        SqlGrantRequired = -not $ExistingAppOnly
        PackagePath = $zip
        ManifestPath = $manifestPath
        PackageBytes = $packageBytes
        PackageSHA256 = $packageSha256
        PackageEntries = $packageEntries
        DeploymentId = $deployment.id
    } | Format-List
}
catch {
    if ($null -ne $manifest -and $null -ne $manifestPath) {
        $manifest.DeploymentResult = [pscustomobject]@{
            Status = 'Failed'
            ExceptionType = $_.Exception.GetType().FullName
            RecordedAtUtc = [DateTime]::UtcNow.ToString('o')
        }
    }
    throw
}
finally {
    if ($null -ne $manifest -and $null -ne $manifestPath) {
        # CreateNew refuses to overwrite any earlier package or deployment evidence.
        $manifestStream = [IO.File]::Open($manifestPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        try {
            $manifestBytes = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 5))
            $manifestStream.Write($manifestBytes, 0, $manifestBytes.Length)
        }
        finally { $manifestStream.Dispose() }
    }
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $resolvedStaging = [IO.Path]::GetFullPath($staging)
    if ([IO.Path]::GetDirectoryName($resolvedStaging) -ine $tempRoot -or
        [IO.Path]::GetFileName($resolvedStaging) -cnotmatch '^sati-demo-refresh-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the intended temporary staging directory.'
    }
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    if (-not $retainPackage -and (Test-Path -LiteralPath $zip)) { Remove-Item -LiteralPath $zip -Force }
}
