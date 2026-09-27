<#
.SYNOPSIS
    Provisions the isolated SatiDemo signature-key and portal-identity boundary.

.DESCRIPTION
    Preview-only unless -Apply is supplied. Creates a stopped, undeployed .NET 10
    App Service to establish the signature portal's system-assigned identity. Creates
    separate access-policy Key Vaults for the PIN and notification-outbox keys so the
    portal can never unwrap the outbox or existing SSN key. Grants the Demo API
    wrap/unwrap on both signature keys, the portal unwrap-only on the PIN key, and the
    portal read-only access to the dedicated signature blob container.

    This script does not deploy portal code, create an Azure SQL user, configure app
    settings, enable signatures/workers/uploads, configure email, select document
    retention, or alter SQL/network firewall rules.
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$SubscriptionId = '253e5008-51c0-434b-80b9-ae3ac94bd66b',
    [string]$TenantId = '8ce091df-7b0f-40dc-8bf6-ce5dd04f9907',
    [string]$ResourceGroup = 'rg-sati-demo',
    [string]$Location = 'centralus',
    [string]$Plan = 'asp-sati-demo-central-f1',
    [string]$ApiApp = 'sati-demo-api-satilogica',
    [string]$PortalApp = 'sati-demo-sign-satilogica',
    [string]$PinVault = 'sati-demo-sign-pin-kv',
    [string]$PinKey = 'signing-pin',
    [string]$OutboxVault = 'sati-demo-sign-out-kv',
    [string]$OutboxKey = 'signature-outbox',
    [string]$StorageAccount = 'satidemosignatures',
    [string]$Container = 'signature-documents'
)

$ErrorActionPreference = 'Stop'
$operatorKeyPermissions = @(
    'get', 'list', 'create', 'update', 'delete', 'recover', 'backup', 'restore',
    'rotate', 'getrotationpolicy', 'setrotationpolicy')

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

function Assert-StringSet {
    param([string]$Label, [object[]]$Actual, [string[]]$Expected)
    $differences = @(Compare-Object @($Expected | Sort-Object) @($Actual | Sort-Object))
    if ($differences.Count -ne 0) {
        throw "$Label differs from the reviewed permission set. Refusing to broaden or narrow it implicitly."
    }
}

function Get-Vault {
    param([string]$Name)
    $vaults = Read-AzureJson @('keyvault', 'list', '--resource-group', $ResourceGroup)
    return @($vaults | Where-Object { $_.name -ceq $Name }) | Select-Object -First 1
}

function Ensure-Vault {
    param([string]$Name)
    $vault = Get-Vault $Name
    if ($null -eq $vault) {
        $availability = Read-AzureJson @('keyvault', 'check-name', '--name', $Name)
        if ($availability.nameAvailable -ne $true) {
            throw "Key Vault name '$Name' is unavailable: $($availability.reason) $($availability.message)"
        }
        if (-not $Apply) {
            Write-Host "Would create isolated Key Vault '$Name'." -ForegroundColor Yellow
            return $null
        }
        Write-Host "Creating isolated Key Vault '$Name'..." -ForegroundColor Yellow
        Invoke-AzureCli @(
            'keyvault', 'create', '--name', $Name, '--resource-group', $ResourceGroup,
            '--location', $Location, '--enable-rbac-authorization', 'false',
            '--enable-purge-protection', 'true', '--retention-days', '90',
            '--public-network-access', 'Enabled', '--default-action', 'Allow',
            '--only-show-errors', '--output', 'none') | Out-Null
        $vault = Read-AzureJson @('keyvault', 'show', '--name', $Name, '--resource-group', $ResourceGroup)
    }

    Assert-Equal "$Name resource group" $vault.resourceGroup $ResourceGroup
    Assert-Equal "$Name location" $vault.location.ToLowerInvariant() $Location.ToLowerInvariant()
    Assert-Equal "$Name RBAC mode" $vault.properties.enableRbacAuthorization $false
    Assert-Equal "$Name purge protection" $vault.properties.enablePurgeProtection $true
    Assert-Equal "$Name soft delete" $vault.properties.enableSoftDelete $true
    Assert-Equal "$Name soft-delete recovery window" $vault.properties.softDeleteRetentionInDays 90
    Assert-Equal "$Name public network setting" $vault.properties.publicNetworkAccess 'Enabled'
    if ($null -ne $vault.properties.networkAcls) {
        Assert-Equal "$Name network default" $vault.properties.networkAcls.defaultAction 'Allow'
    }
    return $vault
}

