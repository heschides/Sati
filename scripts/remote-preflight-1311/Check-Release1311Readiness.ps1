<#
.SYNOPSIS
    Reports whether a Local SatiProduction database can accept the 1.3.11 annual
    compliance conversion. Makes no changes.

.DESCRIPTION
    Release 1.3.11 converts annual forms to target-effective-date identity the first time
    the client launches. That conversion refuses, and rolls back, when it meets data it
    cannot attribute:

      * an annual form whose consumer has no effective date, because there is no
        anniversary to derive the target from; or
      * a stored deadline that does not match the documented legacy calculator, because
        choosing a target for it would move a real billing boundary.

    This script only counts those rows. It opens no transaction, writes nothing, and
    reports no consumer information: counts, dates and form types only.

    Run it on the machine that holds the database, before installing 1.3.11.
#>
[CmdletBinding()]
param(
    [string]$SqlServer = '(localdb)\MSSQLLocalDB',
    [string]$DatabaseName = 'SatiProduction'
)

$ErrorActionPreference = 'Stop'

"Sati 1.3.11 readiness check"
"Run at      : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
"Server      : $SqlServer"
"Database    : $DatabaseName"
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

IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 52700, 'This does not look like a Sati database: dbo.SatiDatabaseIdentity is missing.', 1;

DECLARE @converted bit =
    CASE WHEN COL_LENGTH(N'dbo.Forms', N'TargetEffectiveDate') IS NOT NULL THEN 1 ELSE 0 END;

