<#
.SYNOPSIS
    Provisions the private SatiDemo signature-document storage boundary.

.DESCRIPTION
    Preview-only unless -Apply is supplied. Creates one dedicated StorageV2 account,
    enables versioning, creates a private version-level-immutability-capable container,
    defines a no-delete blob writer role, and assigns that role only to the existing
    Demo API managed identity at container scope.

    This script does not select a retention duration, lock an immutability policy,
    deploy the public portal, configure application settings, enable signatures,
    send email, or alter SQL/network firewall rules.
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$SubscriptionId = '253e5008-51c0-434b-80b9-ae3ac94bd66b',
    [string]$ResourceGroup = 'rg-sati-demo',
    [string]$Location = 'centralus',
    [string]$StorageAccount = 'satidemosignatures',
    [string]$Container = 'signature-documents',
    [string]$ApiApp = 'sati-demo-api-satilogica',
    [string]$WriterRoleName = 'Sati Demo Signature Blob Writer'
)

$ErrorActionPreference = 'Stop'

function Invoke-AzureCli {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $output = & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: az $($Arguments -join ' ')"
    }
    return $output
}

function Read-AzureJson {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $text = Invoke-AzureCli ($Arguments + @('--output', 'json')) | Out-String
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return $text | ConvertFrom-Json
}

function Assert-Equal {
    param([string]$Label, $Actual, $Expected)
    if ($Actual -cne $Expected) {
        throw "$Label is '$Actual'; expected '$Expected'. Refusing to repurpose the resource."
    }
}

$account = Read-AzureJson @('account', 'show')
Assert-Equal 'Azure subscription ID' $account.id $SubscriptionId

$group = Read-AzureJson @('group', 'show', '--name', $ResourceGroup)
Assert-Equal 'Resource-group subscription' $group.id.Split('/')[2] $SubscriptionId
Assert-Equal 'Resource-group name' $group.name $ResourceGroup

$api = Read-AzureJson @(
    'webapp', 'show', '--resource-group', $ResourceGroup, '--name', $ApiApp,
    '--query', '{id:id,principalId:identity.principalId,state:state}')
if ([string]::IsNullOrWhiteSpace($api.principalId)) {
    throw "Demo API '$ApiApp' has no system-assigned managed identity."
}
Assert-Equal 'Demo API state' $api.state 'Running'

$storage = Read-AzureJson @(
    'storage', 'account', 'list', '--resource-group', $ResourceGroup,
    '--query', "[?name=='$StorageAccount'] | [0]", '--only-show-errors')

if ($null -eq $storage) {
    $availability = Read-AzureJson @(
        'storage', 'account', 'check-name', '--name', $StorageAccount)
    if ($availability.nameAvailable -ne $true) {
        throw "Storage-account name '$StorageAccount' is unavailable: $($availability.reason) $($availability.message)"
    }
    if (-not $Apply) {
        Write-Host "Would create dedicated storage account '$StorageAccount' in '$ResourceGroup'." -ForegroundColor Yellow
    }
    else {
        Write-Host "Creating dedicated storage account '$StorageAccount'..." -ForegroundColor Yellow
        Invoke-AzureCli @(
            'storage', 'account', 'create',
            '--resource-group', $ResourceGroup,
            '--name', $StorageAccount,
            '--location', $Location,
            '--sku', 'Standard_LRS',
            '--kind', 'StorageV2',
            '--https-only', 'true',
            '--min-tls-version', 'TLS1_2',
            '--allow-blob-public-access', 'false',
            '--allow-shared-key-access', 'false',
            '--public-network-access', 'Enabled',
            '--default-action', 'Allow',
            '--require-infrastructure-encryption', 'true',
            '--only-show-errors', '--output', 'none') | Out-Null
        Invoke-AzureCli @(
            'storage', 'account', 'blob-service-properties', 'update',
            '--resource-group', $ResourceGroup,
            '--account-name', $StorageAccount,
            '--enable-versioning', 'true',
            '--only-show-errors', '--output', 'none') | Out-Null
        $storage = Read-AzureJson @(
            'storage', 'account', 'show', '--resource-group', $ResourceGroup,
            '--name', $StorageAccount, '--only-show-errors')
    }
}

