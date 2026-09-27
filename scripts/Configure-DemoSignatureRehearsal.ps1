<#
.SYNOPSIS
    Activates the hosted-signature platform for a controlled SatiDemo rehearsal.

.DESCRIPTION
    Preview-only unless -Apply is supplied. Verifies the exact Demo resources,
    configures the public portal and staff API with one exact allowed recipient,
    starts and probes the portal, then enables the API feature, workers and email.

    This does not opt in an agency, create a signature request, send an email,
    change Production, select retention, or alter SQL/network firewall rules.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TestRecipient,
    [switch]$Apply,
    [string]$SubscriptionId = '253e5008-51c0-434b-80b9-ae3ac94bd66b',
    [string]$TenantId = '8ce091df-7b0f-40dc-8bf6-ce5dd04f9907',
    [string]$ResourceGroup = 'rg-sati-demo',
    [string]$ApiApp = 'sati-demo-api-satilogica',
    [string]$PortalApp = 'sati-demo-sign-satilogica',
    [string]$CommunicationService = 'sati-demo-sign-comm-satilogica',
    [string]$EmailService = 'sati-demo-sign-email-satilogica',
    [string]$Domain = 'AzureManagedDomain',
    [string]$StorageAccount = 'satidemosignatures',
    [string]$Container = 'signature-documents',
    [string]$PinVault = 'sati-demo-sign-pin-kv',
    [string]$PinKey = 'signing-pin',
    [string]$OutboxVault = 'sati-demo-sign-out-kv',
    [string]$OutboxKey = 'signature-outbox',
    [string]$SenderRoleName = 'Sati Demo Signature Email Sender',
    [string]$ExpectedReleaseVersion = '1.3.29'
)

$ErrorActionPreference = 'Stop'
$apiVersion = '2025-09-01'

function Invoke-AzureCli {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $output = & az @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Azure CLI failed: az $($Arguments -join ' ')" }
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
    if ($Actual -cne $Expected) { throw "$Label is '$Actual'; expected '$Expected'." }
}

function Get-AppSetting {
    param([object[]]$Settings, [string]$Name)
    $flatSettings = @($Settings | ForEach-Object { $_ })
    $items = @($flatSettings | Where-Object { $_.name -ceq $Name })
    if ($items.Count -gt 1) { throw "App setting '$Name' occurs more than once." }
    if ($items.Count -eq 0) { return $null }
    return [string]($items[0].value)
}

function Assert-AppSetting {
    param([object[]]$Settings, [string]$Name, [string]$Expected)
    $actual = Get-AppSetting $Settings $Name
    Assert-Equal "App setting $Name" $actual $Expected
}

function Set-AppSettings {
    param([string]$App, [System.Collections.IDictionary]$Settings)
    $arguments = @(
        'webapp', 'config', 'appsettings', 'set', '--resource-group', $ResourceGroup,
        '--name', $App, '--settings')
    foreach ($entry in $Settings.GetEnumerator()) {
        $arguments += "$($entry.Key)=$($entry.Value)"
    }
    $arguments += @('--only-show-errors', '--output', 'none')
    Invoke-AzureCli $arguments | Out-Null
}

function Wait-HttpsJson {
    param([Uri]$Uri, [scriptblock]$Accept)
    $last = $null
    for ($attempt = 1; $attempt -le 24; $attempt++) {
        try {
            $value = Invoke-RestMethod -Uri $Uri -TimeoutSec 30
            if (& $Accept $value) { return $value }
            $last = 'The response did not match the required state.'
        }
        catch { $last = $_.Exception.Message }
        if ($attempt -lt 24) { Start-Sleep -Seconds 5 }
    }
    throw "Timed out waiting for '$Uri'. Last result: $last"
}

if ($TestRecipient.Length -gt 254 -or $TestRecipient -match '\s') {
    throw 'TestRecipient is not a valid exact email address.'
}
try { $parsedRecipient = [System.Net.Mail.MailAddress]::new($TestRecipient) }
catch { throw 'TestRecipient is not a valid exact email address.' }
if ($parsedRecipient.Address -cne $TestRecipient) {
    throw 'TestRecipient must be a plain exact email address without a display name.'
}

$account = Read-AzureJson @('account', 'show')
Assert-Equal 'Azure subscription ID' $account.id $SubscriptionId
Assert-Equal 'Azure tenant ID' $account.tenantId $TenantId
$group = Read-AzureJson @('group', 'show', '--name', $ResourceGroup)
Assert-Equal 'Resource-group subscription' $group.id.Split('/')[2] $SubscriptionId
Assert-Equal 'Resource-group name' $group.name $ResourceGroup

