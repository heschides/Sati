<#
.SYNOPSIS
    Provisions the disabled SatiDemo signature-email sending boundary.

.DESCRIPTION
    Preview-only unless -Apply is supplied. Registers Microsoft.Communication when
    necessary, creates a dedicated Email Communication Service with an Azure-managed
    domain, links that domain to a dedicated Azure Communication Services resource,
    disables access-key authentication, defines the minimum managed-identity role
    Microsoft documents for email sending, and assigns that role only to the existing
    Demo API identity at the communication-resource scope.

    This script does not start the signing portal, configure a recipient, enable
    signatures or workers, send email, select document retention, or alter SQL/network
    firewall rules. Azure-managed domains are suitable for controlled testing; a
    reviewed custom domain and monitored bounce/complaint process remain production
    decisions.
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$SubscriptionId = '253e5008-51c0-434b-80b9-ae3ac94bd66b',
    [string]$TenantId = '8ce091df-7b0f-40dc-8bf6-ce5dd04f9907',
    [string]$ResourceGroup = 'rg-sati-demo',
    [string]$ApiApp = 'sati-demo-api-satilogica',
    [string]$PortalApp = 'sati-demo-sign-satilogica',
    [string]$EmailService = 'sati-demo-sign-email-satilogica',
    [string]$CommunicationService = 'sati-demo-sign-comm-satilogica',
    [string]$Domain = 'AzureManagedDomain',
    [string]$DataLocation = 'United States',
    [string]$SenderRoleName = 'Sati Demo Signature Email Sender'
)

$ErrorActionPreference = 'Stop'
$providerNamespace = 'Microsoft.Communication'
$apiVersion = '2025-09-01'

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

function Get-AppSetting {
    param([object[]]$Settings, [string]$Name)
    $flatSettings = @($Settings | ForEach-Object { $_ })
    $matches = @($flatSettings | Where-Object { $_.name -ceq $Name })
    if ($matches.Count -gt 1) { throw "App setting '$Name' occurs more than once." }
    if ($matches.Count -eq 0) { return $null }
    return [string]($matches[0].value)
}

function Test-AppSettingEnabled {
    param([object[]]$Settings, [string]$Name)
    $value = Get-AppSetting $Settings $Name
    return [string]::Equals($value, 'true', [StringComparison]::OrdinalIgnoreCase)
}

function Get-ResourceById {
    param([Parameter(Mandatory)][string]$ResourceId)
    $resources = Read-AzureJson @('resource', 'list', '--resource-group', $ResourceGroup)
    $match = @($resources | Where-Object { $_.id -ieq $ResourceId }) | Select-Object -First 1
    if ($null -eq $match) { return $null }
    return Read-AzureJson @(
        'resource', 'show', '--ids', $ResourceId, '--api-version', $apiVersion)
}

function Set-ArmResource {
    param(
        [Parameter(Mandatory)][string]$ResourceId,
        [Parameter(Mandatory)][hashtable]$Body
    )
    $url = "https://management.azure.com$ResourceId`?api-version=$apiVersion"
    $bodyPath = Join-Path ([System.IO.Path]::GetTempPath()) "sati-communication-resource-$PID-$([Guid]::NewGuid().ToString('N')).json"
    try {
        [System.IO.File]::WriteAllText(
            $bodyPath,
            ($Body | ConvertTo-Json -Depth 8 -Compress),
            [System.Text.UTF8Encoding]::new($false))
        Invoke-AzureCli @(
            'rest', '--method', 'put', '--url', $url,
            '--headers', 'Content-Type=application/json', '--body', "@$bodyPath",
            '--only-show-errors', '--output', 'none') | Out-Null
        Invoke-AzureCli @(
            'resource', 'wait', '--created', '--ids', $ResourceId,
            '--api-version', $apiVersion, '--interval', '5', '--timeout', '300',
            '--only-show-errors', '--output', 'none') | Out-Null
    }
    finally {
        if (Test-Path -LiteralPath $bodyPath) { Remove-Item -LiteralPath $bodyPath -Force }
    }
}