function Set-KeyPolicy {
    param([string]$Vault, [string]$ObjectId, [string[]]$Permissions, [string]$Description)
    if (-not $Apply) {
        Write-Host "Would grant $Description on '$Vault': $($Permissions -join ', ')." -ForegroundColor Yellow
        return
    }
    if ($Description -ceq 'operator key lifecycle access') {
        # Access-policy vault creation gives the creator broad secret/certificate/storage
        # permissions. These vaults contain keys only, so replace that automatic policy
        # rather than leaving unrelated data-plane capabilities behind.
        $vaultState = Read-AzureJson @(
            'keyvault', 'show', '--name', $Vault, '--resource-group', $ResourceGroup)
        $operatorPolicy = @($vaultState.properties.accessPolicies | Where-Object {
            $_.objectId -ceq $ObjectId
        }) | Select-Object -First 1
        $hasNonKeyPermissions = $null -ne $operatorPolicy -and (
            @($operatorPolicy.permissions.secrets | Where-Object { $null -ne $_ }).Count -ne 0 -or
            @($operatorPolicy.permissions.certificates | Where-Object { $null -ne $_ }).Count -ne 0 -or
            @($operatorPolicy.permissions.storage | Where-Object { $null -ne $_ }).Count -ne 0)
        if ($hasNonKeyPermissions) {
            Write-Host "Removing Azure's broad automatic creator policy from '$Vault'..." -ForegroundColor Yellow
            Invoke-AzureCli @(
                'keyvault', 'delete-policy', '--name', $Vault, '--resource-group', $ResourceGroup,
                '--object-id', $ObjectId, '--only-show-errors', '--output', 'none') | Out-Null
        }
    }
    $policyArguments = @(
        'keyvault', 'set-policy', '--name', $Vault, '--resource-group', $ResourceGroup,
        '--object-id', $ObjectId, '--key-permissions') + @($Permissions) +
        @('--only-show-errors', '--output', 'none')
    Invoke-AzureCli $policyArguments | Out-Null
}

function Ensure-Key {
    param([string]$Vault, [string]$Name)
    if (-not $Apply) {
        Write-Host "Would create or verify RSA-3072 key '$Name' in '$Vault'." -ForegroundColor Yellow
        return $null
    }
    $keys = Read-AzureJson @('keyvault', 'key', 'list', '--vault-name', $Vault, '--only-show-errors')
    $unexpectedKeys = @($keys | Where-Object { $_.name -cne $Name })
    if ($unexpectedKeys.Count -ne 0) {
        throw "Vault '$Vault' contains a key other than its reviewed single-purpose key '$Name'."
    }
    $key = @($keys | Where-Object { $_.name -ceq $Name }) | Select-Object -First 1
    if ($null -eq $key) {
        Write-Host "Creating RSA-3072 key '$Name' in '$Vault'..." -ForegroundColor Yellow
        $key = Read-AzureJson @(
            'keyvault', 'key', 'create', '--vault-name', $Vault, '--name', $Name,
            '--kty', 'RSA', '--size', '3072', '--ops', 'wrapKey', 'unwrapKey',
            '--only-show-errors')
    }
    else {
        $key = Read-AzureJson @(
            'keyvault', 'key', 'show', '--vault-name', $Vault, '--name', $Name,
            '--only-show-errors')
    }
    Assert-Equal "$Vault/$Name key type" $key.key.kty 'RSA'
    Assert-StringSet "$Vault/$Name key operations" @($key.key.keyOps) @('wrapKey', 'unwrapKey')
    Assert-Equal "$Vault/$Name enabled state" $key.attributes.enabled $true
    $modulus = $key.key.n.Replace('-', '+').Replace('_', '/')
    switch ($modulus.Length % 4) {
        2 { $modulus += '==' }
        3 { $modulus += '=' }
    }
    $modulusBytes = [Convert]::FromBase64String($modulus)
    if ($modulusBytes.Length -ne 384 -or $modulusBytes[0] -lt 128) {
        throw "$Vault/$Name is not an RSA-3072 key."
    }
    if ($key.key.kid -notmatch "^https://$([regex]::Escape($Vault))\.vault\.azure\.net/keys/$([regex]::Escape($Name))/[a-fA-F0-9]{32}$") {
        throw "$Vault/$Name did not return the required versioned key URI."
    }
    return $key
}