$api = Read-AzureJson @(
    'webapp', 'show', '--resource-group', $ResourceGroup, '--name', $ApiApp,
    '--query', '{state:state,principalId:identity.principalId,defaultHostName:defaultHostName}')
Assert-Equal 'Demo API state' $api.state 'Running'
if ([string]::IsNullOrWhiteSpace($api.principalId)) { throw 'Demo API managed identity is absent.' }

$portal = Read-AzureJson @(
    'webapp', 'show', '--resource-group', $ResourceGroup, '--name', $PortalApp,
    '--query', '{state:state,principalId:identity.principalId,defaultHostName:defaultHostName,httpsOnly:httpsOnly}')
if ($portal.state -notin @('Running', 'Stopped')) { throw "Portal is in unexpected state '$($portal.state)'." }
Assert-Equal 'Portal HTTPS-only setting' $portal.httpsOnly $true
if ([string]::IsNullOrWhiteSpace($portal.principalId) -or $portal.principalId -ceq $api.principalId) {
    throw 'Portal must have its own system identity.'
}

$baseId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Communication"
$emailServiceId = "$baseId/emailServices/$EmailService"
$domainId = "$emailServiceId/domains/$Domain"
$communicationServiceId = "$baseId/communicationServices/$CommunicationService"
$communication = Read-AzureJson @(
    'resource', 'show', '--ids', $communicationServiceId, '--api-version', $apiVersion)
Assert-Equal 'Communication local authentication' $communication.properties.disableLocalAuth $true
Assert-Equal 'Communication public network setting' $communication.properties.publicNetworkAccess 'Enabled'
if (@($communication.properties.linkedDomains).Count -ne 1 -or
    $communication.properties.linkedDomains[0] -ine $domainId) {
    throw 'Communication Service is not linked only to the reviewed email domain.'
}
$domainState = Read-AzureJson @(
    'resource', 'show', '--ids', $domainId, '--api-version', $apiVersion)
Assert-Equal 'Email-domain management' $domainState.properties.domainManagement 'AzureManaged'
Assert-Equal 'Email engagement tracking' $domainState.properties.userEngagementTracking 'Disabled'
$senders = Read-AzureJson @(
    'rest', '--method', 'get',
    '--url', "https://management.azure.com$domainId/senderUsernames?api-version=$apiVersion",
    '--only-show-errors')
$senderRows = @($senders.value)
if ($senderRows.Count -ne 1 -or $senderRows[0].properties.username -cne 'DoNotReply') {
    throw 'The expected single Azure-managed DoNotReply sender is absent.'
}
$emailEndpoint = "https://$($communication.properties.hostName)/"
$emailSender = "DoNotReply@$($domainState.properties.mailFromSenderDomain)"

$senderAssignments = @(Read-AzureJson @(
    'role', 'assignment', 'list', '--assignee-object-id', $api.principalId,
    '--scope', $communicationServiceId,
    '--query', "[?roleDefinitionName=='$SenderRoleName']"))
if ($senderAssignments.Count -ne 1 -or $senderAssignments[0].scope -ine $communicationServiceId) {
    throw 'The Demo API sender role is not assigned exactly once at the communication-resource scope.'
}

$pin = Read-AzureJson @(
    'keyvault', 'key', 'show', '--vault-name', $PinVault, '--name', $PinKey,
    '--query', 'key.kid', '--only-show-errors')
$outbox = Read-AzureJson @(
    'keyvault', 'key', 'show', '--vault-name', $OutboxVault, '--name', $OutboxKey,
    '--query', 'key.kid', '--only-show-errors')
if ($pin -notmatch '^https://.+/keys/.+/[a-fA-F0-9]{32}$' -or
    $outbox -notmatch '^https://.+/keys/.+/[a-fA-F0-9]{32}$' -or $pin -ceq $outbox) {
    throw 'Versioned and distinct PIN/outbox key URIs are required.'
}

$portalBaseUri = "https://$($portal.defaultHostName)/"
$blobContainerUri = "https://$StorageAccount.blob.core.windows.net/$Container"
$portalSettings = @(Read-AzureJson @(
    'webapp', 'config', 'appsettings', 'list', '--resource-group', $ResourceGroup,
    '--name', $PortalApp, '--only-show-errors'))
$portalSettings = @($portalSettings | ForEach-Object { $_ })
$portalForbidden = @($portalSettings | Where-Object {
    $_.name -in @('Signatures__OutboxKeyUri', 'Signatures__EmailEndpoint',
        'Signatures__EmailSender', 'Signatures__EmailEnabled') -or
    $_.name -like 'Signatures__AllowedTestRecipients*'
})
if ($portalForbidden.Count -ne 0) { throw 'The public portal contains a forbidden outbox or email setting.' }