$account = Read-AzureJson @('account', 'show')
Assert-Equal 'Azure subscription ID' $account.id $SubscriptionId
Assert-Equal 'Azure tenant ID' $account.tenantId $TenantId

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

$provider = Read-AzureJson @('provider', 'show', '--namespace', $providerNamespace)
if ($provider.registrationState -cne 'Registered') {
    if (-not $Apply) {
        Write-Host "Would register resource provider '$providerNamespace'." -ForegroundColor Yellow
    }
    else {
        Write-Host "Registering resource provider '$providerNamespace'..." -ForegroundColor Yellow
        Invoke-AzureCli @(
            'provider', 'register', '--namespace', $providerNamespace,
            '--wait', '--only-show-errors', '--output', 'none') | Out-Null
        $provider = Read-AzureJson @('provider', 'show', '--namespace', $providerNamespace)
        Assert-Equal 'Communication provider registration' $provider.registrationState 'Registered'
    }
}

$baseId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/$providerNamespace"
$emailServiceId = "$baseId/emailServices/$EmailService"
$domainId = "$emailServiceId/domains/$Domain"
$communicationServiceId = "$baseId/communicationServices/$CommunicationService"

$emailState = Get-ResourceById $emailServiceId
if ($null -eq $emailState) {
    if (-not $Apply) {
        Write-Host "Would create Email Communication Service '$EmailService'." -ForegroundColor Yellow
    }
    else {
        Write-Host "Creating Email Communication Service '$EmailService'..." -ForegroundColor Yellow
        Set-ArmResource $emailServiceId ([ordered]@{
            location = 'Global'
            properties = [ordered]@{ dataLocation = $DataLocation }
        })
        $emailState = Get-ResourceById $emailServiceId
    }
}
if ($null -ne $emailState) {
    Assert-Equal 'Email-service resource group' $emailState.resourceGroup $ResourceGroup
    Assert-Equal 'Email-service location' $emailState.location.ToLowerInvariant() 'global'
    Assert-Equal 'Email-service data location' $emailState.properties.dataLocation $DataLocation
}

$domainState = Get-ResourceById $domainId
if ($null -eq $domainState) {
    if (-not $Apply) {
        Write-Host "Would create Azure-managed email domain '$Domain' with engagement tracking disabled." -ForegroundColor Yellow
    }
    else {
        Write-Host "Creating Azure-managed email domain '$Domain'..." -ForegroundColor Yellow
        Set-ArmResource $domainId ([ordered]@{
            location = 'Global'
            properties = [ordered]@{
                domainManagement = 'AzureManaged'
                userEngagementTracking = 'Disabled'
            }
        })
        $domainState = Get-ResourceById $domainId
    }
}
if ($null -ne $domainState) {
    Assert-Equal 'Email-domain location' $domainState.location.ToLowerInvariant() 'global'
    Assert-Equal 'Email-domain management' $domainState.properties.domainManagement 'AzureManaged'
    Assert-Equal 'Email engagement tracking' $domainState.properties.userEngagementTracking 'Disabled'
}