function Assert-Policy {
    param([object]$Vault, [string]$ObjectId, [string[]]$Expected, [string]$Description)
    $policies = @($Vault.properties.accessPolicies | Where-Object { $_.objectId -ceq $ObjectId })
    if ($policies.Count -ne 1) { throw "$Description is not represented by exactly one Key Vault policy." }
    Assert-StringSet "$Description key permissions" @($policies[0].permissions.keys) $Expected
    if (@($policies[0].permissions.secrets | Where-Object { $null -ne $_ }).Count -ne 0 -or
        @($policies[0].permissions.certificates | Where-Object { $null -ne $_ }).Count -ne 0 -or
        @($policies[0].permissions.storage | Where-Object { $null -ne $_ }).Count -ne 0) {
        throw "$Description unexpectedly grants non-key permissions."
    }
}

$account = Read-AzureJson @('account', 'show')
Assert-Equal 'Azure subscription ID' $account.id $SubscriptionId
Assert-Equal 'Azure tenant ID' $account.tenantId $TenantId

$group = Read-AzureJson @('group', 'show', '--name', $ResourceGroup)
Assert-Equal 'Resource-group subscription' $group.id.Split('/')[2] $SubscriptionId
Assert-Equal 'Resource-group name' $group.name $ResourceGroup

$operator = Read-AzureJson @('ad', 'signed-in-user', 'show', '--query', '{id:id,userPrincipalName:userPrincipalName}')
if ([string]::IsNullOrWhiteSpace($operator.id)) { throw 'The signed-in Azure operator could not be resolved.' }

$api = Read-AzureJson @(
    'webapp', 'show', '--resource-group', $ResourceGroup, '--name', $ApiApp,
    '--query', '{id:id,principalId:identity.principalId,state:state}')
if ([string]::IsNullOrWhiteSpace($api.principalId)) { throw "Demo API '$ApiApp' has no system identity." }
Assert-Equal 'Demo API state' $api.state 'Running'

$planState = Read-AzureJson @(
    'appservice', 'plan', 'show', '--resource-group', $ResourceGroup, '--name', $Plan,
    '--query', '{id:id,location:location,kind:kind}')
Assert-Equal 'Portal plan location' $planState.location.Replace(' ', '').ToLowerInvariant() $Location.ToLowerInvariant()

$portal = Read-AzureJson @(
    'webapp', 'list', '--resource-group', $ResourceGroup,
    '--query', "[?name=='$PortalApp'] | [0]")
if ($null -eq $portal) {
    if (-not $Apply) {
        Write-Host "Would create stopped, undeployed portal host '$PortalApp' on '$Plan'." -ForegroundColor Yellow
    }
    else {
        Write-Host "Creating portal host '$PortalApp'..." -ForegroundColor Yellow
        Invoke-AzureCli @(
            'webapp', 'create', '--resource-group', $ResourceGroup, '--plan', $Plan,
            '--name', $PortalApp, '--runtime', 'DOTNET:10', '--https-only', 'true',
            '--only-show-errors', '--output', 'none') | Out-Null
        Invoke-AzureCli @(
            'webapp', 'update', '--resource-group', $ResourceGroup, '--name', $PortalApp,
            '--client-affinity-enabled', 'false', '--https-only', 'true',
            '--set', 'publicNetworkAccess=Enabled', '--only-show-errors', '--output', 'none') | Out-Null
        Invoke-AzureCli @(
            'webapp', 'config', 'set', '--resource-group', $ResourceGroup, '--name', $PortalApp,
            '--net-framework-version', 'v10.0', '--min-tls-version', '1.2',
            '--ftps-state', 'Disabled', '--http20-enabled', 'true', '--always-on', 'false',
            '--only-show-errors', '--output', 'none') | Out-Null
        Invoke-AzureCli @(
            'webapp', 'identity', 'assign', '--resource-group', $ResourceGroup,
            '--name', $PortalApp, '--only-show-errors', '--output', 'none') | Out-Null
        Invoke-AzureCli @(
            'webapp', 'stop', '--resource-group', $ResourceGroup, '--name', $PortalApp,
            '--only-show-errors', '--output', 'none') | Out-Null
    }
}

