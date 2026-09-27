<#
.SYNOPSIS
    Creates and verifies the least-privilege SatiDemo database principal for the stopped signature portal.

.DESCRIPTION
    Resolves the portal's system-assigned identity from Azure, validates the exact Demo database
    and 119-migration boundary, executes the hash-pinned Grant-SignaturePortal.sql permissions,
    creates the contained external user, and adds only that user to sati_signature_portal.

    Run -PreflightOnly, then -WhatIfOnly, then apply, then rerun for idempotency. The script never
    changes a firewall rule, starts or deploys the portal, configures app settings, or prints tokens.
#>
[CmdletBinding()]
param(
    [switch]$PreflightOnly,
    [switch]$WhatIfOnly,
    [string]$SubscriptionId = '253e5008-51c0-434b-80b9-ae3ac94bd66b',
    [string]$TenantId = '8ce091df-7b0f-40dc-8bf6-ce5dd04f9907',
    [string]$ResourceGroup = 'rg-sati-demo',
    [string]$PortalApp = 'sati-demo-sign-satilogica',
    [string]$SqlServer = 'sati-demo-satilogica-central.database.windows.net',
    [string]$DatabaseName = 'SatiDemo'
)

$ErrorActionPreference = 'Stop'
if ($PreflightOnly -and $WhatIfOnly) { throw 'Choose only one of -PreflightOnly and -WhatIfOnly.' }
if ($PortalApp -notmatch '^[a-z0-9-]{2,60}$') { throw 'Portal app name is not safe for the reviewed SQL principal.' }
if ($DatabaseName -cne 'SatiDemo' -or $SqlServer -cne 'sati-demo-satilogica-central.database.windows.net') {
    throw 'This runner supports only the reviewed Azure SatiDemo database.'
}

$grantPath = Join-Path $PSScriptRoot 'Grant-SignaturePortal.sql'
$expectedGrantHash = 'D61F8A2E8CB8BBDD35C7731793847387D3E045B462FD4ADAA6EB58E1F765C61C'
if (-not (Test-Path -LiteralPath $grantPath -PathType Leaf)) { throw 'The reviewed portal grant script is missing.' }
if ((Get-FileHash -LiteralPath $grantPath -Algorithm SHA256).Hash -cne $expectedGrantHash) {
    throw 'Grant-SignaturePortal.sql changed. Review and repin it before deployment.'
}

$account = az account show --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $account.id -cne $SubscriptionId -or $account.tenantId -cne $TenantId) {
    throw 'Azure CLI is not signed in to the reviewed Demo subscription and tenant.'
}
$portal = az webapp show --resource-group $ResourceGroup --name $PortalApp `
    --query '{principalId:identity.principalId,identityType:identity.type,state:state}' --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($portal.principalId) -or
    $portal.identityType -cne 'SystemAssigned' -or $portal.state -cne 'Stopped') {
    throw 'The reviewed stopped portal host and its system-assigned identity were not found.'
}
$portalObjectId = [Guid]::Parse($portal.principalId)
$portalServicePrincipal = az ad sp show --id $portalObjectId.ToString('D') `
    --query '{objectId:id,clientId:appId,servicePrincipalType:servicePrincipalType}' --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $portalServicePrincipal.objectId -cne $portalObjectId.ToString('D') -or
    $portalServicePrincipal.servicePrincipalType -cne 'ManagedIdentity') {
    throw 'The portal principal does not resolve to the expected managed-identity service principal.'
}
$portalClientId = [Guid]::Parse($portalServicePrincipal.clientId)

$token = az account get-access-token --resource 'https://database.windows.net/' --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
    throw 'Azure SQL access token is unavailable.'
}
$connection = New-Object System.Data.SqlClient.SqlConnection(
    "Server=$SqlServer;Database=$DatabaseName;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;")
$connection.AccessToken = $token.Trim()
$token = $null

function Invoke-Sql {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [System.Data.SqlClient.SqlTransaction]$Transaction,
        [string]$Sql,
        [switch]$Scalar
    )
    $command = $Connection.CreateCommand()
    $command.CommandTimeout = 180
    if ($Transaction) { $command.Transaction = $Transaction }
    $command.CommandText = $Sql
    try {
        if ($Scalar) { return $command.ExecuteScalar() }
        $command.ExecuteNonQuery() | Out-Null
    }
    finally { $command.Dispose() }
}