$communicationState = Get-ResourceById $communicationServiceId
if ($null -eq $communicationState) {
    if (-not $Apply) {
        Write-Host "Would create keyless Communication Service '$CommunicationService' linked only to '$Domain'." -ForegroundColor Yellow
    }
    else {
        Write-Host "Creating Communication Service '$CommunicationService'..." -ForegroundColor Yellow
        Set-ArmResource $communicationServiceId ([ordered]@{
            location = 'Global'
            properties = [ordered]@{
                dataLocation = $DataLocation
                disableLocalAuth = $true
                linkedDomains = @($domainId)
                publicNetworkAccess = 'Enabled'
            }
        })
        $communicationState = Get-ResourceById $communicationServiceId
    }
}
elseif ($Apply) {
    # A PUT also makes drift correction explicit without ever introducing a key.
    Set-ArmResource $communicationServiceId ([ordered]@{
        location = 'Global'
        properties = [ordered]@{
            dataLocation = $DataLocation
            disableLocalAuth = $true
            linkedDomains = @($domainId)
            publicNetworkAccess = 'Enabled'
        }
    })
    $communicationState = Get-ResourceById $communicationServiceId
}
if ($null -ne $communicationState) {
    Assert-Equal 'Communication-service resource group' $communicationState.resourceGroup $ResourceGroup
    Assert-Equal 'Communication-service location' $communicationState.location.ToLowerInvariant() 'global'
    Assert-Equal 'Communication-service data location' $communicationState.properties.dataLocation $DataLocation
    Assert-Equal 'Communication-service local authentication' $communicationState.properties.disableLocalAuth $true
    Assert-Equal 'Communication-service public network setting' $communicationState.properties.publicNetworkAccess 'Enabled'
    Assert-StringSet 'Linked email domains' @($communicationState.properties.linkedDomains) @($domainId)
}

$assignableScope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup"
$expectedActions = @(
    'Microsoft.Communication/CommunicationServices/Read',
    'Microsoft.Communication/CommunicationServices/Write')
