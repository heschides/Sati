<#
.SYNOPSIS
    Configures the stopped, undeployed SatiDemo signature portal without enabling it.

.DESCRIPTION
    Preview-only unless -Apply is supplied. Resolves the current versioned PIN key,
    writes the exact Demo identity/storage/portal settings and a passwordless managed-
    identity Azure SQL connection, and verifies that no outbox or email capability is
    configured. The host must be stopped before and after this operation.

    This script does not deploy code, start the host, enable signatures, configure a
    worker or sender, alter SQL, or change any firewall rule.
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$SubscriptionId = '253e5008-51c0-434b-80b9-ae3ac94bd66b',
    [string]$TenantId = '8ce091df-7b0f-40dc-8bf6-ce5dd04f9907',
    [string]$ResourceGroup = 'rg-sati-demo',
    [string]$PortalApp = 'sati-demo-sign-satilogica',
    [string]$ExpectedPortalPrincipalId = '89b2e00a-f516-4adf-9f66-5ee042cb0bc7',
    [string]$PinVault = 'sati-demo-sign-pin-kv',
    [string]$PinKey = 'signing-pin',
    [string]$StorageAccount = 'satidemosignatures',
    [string]$Container = 'signature-documents',
    [string]$SqlServer = 'sati-demo-satilogica-central.database.windows.net',
    [string]$DatabaseName = 'SatiDemo'
)

$ErrorActionPreference = 'Stop'

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

$account = Read-AzureJson @('account', 'show')
Assert-Equal 'Azure subscription ID' $account.id $SubscriptionId
Assert-Equal 'Azure tenant ID' $account.tenantId $TenantId

$portal = Read-AzureJson @(
    'webapp', 'show', '--resource-group', $ResourceGroup, '--name', $PortalApp,
    '--query', '{name:name,state:state,principalId:identity.principalId,identityType:identity.type,defaultHostName:defaultHostName,httpsOnly:httpsOnly}')
Assert-Equal 'Portal name' $portal.name $PortalApp
Assert-Equal 'Portal state' $portal.state 'Stopped'
Assert-Equal 'Portal identity type' $portal.identityType 'SystemAssigned'
Assert-Equal 'Portal identity' $portal.principalId $ExpectedPortalPrincipalId
Assert-Equal 'Portal HTTPS-only setting' $portal.httpsOnly $true

$pin = Read-AzureJson @(
    'keyvault', 'key', 'show', '--vault-name', $PinVault, '--name', $PinKey,
    '--query', '{id:key.kid,kty:key.kty,operations:key.keyOps,enabled:attributes.enabled}',
    '--only-show-errors')
Assert-Equal 'PIN key type' $pin.kty 'RSA'
Assert-Equal 'PIN key state' $pin.enabled $true
if ($pin.id -notmatch "^https://$([regex]::Escape($PinVault))\.vault\.azure\.net/keys/$([regex]::Escape($PinKey))/[a-fA-F0-9]{32}$") {
    throw 'The PIN key did not return the required versioned URI.'
}
if (@(Compare-Object @('unwrapKey', 'wrapKey') @($pin.operations | Sort-Object)).Count -ne 0) {
    throw 'The PIN key operations differ from wrapKey/unwrapKey.'
}

$portalBaseUri = "https://$($portal.defaultHostName)/"
$blobContainerUri = "https://$StorageAccount.blob.core.windows.net/$Container"
$connectionString = "Server=tcp:$SqlServer,1433;Initial Catalog=$DatabaseName;Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
$expectedSettings = [ordered]@{
    'AllowedHosts' = $portal.defaultHostName
    'Sati__ExpectedEnvironment' = 'Demo'
    'Sati__ExpectedDatabaseName' = $DatabaseName
    'Signatures__Enabled' = 'false'
    'Signatures__WorkersEnabled' = 'false'
    'Signatures__BlobContainerUri' = $blobContainerUri
    'Signatures__PinKeyUri' = $pin.id
    'Signatures__PortalBaseUri' = $portalBaseUri
}