$portalExpected = [ordered]@{
    'Signatures__Enabled' = 'true'
    'Signatures__WorkersEnabled' = 'false'
    'Signatures__BlobContainerUri' = $blobContainerUri
    'Signatures__PinKeyUri' = $pin
    'Signatures__PortalBaseUri' = $portalBaseUri
}
$apiExpected = [ordered]@{
    'Signatures__Enabled' = 'true'
    'Signatures__WorkersEnabled' = 'true'
    'Signatures__BlobContainerUri' = $blobContainerUri
    'Signatures__PinKeyUri' = $pin
    'Signatures__OutboxKeyUri' = $outbox
    'Signatures__PortalBaseUri' = $portalBaseUri
    'Signatures__EmailEnabled' = 'true'
    'Signatures__EmailEndpoint' = $emailEndpoint
    'Signatures__EmailSender' = $emailSender
    'Signatures__AllowedTestRecipients__0' = $TestRecipient
}

if (-not $Apply) {
    Write-Host "Would configure and start Demo portal '$PortalApp'." -ForegroundColor Yellow
    Write-Host "Would enable the Demo API platform, workers and ACS email for exactly '$TestRecipient'." -ForegroundColor Yellow
    Write-Host 'Would not opt in an agency, create a request, or send an email.' -ForegroundColor Yellow
    Write-Host 'Preview complete. Rerun with -Apply for the controlled synthetic rehearsal.' -ForegroundColor Cyan
    return
}

Set-AppSettings $PortalApp $portalExpected
if ($portal.state -cne 'Running') {
    Invoke-AzureCli @(
        'webapp', 'start', '--resource-group', $ResourceGroup, '--name', $PortalApp,
        '--only-show-errors', '--output', 'none') | Out-Null
}
$bootstrap = Wait-HttpsJson ([Uri]::new([Uri]$portalBaseUri, 'portal/bootstrap')) {
    param($value) $value.enabled -eq $true -and -not [string]::IsNullOrWhiteSpace($value.csrfToken)
}

Set-AppSettings $ApiApp $apiExpected
$apiBaseUri = "https://$($api.defaultHostName)/"
$version = Wait-HttpsJson ([Uri]::new([Uri]$apiBaseUri, 'health/version')) {
    param($value) $value.releaseVersion -ceq $ExpectedReleaseVersion
}
$ready = Wait-HttpsJson ([Uri]::new([Uri]$apiBaseUri, 'health/ready')) {
    param($value)
    ($value -is [string] -and $value -ceq 'Healthy') -or $value.status -ceq 'Healthy'
}

$portalSettings = @(Read-AzureJson @(
    'webapp', 'config', 'appsettings', 'list', '--resource-group', $ResourceGroup,
    '--name', $PortalApp, '--only-show-errors'))
$apiSettings = @(Read-AzureJson @(
    'webapp', 'config', 'appsettings', 'list', '--resource-group', $ResourceGroup,
    '--name', $ApiApp, '--only-show-errors'))
$portalSettings = @($portalSettings | ForEach-Object { $_ })
$apiSettings = @($apiSettings | ForEach-Object { $_ })
foreach ($entry in $portalExpected.GetEnumerator()) {
    Assert-AppSetting $portalSettings $entry.Key $entry.Value
}
foreach ($entry in $apiExpected.GetEnumerator()) {
    Assert-AppSetting $apiSettings $entry.Key $entry.Value
}
$unexpectedRecipients = @($apiSettings | Where-Object {
    $_.name -like 'Signatures__AllowedTestRecipients__*' -and
    ($_.name -cne 'Signatures__AllowedTestRecipients__0' -or $_.value -cne $TestRecipient)
})
if ($unexpectedRecipients.Count -ne 0) { throw 'The API contains an unexpected signature-email recipient.' }

$portal = Read-AzureJson @(
    'webapp', 'show', '--resource-group', $ResourceGroup, '--name', $PortalApp,
    '--query', '{state:state}')
Assert-Equal 'Portal state after activation' $portal.state 'Running'

[pscustomobject][ordered]@{
    Environment = 'Demo/SatiDemo'
    PortalState = $portal.state
    PortalEnabled = $bootstrap.enabled
    ApiReleaseVersion = $version.releaseVersion
    ApiReadiness = if ($ready -is [string]) { $ready } else { $ready.status }
    WorkersEnabled = $true
    EmailEnabled = $true
    AllowedRecipient = $TestRecipient
    EmailSender = $emailSender
    AgencyOptInChanged = $false
    EmailSent = $false
} | Format-List

Write-Host 'Controlled Demo signature platform activation passed.' -ForegroundColor Green
Write-Host 'Every agency remains opted out until an authorized agency Admin enables its setting.' -ForegroundColor Yellow