$portalNameSql = $PortalApp.Replace("'", "''")
$portalClientIdSql = $portalClientId.ToString('D')
$preflightSql = @"
SET NOCOUNT ON;
IF DB_NAME() <> N'SatiDemo'
    THROW 52960, 'Connected database is not SatiDemo.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL OR
   NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 52961, 'Database identity is not Demo.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL OR
   (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 119 OR
   (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC)
       <> N'20260927152031_AddExternalSignatureEvidence'
    THROW 52962, 'SatiDemo is not at the reviewed 119-migration boundary.', 1;
IF OBJECT_ID(N'dbo.SignatureDatabaseEnvironment', N'V') IS NULL OR
   OBJECT_ID(N'dbo.SignatureSourceDocuments', N'V') IS NULL OR
   OBJECT_ID(N'dbo.FrozenSignatureDocuments', N'U') IS NULL OR
   OBJECT_ID(N'dbo.SignatureRequests', N'U') IS NULL OR
   OBJECT_ID(N'dbo.SignatureSessions', N'U') IS NULL OR
   OBJECT_ID(N'dbo.SignatureConsents', N'U') IS NULL OR
   OBJECT_ID(N'dbo.SignatureEvents', N'U') IS NULL OR
   OBJECT_ID(N'dbo.SignatureCompletions', N'U') IS NULL OR
   OBJECT_ID(N'dbo.SignaturePackages', N'U') IS NULL OR
   OBJECT_ID(N'dbo.SignatureOutbox', N'U') IS NULL
    THROW 52963, 'A required signature object is missing.', 1;
IF DATABASE_PRINCIPAL_ID(N'$portalNameSql') IS NOT NULL AND NOT EXISTS (
    SELECT 1 FROM sys.database_principals
    WHERE name = N'$portalNameSql' AND type = N'E'
      AND sid = CONVERT(varbinary(16), CONVERT(uniqueidentifier, '$portalClientIdSql')))
    THROW 52964, 'The portal database principal name is already bound to another identity.', 1;
SELECT
    (CASE WHEN DATABASE_PRINCIPAL_ID(N'sati_signature_portal') IS NULL THEN 0 ELSE 1 END) * 1 +
    (CASE WHEN DATABASE_PRINCIPAL_ID(N'$portalNameSql') IS NULL THEN 0 ELSE 1 END) * 2 +
    (CASE WHEN EXISTS (
        SELECT 1 FROM sys.database_role_members AS rm
        JOIN sys.database_principals AS rolep ON rolep.principal_id = rm.role_principal_id
        JOIN sys.database_principals AS memberp ON memberp.principal_id = rm.member_principal_id
        WHERE rolep.name = N'sati_signature_portal' AND memberp.name = N'$portalNameSql')
      THEN 1 ELSE 0 END) * 4;
"@

$createAndBindSql = @"
IF DATABASE_PRINCIPAL_ID(N'$portalNameSql') IS NULL
BEGIN
    -- Service-principal token matching uses its application/client ID as the
    -- contained-user SID. The Azure object ID was separately verified above.
    DECLARE @PortalClientId uniqueidentifier = CONVERT(uniqueidentifier, '$portalClientIdSql');
    DECLARE @PortalSid nvarchar(34) = CONVERT(nvarchar(34), CONVERT(varbinary(16), @PortalClientId), 1);
    EXEC(N'CREATE USER [$portalNameSql] WITH SID = ' + @PortalSid + N', TYPE = E;');
END;
IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members AS rm
    JOIN sys.database_principals AS rolep ON rolep.principal_id = rm.role_principal_id
    JOIN sys.database_principals AS memberp ON memberp.principal_id = rm.member_principal_id
    WHERE rolep.name = N'sati_signature_portal' AND memberp.name = N'$portalNameSql')
    ALTER ROLE sati_signature_portal ADD MEMBER [$portalNameSql];
"@

$verifySql = @"
SET NOCOUNT ON;
IF NOT EXISTS (
    SELECT 1 FROM sys.database_principals
    WHERE name = N'$portalNameSql' AND type = N'E'
      AND sid = CONVERT(varbinary(16), CONVERT(uniqueidentifier, '$portalClientIdSql')))
    THROW 52965, 'Portal database principal does not match the managed identity.', 1;