if ($null -ne $storage) {
    Assert-Equal 'Storage resource group' $storage.resourceGroup $ResourceGroup
    Assert-Equal 'Storage location' $storage.primaryLocation.ToLowerInvariant() $Location.ToLowerInvariant()
    Assert-Equal 'Storage kind' $storage.kind 'StorageV2'
    Assert-Equal 'Storage SKU' $storage.sku.name 'Standard_LRS'
    Assert-Equal 'HTTPS-only setting' $storage.enableHttpsTrafficOnly $true
    Assert-Equal 'Minimum TLS version' $storage.minimumTlsVersion 'TLS1_2'
    Assert-Equal 'Blob public-access setting' $storage.allowBlobPublicAccess $false
    Assert-Equal 'Shared-key access setting' $storage.allowSharedKeyAccess $false
    Assert-Equal 'Public network access setting' $storage.publicNetworkAccess 'Enabled'
    Assert-Equal 'Storage firewall default' $storage.networkRuleSet.defaultAction 'Allow'
    Assert-Equal 'Infrastructure encryption' $storage.encryption.requireInfrastructureEncryption $true

    $blobProperties = Read-AzureJson @(
        'storage', 'account', 'blob-service-properties', 'show',
        '--resource-group', $ResourceGroup, '--account-name', $StorageAccount)
    Assert-Equal 'Blob versioning' $blobProperties.isVersioningEnabled $true
}

$containerResourceId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Storage/storageAccounts/$StorageAccount/blobServices/default/containers/$Container"
$containerState = $null
if ($null -ne $storage) {
    $containerState = Read-AzureJson @(
        'storage', 'container-rm', 'list', '--resource-group', $ResourceGroup,
        '--storage-account', $StorageAccount,
        '--query', "[?name=='$Container'] | [0]", '--only-show-errors')
}
if ($null -eq $containerState) {
    if (-not $Apply) {
        Write-Host "Would create private container '$Container' with version-level immutability support." -ForegroundColor Yellow
    }
    else {
        Write-Host "Creating private container '$Container'..." -ForegroundColor Yellow
        Invoke-AzureCli @(
            'storage', 'container-rm', 'create',
            '--resource-group', $ResourceGroup,
            '--storage-account', $StorageAccount,
            '--name', $Container,
            '--public-access', 'off',
            '--enable-vlw', 'true',
            '--only-show-errors', '--output', 'none') | Out-Null
        $containerState = Read-AzureJson @(
            'storage', 'container-rm', 'show', '--resource-group', $ResourceGroup,
            '--storage-account', $StorageAccount, '--name', $Container, '--only-show-errors')
    }
}
if ($null -ne $containerState) {
    if ($null -ne $containerState.publicAccess -and
        $containerState.publicAccess -cne 'None') {
        throw "Container '$Container' permits public access."
    }
    Assert-Equal 'Version-level immutability support' $containerState.immutableStorageWithVersioning.enabled $true
}

$assignableScope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup"
$role = Read-AzureJson @('role', 'definition', 'list', '--name', $WriterRoleName)
if ($role -is [array]) { $role = $role | Select-Object -First 1 }
$expectedActions = @('Microsoft.Storage/storageAccounts/blobServices/containers/read')
$expectedDataActions = @(
    'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read',
    'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write',
    'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/add/action')