if ($Apply) {
    $portal = Read-AzureJson @(
        'webapp', 'show', '--resource-group', $ResourceGroup, '--name', $PortalApp,
        '--query', '{id:id,name:name,location:location,serverFarmId:serverFarmId,httpsOnly:httpsOnly,publicNetworkAccess:publicNetworkAccess,clientAffinityEnabled:clientAffinityEnabled,principalId:identity.principalId,identityType:identity.type,state:state,defaultHostName:defaultHostName}')
    Assert-Equal 'Portal host name' $portal.name $PortalApp
    Assert-Equal 'Portal host location' $portal.location.Replace(' ', '').ToLowerInvariant() $Location.ToLowerInvariant()
    Assert-Equal 'Portal host plan' $portal.serverFarmId.Split('/')[-1] $Plan
    Assert-Equal 'Portal HTTPS-only setting' $portal.httpsOnly $true
    Assert-Equal 'Portal public network setting' $portal.publicNetworkAccess 'Enabled'
    Assert-Equal 'Portal client affinity' $portal.clientAffinityEnabled $false
    Assert-Equal 'Portal identity type' $portal.identityType 'SystemAssigned'
    Assert-Equal 'Portal host state' $portal.state 'Stopped'
    if ([string]::IsNullOrWhiteSpace($portal.principalId)) { throw 'Portal host identity was not assigned.' }
    if ($portal.principalId -ceq $api.principalId) { throw 'Portal and API identities must be different.' }

    $portalConfig = Read-AzureJson @(
        'webapp', 'config', 'show', '--resource-group', $ResourceGroup, '--name', $PortalApp,
        '--query', '{netFrameworkVersion:netFrameworkVersion,minTlsVersion:minTlsVersion,ftpsState:ftpsState,http20Enabled:http20Enabled,alwaysOn:alwaysOn}')
    Assert-Equal 'Portal .NET runtime' $portalConfig.netFrameworkVersion 'v10.0'
    Assert-Equal 'Portal minimum TLS' $portalConfig.minTlsVersion '1.2'
    Assert-Equal 'Portal FTPS setting' $portalConfig.ftpsState 'Disabled'
    Assert-Equal 'Portal HTTP/2 setting' $portalConfig.http20Enabled $true
    Assert-Equal 'Portal Always On setting' $portalConfig.alwaysOn $false
}

$pinVaultState = Ensure-Vault $PinVault
$outboxVaultState = Ensure-Vault $OutboxVault

Set-KeyPolicy $PinVault $operator.id $operatorKeyPermissions 'operator key lifecycle access'
Set-KeyPolicy $OutboxVault $operator.id $operatorKeyPermissions 'operator key lifecycle access'
Set-KeyPolicy $PinVault $api.principalId @('wrapKey', 'unwrapKey') 'API PIN wrap/unwrap'
Set-KeyPolicy $OutboxVault $api.principalId @('wrapKey', 'unwrapKey') 'API outbox wrap/unwrap'
if ($Apply) {
    Set-KeyPolicy $PinVault $portal.principalId @('unwrapKey') 'portal PIN unwrap-only'
}
else {
    Write-Host "Would grant the new portal identity PIN unwrap-only and no outbox-vault access." -ForegroundColor Yellow
}

$pinKeyState = Ensure-Key $PinVault $PinKey
$outboxKeyState = Ensure-Key $OutboxVault $OutboxKey

$containerScope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Storage/storageAccounts/$StorageAccount/blobServices/default/containers/$Container"
if (-not $Apply) {
    Write-Host "Would assign Storage Blob Data Reader to the portal identity at '$containerScope'." -ForegroundColor Yellow
    Write-Host 'Preview complete. Rerun with -Apply to provision the reviewed boundary.' -ForegroundColor Cyan
    return
}