IF (SELECT COUNT(*)
    FROM sys.database_role_members AS rm
    JOIN sys.database_principals AS rolep ON rolep.principal_id = rm.role_principal_id
    JOIN sys.database_principals AS memberp ON memberp.principal_id = rm.member_principal_id
    WHERE memberp.name = N'$portalNameSql') <> 1 OR NOT EXISTS (
    SELECT 1 FROM sys.database_role_members AS rm
    JOIN sys.database_principals AS rolep ON rolep.principal_id = rm.role_principal_id
    JOIN sys.database_principals AS memberp ON memberp.principal_id = rm.member_principal_id
    WHERE rolep.name = N'sati_signature_portal' AND memberp.name = N'$portalNameSql')
    THROW 52966, 'Portal principal does not belong only to the reviewed role.', 1;
IF EXISTS (
    SELECT 1 FROM sys.database_permissions
    WHERE grantee_principal_id = DATABASE_PRINCIPAL_ID(N'$portalNameSql')
      AND NOT (class = 0 AND major_id = 0 AND permission_name = N'CONNECT' AND state = N'G'))
    THROW 52967, 'Portal principal has a direct permission other than the required CONNECT grant.', 1;
IF EXISTS (
    SELECT 1 FROM sys.database_permissions
    WHERE grantee_principal_id = DATABASE_PRINCIPAL_ID(N'sati_signature_portal') AND class = 0)
    THROW 52968, 'Portal role has a database-wide permission.', 1;
EXECUTE AS USER = N'$portalNameSql';
IF HAS_PERMS_BY_NAME(N'dbo.SignatureRequests', N'OBJECT', N'SELECT') <> 1 OR
   HAS_PERMS_BY_NAME(N'dbo.SignatureSessions', N'OBJECT', N'INSERT') <> 1 OR
   HAS_PERMS_BY_NAME(N'dbo.People', N'OBJECT', N'SELECT') <> 0 OR
   HAS_PERMS_BY_NAME(N'dbo.Users', N'OBJECT', N'SELECT') <> 0 OR
   HAS_PERMS_BY_NAME(N'dbo.SignatureOutbox', N'OBJECT', N'SELECT') <> 0 OR
   HAS_PERMS_BY_NAME(N'dbo.FrozenSignatureDocuments', N'OBJECT', N'UPDATE') <> 0 OR
   HAS_PERMS_BY_NAME(N'dbo.SignatureSessions', N'OBJECT', N'DELETE') <> 0
BEGIN
    REVERT;
    THROW 52969, 'Portal effective permissions do not match the reviewed boundary.', 1;
END;
REVERT;
"@

try {
    $connection.Open()
    $before = [int](Invoke-Sql -Connection $connection -Sql $preflightSql -Scalar)
    Write-Host "Identity-checked SatiDemo and stopped portal principal passed preflight (state mask $before)."
    if ($PreflightOnly) { return }

    $grantSql = Get-Content -LiteralPath $grantPath -Raw
    $grantSql = $grantSql.Replace('$(ExpectedDatabase)', 'SatiDemo').Replace('$(ExpectedEnvironment)', 'Demo')
    $grantSql = [regex]::Replace($grantSql, '(?im)^BEGIN TRANSACTION;\s*$', '')
    $grantSql = [regex]::Replace($grantSql, '(?im)^COMMIT;\s*$', '')

    $transaction = $connection.BeginTransaction()
    try {
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $grantSql
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $createAndBindSql
        Invoke-Sql -Connection $connection -Transaction $transaction -Sql $verifySql
        if ($WhatIfOnly) {
            $transaction.Rollback()
            $afterRollback = [int](Invoke-Sql -Connection $connection -Sql $preflightSql -Scalar)
            if ($afterRollback -ne $before) { throw 'Rollback rehearsal did not restore the original principal state.' }
            Write-Host 'Rollback rehearsal passed; role, user, membership, and permissions returned to their prior state.'
        }
        else {
            $transaction.Commit()
            Invoke-Sql -Connection $connection -Sql $verifySql
            Write-Host 'Applied and verified the isolated Demo signature-portal database grant.'
        }
    }
    catch {
        try { $transaction.Rollback() } catch { }
        throw
    }
    finally { $transaction.Dispose() }
}
finally { $connection.Dispose() }
