<#
.SYNOPSIS
    Enables Sati-hosted signatures for the authenticated synthetic Demo agency.

.DESCRIPTION
    Preview-only unless -Apply is supplied. Uses a Windows user-bound encrypted
    agency-Admin credential, reads the agency's full settings contract, changes only
    IsInternalElectronicSignatureEnabled through the authorized API with its revision
    token, and verifies the combined platform/agency availability result.

    This script does not create a signature request or send email.
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$BaseAddress = 'https://sati-demo-api-satilogica.azurewebsites.net/',
    [string]$ExpectedReleaseVersion = '1.3.34',
    [string]$CredentialPath = (Join-Path $env:LOCALAPPDATA 'SatiLogica\Sati\Credentials\demo-agency-admin.xml')
)

$ErrorActionPreference = 'Stop'
$baseUri = [Uri]$BaseAddress
if (-not $baseUri.IsAbsoluteUri -or $baseUri.Scheme -cne 'https' -or
    $baseUri.Host -cne 'sati-demo-api-satilogica.azurewebsites.net') {
    throw 'BaseAddress must be the exact Sati Demo API HTTPS host.'
}
$resolvedCredential = [System.IO.Path]::GetFullPath($CredentialPath)
if (-not (Test-Path -LiteralPath $resolvedCredential -PathType Leaf)) {
    throw "Encrypted Demo agency Admin credential not found: $resolvedCredential"
}

function Get-DemoUri([string]$RelativePath) {
    return [Uri]::new($baseUri, $RelativePath)
}

$version = Invoke-RestMethod -Uri (Get-DemoUri 'health/version') -TimeoutSec 90
if ($version.releaseVersion -cne $ExpectedReleaseVersion) {
    throw "Demo API release '$($version.releaseVersion)' does not match '$ExpectedReleaseVersion'."
}

$credential = Import-Clixml -LiteralPath $resolvedCredential
if ($credential -isnot [PSCredential]) {
    throw 'The encrypted Demo agency Admin credential file does not contain a PSCredential.'
}
$plainPassword = $credential.GetNetworkCredential().Password
try {
    $loginBody = @{
        username = $credential.UserName
        password = $plainPassword
    } | ConvertTo-Json -Compress
    $login = Invoke-RestMethod `
        -Uri (Get-DemoUri 'api/v1/auth/login') `
        -Method Post `
        -ContentType 'application/json' `
        -Body $loginBody `
        -TimeoutSec 90
    if ($login.user.role -cne 'Admin') {
        throw "The credential returned role '$($login.user.role)'; an agency Admin is required."
    }
    $headers = @{ Authorization = "Bearer $($login.accessToken)" }
    $settings = Invoke-RestMethod `
        -Uri (Get-DemoUri 'api/v1/settings') `
        -Headers $headers `
        -TimeoutSec 90
    $before = $settings.isInternalElectronicSignatureEnabled -eq $true

    if (-not $Apply) {
        [pscustomobject][ordered]@{
            Environment = 'Demo/SatiDemo'
            Username = $credential.UserName
            Role = $login.user.role
            CurrentAgencySetting = $before
            WouldEnableAgencySetting = -not $before
            SignatureRequestCreated = $false
            EmailSent = $false
        } | Format-List
        Write-Host 'Preview complete. Rerun with -Apply to opt this synthetic Demo agency in.' -ForegroundColor Cyan
        return
    }

    if (-not $before) {
        $settings.isInternalElectronicSignatureEnabled = $true
        $body = $settings | ConvertTo-Json -Depth 12 -Compress
        $settings = Invoke-RestMethod `
            -Uri (Get-DemoUri 'api/v1/settings') `
            -Headers $headers `
            -Method Put `
            -ContentType 'application/json' `
            -Body $body `
            -TimeoutSec 90
    }
    if ($settings.isInternalElectronicSignatureEnabled -ne $true) {
        throw 'The API did not return the enabled agency signature setting.'
    }

    $availability = Invoke-RestMethod `
        -Uri (Get-DemoUri 'api/v1/signatures/availability') `
        -Headers $headers `
        -TimeoutSec 90
    if ($availability.platformEnabled -ne $true -or
        $availability.agencyEnabled -ne $true -or
        $availability.enabled -ne $true -or
        $availability.storageConfigured -ne $true -or
        $availability.emailMode -cne 'RestrictedTestRecipients') {
        throw 'The combined platform/agency signature availability check did not pass.'
    }

    [pscustomobject][ordered]@{
        Environment = 'Demo/SatiDemo'
        Username = $credential.UserName
        Role = $login.user.role
        AgencySettingEnabled = $settings.isInternalElectronicSignatureEnabled
        PlatformEnabled = $availability.platformEnabled
        CombinedAvailability = $availability.enabled
        StorageConfigured = $availability.storageConfigured
        EmailMode = $availability.emailMode
        SignatureRequestCreated = $false
        EmailSent = $false
    } | Format-List
    Write-Host 'The authenticated synthetic Demo agency is opted in.' -ForegroundColor Green
}
finally {
    $plainPassword = $null
    $loginBody = $null
    $body = $null
    $login = $null
    $headers = $null
}