$reader = Read-AzureJson @(
    'role', 'assignment', 'list', '--assignee-object-id', $portal.principalId,
    '--scope', $containerScope, '--query', "[?roleDefinitionName=='Storage Blob Data Reader']")
if (@($reader).Count -eq 0) {
    Write-Host 'Assigning container-scoped read access to the portal identity...' -ForegroundColor Yellow
    $assigned = $false
    for ($attempt = 1; $attempt -le 6 -and -not $assigned; $attempt++) {
        & az role assignment create `
            --assignee-object-id $portal.principalId `
            --assignee-principal-type ServicePrincipal `
            --role 'Storage Blob Data Reader' `
            --scope $containerScope `
            --only-show-errors --output none
        if ($LASTEXITCODE -eq 0) { $assigned = $true; break }
        if ($attempt -lt 6) { Start-Sleep -Seconds 5 }
    }
    if (-not $assigned) { throw 'Portal container-reader role assignment could not be created.' }
}

$pinVaultState = Read-AzureJson @('keyvault', 'show', '--name', $PinVault, '--resource-group', $ResourceGroup)
$outboxVaultState = Read-AzureJson @('keyvault', 'show', '--name', $OutboxVault, '--resource-group', $ResourceGroup)
Assert-Policy $pinVaultState $operator.id $operatorKeyPermissions 'PIN-vault operator policy'
Assert-Policy $pinVaultState $api.principalId @('wrapKey', 'unwrapKey') 'PIN-vault API policy'
Assert-Policy $pinVaultState $portal.principalId @('unwrapKey') 'PIN-vault portal policy'
Assert-Policy $outboxVaultState $operator.id $operatorKeyPermissions 'outbox-vault operator policy'
Assert-Policy $outboxVaultState $api.principalId @('wrapKey', 'unwrapKey') 'outbox-vault API policy'
if (@($outboxVaultState.properties.accessPolicies | Where-Object { $_.objectId -ceq $portal.principalId }).Count -ne 0) {
    throw 'The portal identity unexpectedly has an outbox-vault access policy.'
}

$portalAssignments = @(Read-AzureJson @(
    'role', 'assignment', 'list', '--assignee-object-id', $portal.principalId,
    '--scope', $containerScope, '--include-inherited'))
$containerReaders = @($portalAssignments | Where-Object {
    $_.roleDefinitionName -ceq 'Storage Blob Data Reader' -and $_.scope -ceq $containerScope
})
if ($containerReaders.Count -ne 1) { throw 'Portal blob-reader access is not assigned exactly once at container scope.' }
$forbiddenStorageRoles = @($portalAssignments | Where-Object {
    $_.roleDefinitionName -in @('Owner', 'Contributor', 'Storage Blob Data Owner', 'Storage Blob Data Contributor')
})
if ($forbiddenStorageRoles.Count -ne 0) { throw 'Portal identity inherits a forbidden storage owner/writer role.' }

$portalSettings = @(Read-AzureJson @(
    'webapp', 'config', 'appsettings', 'list', '--resource-group', $ResourceGroup, '--name', $PortalApp))
$signatureSettings = @($portalSettings | Where-Object { $_.name -like 'Signatures:*' })
if ($signatureSettings.Count -ne 0) { throw 'The stopped portal host unexpectedly contains signature application settings.' }

[pscustomobject][ordered]@{
    PortalApp = $PortalApp
    PortalState = $portal.state
    PortalHostName = $portal.defaultHostName
    PortalPrincipalId = $portal.principalId
    PortalBlobRole = 'Storage Blob Data Reader'
    PortalBlobScope = $containerScope
    PinKeyUri = $pinKeyState.key.kid
    PinApiPermissions = 'wrapKey, unwrapKey'
    PinPortalPermissions = 'unwrapKey'
    OutboxKeyUri = $outboxKeyState.key.kid
    OutboxApiPermissions = 'wrapKey, unwrapKey'
    OutboxPortalPermissions = 'none'
    KeySoftDeleteDays = 90
    KeyPurgeProtection = $true
    SignatureSettingsConfigured = $false
} | Format-List

Write-Host 'Signature keys and stopped portal identity passed least-privilege verification.' -ForegroundColor Green
Write-Host 'No portal code, SQL grant, app setting, signature feature, worker, upload, or email path was enabled.' -ForegroundColor Yellow
