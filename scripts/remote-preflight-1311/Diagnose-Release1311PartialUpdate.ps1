<#
.SYNOPSIS
    Reports exactly which effects of the 1.3.11 annual compliance update are already
    present in a Local SatiProduction database. Makes no changes.

.DESCRIPTION
    Sati refuses to start when a pending update's effects are partly present, because
    applying it again could fail halfway or double-apply. This script lists each object
    that update creates, whether it exists, and what migration history records, so the
    repair can be decided from facts instead of guesses.

    It writes nothing, opens no transaction, and reports no consumer information.
#>
[CmdletBinding()]
param(
    [string]$SqlServer = '(localdb)\MSSQLLocalDB',
    [string]$DatabaseName = 'SatiProduction'
)

$ErrorActionPreference = 'Stop'

"Sati 1.3.11 partial-update diagnosis"
"Run at   : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
"Database : $DatabaseName on $SqlServer"
"This check makes no changes."
""

$connection = New-Object System.Data.SqlClient.SqlConnection `
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=30;"
$connection.Open()
try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 300
    $command.CommandText = @'
SET NOCOUNT ON;

SELECT 'migration-history' AS Section, MigrationId AS Name, ProductVersion AS Detail
FROM dbo.__EFMigrationsHistory
WHERE MigrationId >= N'20260914'
UNION ALL
SELECT 'migration-count', CAST(COUNT_BIG(*) AS nvarchar(20)), '' FROM dbo.__EFMigrationsHistory
UNION ALL
-- Tables the 2026-09-15 correction creates.
SELECT 'table', t.name,
       CASE WHEN OBJECT_ID(N'dbo.' + t.name, N'U') IS NOT NULL THEN 'present' ELSE 'absent' END
FROM (VALUES
    ('ReleaseObligations'), ('ReleaseObligationAttestations'), ('ReleaseAuthorizationEvents'),
    ('SignatureComplianceProjections'), ('BillingCompliancePolicyVersions'),
    ('BillingComplianceRecoveryDecisions'), ('BillingComplianceRecoveryNotes'),
    ('BillingComplianceRecoveryObligations'), ('BillingCompliancePolicyReviewFlags')
) AS t(name)
UNION ALL
-- Columns it adds.
SELECT 'column', c.tbl + '.' + c.col,
       CASE WHEN COL_LENGTH(N'dbo.' + c.tbl, c.col) IS NOT NULL THEN 'present' ELSE 'absent' END
FROM (VALUES
    ('Forms', 'TargetEffectiveDate'), ('Forms', 'OpenedDate'),
    ('Notes', 'OverrideAttestationConfirmed'), ('Notes', 'OverrideObligationIdsJson'),
    ('Settings', 'AllowPastBillingPolicyEffectiveDates'),
    ('DocumentArtifacts', 'ReleaseObligationId'),
    ('PersonProviders', 'AssignmentKnownOn')
) AS c(tbl, col)
UNION ALL
-- Indexes and constraints it creates or drops.
SELECT 'index', i.name,
       CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = i.name) THEN 'present' ELSE 'absent' END
FROM (VALUES
    ('IX_Forms_PersonId_Type_TargetEffectiveDate'), ('IX_Forms_PersonId_Type_DueDate'),
    ('IX_BillingCompliancePolicyVersions_VersionId'),
    ('IX_BillingComplianceRecoveryNotes_NoteId'),
    ('IX_DocumentArtifacts_OneLivePerCycle')
) AS i(name)
UNION ALL
SELECT 'check-constraint', 'CK_Forms_TargetEffectiveDate_Valid',
       CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Forms_TargetEffectiveDate_Valid')
            THEN 'present' ELSE 'absent' END
ORDER BY Section, Name;
'@
    $reader = $command.ExecuteReader()
    while ($reader.Read()) {
        "{0,-18} {1,-52} {2}" -f $reader.GetValue(0), $reader.GetValue(1), $reader.GetValue(2)
    }
    $reader.Close()
    ""
    "Send this whole file to Josh. Do not reinstall or run an older version first."
}
finally {
    $connection.Dispose()
}