if ($null -eq $role) {
    if (-not $Apply) {
        Write-Host "Would create custom no-delete role '$WriterRoleName'." -ForegroundColor Yellow
    }
    else {
        $rolePath = Join-Path ([System.IO.Path]::GetTempPath()) "sati-signature-writer-role-$PID.json"
        try {
            $definition = [ordered]@{
                Name = $WriterRoleName
                IsCustom = $true
                Description = 'Create and read Sati Demo signature blobs without blob deletion permission; Sati enforces conditional write-once paths.'
                Actions = $expectedActions
                NotActions = @()
                DataActions = $expectedDataActions
                NotDataActions = @()
                AssignableScopes = @($assignableScope)
            }
            [System.IO.File]::WriteAllText(
                $rolePath,
                ($definition | ConvertTo-Json -Depth 5),
                [System.Text.UTF8Encoding]::new($false))
            Invoke-AzureCli @(
                'role', 'definition', 'create', '--role-definition', $rolePath,
                '--only-show-errors', '--output', 'none') | Out-Null
        }
        finally {
            if (Test-Path -LiteralPath $rolePath) { Remove-Item -LiteralPath $rolePath -Force }
        }
        $role = Read-AzureJson @('role', 'definition', 'list', '--name', $WriterRoleName)
        if ($role -is [array]) { $role = $role | Select-Object -First 1 }
    }
}
if ($null -ne $role) {
    $actualActions = @($role.permissions[0].actions | Sort-Object)
    $actualDataActions = @($role.permissions[0].dataActions | Sort-Object)
    if (Compare-Object ($expectedActions | Sort-Object) $actualActions) {
        throw "Custom role '$WriterRoleName' has unexpected management actions."
    }
    if (Compare-Object ($expectedDataActions | Sort-Object) $actualDataActions) {
        throw "Custom role '$WriterRoleName' has unexpected data actions."
    }
    if (@($role.permissions[0].notActions).Count -ne 0 -or
        @($role.permissions[0].notDataActions).Count -ne 0 -or
        @($role.assignableScopes).Count -ne 1 -or
        $role.assignableScopes[0] -cne $assignableScope) {
        throw "Custom role '$WriterRoleName' has unexpected exclusions or assignable scopes."
    }
}

$assignment = $null
if ($null -ne $role -and $null -ne $containerState) {
    $assignment = Read-AzureJson @(
        'role', 'assignment', 'list',
        '--assignee-object-id', $api.principalId,
        '--scope', $containerResourceId,
        '--query', "[?roleDefinitionName=='$WriterRoleName']")
    if ($assignment -is [array]) { $assignment = $assignment | Select-Object -First 1 }
}
if ($null -eq $assignment) {
    if (-not $Apply) {
        Write-Host "Would assign '$WriterRoleName' to API identity $($api.principalId) at container scope only." -ForegroundColor Yellow
    }
    else {
        Write-Host 'Assigning the no-delete writer role to the Demo API identity...' -ForegroundColor Yellow
        $assigned = $false
        for ($attempt = 1; $attempt -le 6 -and -not $assigned; $attempt++) {
            & az role assignment create `
                --assignee-object-id $api.principalId `
                --assignee-principal-type ServicePrincipal `
                --role $WriterRoleName `
                --scope $containerResourceId `
                --only-show-errors --output none
            if ($LASTEXITCODE -eq 0) { $assigned = $true; break }
            if ($attempt -lt 6) { Start-Sleep -Seconds 5 }
        }
        if (-not $assigned) { throw 'The container-scoped API role assignment could not be created.' }
    }
}

if (-not $Apply) {
    Write-Host 'Preview complete. Rerun with -Apply to provision the reviewed boundary.' -ForegroundColor Cyan
    return
}

$assignments = @(Read-AzureJson @(
    'role', 'assignment', 'list', '--assignee-object-id', $api.principalId,
    '--scope', $containerResourceId, '--include-inherited'))
$writerAssignments = @($assignments | Where-Object { $_.roleDefinitionName -ceq $WriterRoleName })
if ($writerAssignments.Count -ne 1 -or $writerAssignments[0].scope -cne $containerResourceId) {
    throw 'The API no-delete writer role is not assigned exactly once at the container scope.'
}
$broadBlobRoles = @($assignments | Where-Object {
    $_.roleDefinitionName -in @('Owner', 'Contributor', 'Storage Blob Data Owner', 'Storage Blob Data Contributor')
})
if ($broadBlobRoles.Count -ne 0) {
    throw 'The API identity inherits a broader owner/contributor role than this boundary permits.'
}

[pscustomobject][ordered]@{
    StorageAccount = $StorageAccount
    Container = $Container
    BlobContainerUri = "https://$StorageAccount.blob.core.windows.net/$Container"
    PublicBlobAccess = $false
    SharedKeyAccess = $false
    VersioningEnabled = $true
    VersionLevelImmutabilityCapable = $true
    LockedRetentionPolicy = $false
    ApiPrincipalId = $api.principalId
    ApiRole = $WriterRoleName
    ApiRoleScope = $containerResourceId
} | Format-List

Write-Host 'Provisioning and least-privilege verification passed.' -ForegroundColor Green
Write-Host 'No retention duration was selected and no signature feature was enabled.' -ForegroundColor Yellow