$role = Read-AzureJson @('role', 'definition', 'list', '--name', $SenderRoleName)
if ($role -is [array]) { $role = $role | Select-Object -First 1 }
if ($null -eq $role) {
    if (-not $Apply) {
        Write-Host "Would create minimum documented sender role '$SenderRoleName'." -ForegroundColor Yellow
    }
    else {
        $rolePath = Join-Path ([System.IO.Path]::GetTempPath()) "sati-signature-email-role-$PID.json"
        try {
            $definition = [ordered]@{
                Name = $SenderRoleName
                IsCustom = $true
                Description = 'Allow the Sati Demo API managed identity to send email through its one dedicated Azure Communication Services resource.'
                Actions = $expectedActions
                NotActions = @()
                DataActions = @()
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
        $role = Read-AzureJson @('role', 'definition', 'list', '--name', $SenderRoleName)
        if ($role -is [array]) { $role = $role | Select-Object -First 1 }
    }
}
if ($null -ne $role) {
    Assert-StringSet 'Sender-role actions' @($role.permissions[0].actions) $expectedActions
    if (@($role.permissions[0].notActions).Count -ne 0 -or
        @($role.permissions[0].dataActions).Count -ne 0 -or
        @($role.permissions[0].notDataActions).Count -ne 0 -or
        @($role.assignableScopes).Count -ne 1 -or
        $role.assignableScopes[0] -cne $assignableScope) {
        throw "Custom role '$SenderRoleName' has unexpected permissions or assignable scopes."
    }
}

$assignment = $null
if ($null -ne $role -and $null -ne $communicationState) {
    $assignment = Read-AzureJson @(
        'role', 'assignment', 'list', '--assignee-object-id', $api.principalId,
        '--scope', $communicationServiceId,
        '--query', "[?roleDefinitionName=='$SenderRoleName']")
    if ($assignment -is [array]) { $assignment = $assignment | Select-Object -First 1 }
}
if ($null -eq $assignment) {
    if (-not $Apply) {
        Write-Host "Would assign '$SenderRoleName' to API identity $($api.principalId) at the communication-resource scope only." -ForegroundColor Yellow
    }
    else {
        Write-Host 'Assigning the sender role to the Demo API identity...' -ForegroundColor Yellow
        $assigned = $false
        for ($attempt = 1; $attempt -le 6 -and -not $assigned; $attempt++) {
            & az role assignment create `
                --assignee-object-id $api.principalId `
                --assignee-principal-type ServicePrincipal `
                --role $SenderRoleName `
                --scope $communicationServiceId `
                --only-show-errors --output none
            if ($LASTEXITCODE -eq 0) { $assigned = $true; break }
            if ($attempt -lt 6) { Start-Sleep -Seconds 5 }
        }
        if (-not $assigned) { throw 'The communication-resource-scoped sender role could not be created.' }
    }
}

if (-not $Apply) {
    Write-Host 'Preview complete. Rerun with -Apply to provision the reviewed boundary.' -ForegroundColor Cyan
    return
}

$assignments = @(Read-AzureJson @(
    'role', 'assignment', 'list', '--assignee-object-id', $api.principalId,
    '--scope', $communicationServiceId, '--include-inherited'))
$senderAssignments = @($assignments | Where-Object {
    $_.roleDefinitionName -ceq $SenderRoleName -and $_.scope -ceq $communicationServiceId
})
if ($senderAssignments.Count -ne 1) {
    throw 'The API sender role is not assigned exactly once at the communication-resource scope.'
}
$broadRoles = @($assignments | Where-Object {
    $_.roleDefinitionName -in @('Owner', 'Contributor', 'Communication and Email Service Owner')
})
if ($broadRoles.Count -ne 0) {
    throw 'The API identity inherits a broader communication owner/contributor role than this boundary permits.'
}

$senderDomain = $domainState.properties.mailFromSenderDomain
$senderListUrl = "https://management.azure.com$domainId/senderUsernames?api-version=$apiVersion"
$senderList = Read-AzureJson @('rest', '--method', 'get', '--url', $senderListUrl, '--only-show-errors')
$senders = @($senderList.value)
if ($senders.Count -ne 1 -or $senders[0].name -cne 'donotreply' -or
    $senders[0].properties.username -cne 'DoNotReply') {
    throw 'The Azure-managed domain does not expose the expected single DoNotReply sender.'
}
$senderAddress = "DoNotReply@$senderDomain"

$apiSettings = @(Read-AzureJson @(
    'webapp', 'config', 'appsettings', 'list', '--resource-group', $ResourceGroup,
    '--name', $ApiApp, '--only-show-errors'))
$portal = Read-AzureJson @(
    'webapp', 'show', '--resource-group', $ResourceGroup, '--name', $PortalApp,
    '--query', '{state:state}')
$portalSettings = @(Read-AzureJson @(
    'webapp', 'config', 'appsettings', 'list', '--resource-group', $ResourceGroup,
    '--name', $PortalApp, '--only-show-errors'))
$apiSettings = @($apiSettings | ForEach-Object { $_ })
$portalSettings = @($portalSettings | ForEach-Object { $_ })
$apiSignaturesEnabled = Test-AppSettingEnabled $apiSettings 'Signatures__Enabled'
$apiWorkersEnabled = Test-AppSettingEnabled $apiSettings 'Signatures__WorkersEnabled'
$apiEmailEnabled = Test-AppSettingEnabled $apiSettings 'Signatures__EmailEnabled'
$portalSignaturesEnabled = Test-AppSettingEnabled $portalSettings 'Signatures__Enabled'

[pscustomobject][ordered]@{
    EmailService = $EmailService
    Domain = $Domain
    DomainManagement = $domainState.properties.domainManagement
    CommunicationService = $CommunicationService
    CommunicationEndpoint = $communicationState.properties.hostName
    LocalAuthenticationDisabled = $communicationState.properties.disableLocalAuth
    SenderAddress = $senderAddress
    ApiPrincipalId = $api.principalId
    ApiRole = $SenderRoleName
    ApiRoleScope = $communicationServiceId
    ApiSignaturesEnabled = $apiSignaturesEnabled
    ApiWorkersEnabled = $apiWorkersEnabled
    ApiEmailEnabled = $apiEmailEnabled
    PortalState = $portal.state
    PortalSignaturesEnabled = $portalSignaturesEnabled
    EmailSentByThisScript = $false
} | Format-List

Write-Host 'Signature-email infrastructure and least-privilege verification passed.' -ForegroundColor Green
Write-Host 'This script did not change any signature, portal, worker, recipient, or email setting and did not send mail.' -ForegroundColor Yellow