if (-not $Apply) {
    Write-Host "Would configure the stopped portal '$PortalApp' with:" -ForegroundColor Yellow
    $expectedSettings.GetEnumerator() | ForEach-Object { Write-Host "  $($_.Key)=$($_.Value)" }
    Write-Host "  ConnectionStrings__SignaturePortal=<passwordless managed-identity connection to $DatabaseName>"
    Write-Host 'Would verify that outbox and email settings are absent and leave the host stopped.' -ForegroundColor Yellow
    Write-Host 'Preview complete. Rerun with -Apply to configure the reviewed disabled boundary.' -ForegroundColor Cyan
    return
}

$settingArguments = @(
    'webapp', 'config', 'appsettings', 'set', '--resource-group', $ResourceGroup,
    '--name', $PortalApp, '--settings')
foreach ($entry in $expectedSettings.GetEnumerator()) {
    $settingArguments += "$($entry.Key)=$($entry.Value)"
}
$settingArguments += @('--only-show-errors', '--output', 'none')
Invoke-AzureCli $settingArguments | Out-Null

Invoke-AzureCli @(
    'webapp', 'config', 'connection-string', 'set', '--resource-group', $ResourceGroup,
    '--name', $PortalApp, '--connection-string-type', 'Custom',
    '--settings', "SignaturePortal=$connectionString",
    '--only-show-errors', '--output', 'none') | Out-Null

$actualSettings = Read-AzureJson @(
    'webapp', 'config', 'appsettings', 'list', '--resource-group', $ResourceGroup,
    '--name', $PortalApp, '--only-show-errors')
$actualSettings = @($actualSettings | ForEach-Object { $_ })
foreach ($entry in $expectedSettings.GetEnumerator()) {
    $actual = @($actualSettings | Where-Object { $_.name -ceq $entry.Key })
    if ($actual.Count -ne 1 -or $actual[0].value -cne $entry.Value) {
        throw "Portal setting '$($entry.Key)' does not match the reviewed disabled configuration."
    }
}

$forbiddenNames = @(
    'Signatures__OutboxKeyUri', 'Signatures__EmailEndpoint', 'Signatures__EmailSender',
    'Signatures__AllowedTestRecipients')
if (@($actualSettings | Where-Object { $_.name -in $forbiddenNames }).Count -ne 0) {
    throw 'The public portal contains a forbidden outbox or email setting.'
}
$emailEnabled = @($actualSettings | Where-Object { $_.name -ceq 'Signatures__EmailEnabled' })
if ($emailEnabled.Count -ne 0 -and $emailEnabled[0].value -cne 'false') {
    throw 'The public portal unexpectedly enables email.'
}

$connections = Read-AzureJson @(
    'webapp', 'config', 'connection-string', 'list', '--resource-group', $ResourceGroup,
    '--name', $PortalApp, '--only-show-errors')
$connections = @($connections | ForEach-Object { $_ })
$signatureConnection = @($connections | Where-Object { $_.name -ceq 'SignaturePortal' })
if ($signatureConnection.Count -ne 1 -or $signatureConnection[0].type -cne 'Custom' -or
    $signatureConnection[0].value -cne $connectionString) {
    throw 'The portal managed-identity connection does not match the reviewed target.'
}
if ($signatureConnection[0].value -match '(?i)Password\s*=|User\s+ID\s*=|Trusted_Connection\s*=') {
    throw 'The portal connection unexpectedly contains workstation or password credentials.'
}

$portal = Read-AzureJson @(
    'webapp', 'show', '--resource-group', $ResourceGroup, '--name', $PortalApp,
    '--query', '{state:state,defaultHostName:defaultHostName}')
Assert-Equal 'Portal state after configuration' $portal.state 'Stopped'

[pscustomobject][ordered]@{
    PortalApp = $PortalApp
    PortalState = $portal.state
    PortalBaseUri = $portalBaseUri
    BlobContainerUri = $blobContainerUri
    PinKeyUri = $pin.id
    Database = $DatabaseName
    Authentication = 'Active Directory Managed Identity'
    SignaturesEnabled = $false
    WorkersEnabled = $false
    OutboxConfigured = $false
    EmailConfigured = $false
} | Format-List

Write-Host 'Disabled portal configuration passed verification; the host remains stopped.' -ForegroundColor Green
