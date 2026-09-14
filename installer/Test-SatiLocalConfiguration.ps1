function Test-SatiLocalConfiguration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PrivateConfigurationPath,

        [Parameter(Mandatory)]
        [string]$PublicConfigurationPath
    )

    $privatePath = [System.IO.Path]::GetFullPath($PrivateConfigurationPath)
    $publicPath = [System.IO.Path]::GetFullPath($PublicConfigurationPath)
    foreach ($path in @($privatePath, $publicPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "The Local configuration file is missing: $path"
        }
    }

    try {
        $privateSettings = Get-Content -LiteralPath $privatePath -Raw | ConvertFrom-Json
        $publicSettings = Get-Content -LiteralPath $publicPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "The Local configuration is not valid JSON. $($_.Exception.Message)"
    }

    $expectedDatabaseName = [string]$publicSettings.DataEnvironments.Production.ExpectedDatabaseName
    if ($expectedDatabaseName -cne 'SatiProduction') {
        throw "The Local public configuration must require database 'SatiProduction'."
    }

    $connectionString = [string]$privateSettings.ConnectionStrings.SatiProduction
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        throw "The Local private configuration must contain connection string 'SatiProduction'."
    }

    try {
        $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
        # PowerShell's dictionary adapter otherwise treats a direct property
        # assignment as a literal "ConnectionString" key. Call the CLR setter so
        # the builder parses the individual provider-neutral keys.
        $connection.set_ConnectionString($connectionString)
    }
    catch {
        throw "The Local Production connection string is invalid. $($_.Exception.Message)"
    }

    $databaseName = if ($connection.ContainsKey('Initial Catalog')) {
        [string]$connection['Initial Catalog']
    }
    elseif ($connection.ContainsKey('Database')) {
        [string]$connection['Database']
    }
    else {
        ''
    }
    if ($databaseName -cne $expectedDatabaseName) {
        throw "The Local connection targets database '$databaseName', but '$expectedDatabaseName' is required."
    }

    foreach ($credentialKey in @('User ID', 'UID', 'Password', 'PWD')) {
        if ($connection.ContainsKey($credentialKey)) {
            throw "The Local configuration cannot contain SQL credential '$credentialKey'."
        }
    }

    $integratedSecurity = $false
    foreach ($integratedKey in @('Integrated Security', 'Trusted_Connection')) {
        if (-not $connection.ContainsKey($integratedKey)) {
            continue
        }

        $integratedValue = [string]$connection[$integratedKey]
        if ($integratedValue -ieq 'true' -or $integratedValue -ieq 'sspi') {
            $integratedSecurity = $true
        }
    }
    if (-not $integratedSecurity) {
        throw 'The Local Production connection must use Windows integrated security.'
    }

    [pscustomobject]@{
        ExpectedDatabaseName = $expectedDatabaseName
        DatabaseName = $databaseName
        IntegratedSecurity = $true
    }
}