-- The families the conversion refuses. Derivation matches the migration: the target is
-- the greatest effective-date anniversary strictly before the stored deadline.
WITH c AS (
    SELECT f.Id, f.PersonId, f.[Type], CAST(f.DueDate AS date) AS DueDate,
           CAST(p.EffectiveDate AS date) AS Eff, p.AgencyId,
           f.CompletedDate, f.OpenedDate,
           DATEDIFF(year, CAST(p.EffectiveDate AS date), CAST(f.DueDate AS date)) AS N
    FROM dbo.Forms AS f
    INNER JOIN dbo.People AS p ON p.Id = f.PersonId
    WHERE p.EffectiveDate IS NOT NULL
), a AS (
    SELECT c.*, DATEADD(year, c.N, c.Eff) AS Anniversary FROM c
), t AS (
    SELECT a.*, CASE WHEN a.Anniversary >= a.DueDate
                     THEN DATEADD(year, a.N - 1, a.Eff) ELSE a.Anniversary END AS Target
    FROM a
), e AS (
    SELECT t.*, DATEADD(year, DATEDIFF(year, t.Eff, t.Target) + 1, t.Eff) AS NextAnniversary,
           s.Q4RDaysBeforeAnniversary AS Q4, s.PcpDaysBeforeAnniversary AS Pcp,
           s.CompAssessmentDaysBeforeAnniversary AS Ca, s.ReclassificationDaysBeforeAnniversary AS Rc,
           s.SafetyPlanDaysBeforeAnniversary AS Sp, s.PrivacyPracticesDaysBeforeAnniversary AS Pp,
           s.ReleaseAgencyDaysBeforeAnniversary AS Ra, s.ReleaseDhhsDaysBeforeAnniversary AS Rd,
           s.ReleaseMedicalDaysBeforeAnniversary AS Rm
    FROM t INNER JOIN dbo.Settings AS s ON s.AgencyId = t.AgencyId
), f2 AS (
    SELECT e.*, CASE e.[Type]
        WHEN N'Q1R' THEN DATEADD(day, 90, e.Target)
        WHEN N'Q2R' THEN DATEADD(day, 180, e.Target)
        WHEN N'Q3R' THEN DATEADD(day, 270, e.Target)
        WHEN N'Q4R' THEN DATEADD(day, -e.Q4, e.NextAnniversary)
        WHEN N'PCP' THEN DATEADD(day, -e.Pcp, e.NextAnniversary)
        WHEN N'ComprehensiveAssessment' THEN DATEADD(day, -e.Ca, e.NextAnniversary)
        WHEN N'Reclassification' THEN DATEADD(day, -e.Rc, e.NextAnniversary)
        WHEN N'SafetyPlan' THEN DATEADD(day, -e.Sp, e.NextAnniversary)
        WHEN N'PrivacyPractices' THEN DATEADD(day, -e.Pp, e.NextAnniversary)
        WHEN N'Release_Agency' THEN DATEADD(day, -e.Ra, e.NextAnniversary)
        WHEN N'Release_DHHS' THEN DATEADD(day, -e.Rd, e.NextAnniversary)
        WHEN N'Release_Medical' THEN DATEADD(day, -e.Rm, e.NextAnniversary)
    END AS Expected
    FROM e
), mismatch AS (
    SELECT * FROM f2
    WHERE ([Type] = N'ComprehensiveAssessment'
            AND DueDate NOT IN (Expected, DATEADD(day, -60, NextAnniversary), DATEADD(day, -120, NextAnniversary)))
       OR ([Type] <> N'ComprehensiveAssessment' AND DueDate <> Expected)
)
SELECT
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentMarker,
    @converted AS AlreadyConverted,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationsApplied,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
        WHERE MigrationId = N'20260914030703_AddGoalProgressToCaseNotes') THEN 1 ELSE 0 END AS bit) AS ReadyForThisRange,
    (SELECT COUNT_BIG(*) FROM dbo.People) AS Consumers,
    (SELECT COUNT_BIG(*) FROM dbo.Forms) AS Forms,
    (SELECT COUNT_BIG(*) FROM dbo.Forms f LEFT JOIN dbo.People p ON p.Id = f.PersonId
      WHERE p.Id IS NULL OR p.EffectiveDate IS NULL) AS FormsWithoutUsableEffectiveDate,
    (SELECT COUNT_BIG(*) FROM mismatch WHERE DATEDIFF(day, Expected, DueDate) = -1) AS DeadlinesOffByOneDay,
    (SELECT COUNT_BIG(*) FROM mismatch WHERE DATEDIFF(day, Expected, DueDate) <> -1) AS DeadlinesUnattributable,
    (SELECT COUNT_BIG(*) FROM mismatch WHERE DATEDIFF(day, Expected, DueDate) <> -1
        AND (CompletedDate IS NOT NULL OR OpenedDate IS NOT NULL)) AS UnattributableWithEvidence,
    (SELECT COUNT(DISTINCT PersonId) FROM mismatch) AS ConsumersAffected,
    (SELECT COUNT_BIG(*) FROM dbo.Notes WHERE ComplianceOverride = 1) AS LegacyOverrideNotes;
'@
    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) { throw 'The readiness check returned no result.' }
    $values = [ordered]@{}
    for ($i = 0; $i -lt $reader.FieldCount; $i++) { $values[$reader.GetName($i)] = $reader.GetValue($i) }
    $reader.Close()

    foreach ($key in $values.Keys) { "{0,-32}: {1}" -f $key, $values[$key] }
    ""

    if ([int64]$values['AlreadyConverted'] -eq 1) {
        'RESULT: this database is already converted. Installing 1.3.11 changes nothing here.'
    }
    elseif ([int64]$values['ReadyForThisRange'] -eq 0) {
        'RESULT: this database is behind the migrations this release builds on. Send this file to Josh.'
    }
    elseif (([int64]$values['FormsWithoutUsableEffectiveDate'] +
             [int64]$values['DeadlinesOffByOneDay'] +
             [int64]$values['DeadlinesUnattributable']) -eq 0) {
        'RESULT: ready. Installing 1.3.11 should convert this database without stopping.'
    }
    else {
        'RESULT: NOT ready. Installing 1.3.11 will stop during startup, take its backup, and change'
        '        nothing. Send this file to Josh before installing.'
    }
}
finally {
    $connection.Dispose()
}
