<#
.SYNOPSIS
    Explains why the compliance-date repair stopped on a duplicate key. Read-only.

.DESCRIPTION
    The repair recomputes annual deadlines from the documented legacy calculator.
    dbo.Forms carries a unique index on (PersonId, Type, DueDate), so if two rows of
    one type for one consumer would end up on the same recomputed date, the whole
    repair fails and rolls back. That is what happened.

    This script works out the same final dates the repair would, finds every place two
    rows would land on one date, and prints them. It also prints the agency settings
    the calculation depends on, because more than one settings row for an agency would
    multiply every row in the calculation and produce exactly this kind of collision.

    It opens no transaction and writes nothing. It reports row ids, form types, dates
    and counts only: no consumer names, notes, or other personal information.
#>
[CmdletBinding()]
param(
    [string]$SqlServer = '(localdb)\MSSQLLocalDB',
    [string]$DatabaseName = 'SatiProduction'
)

$ErrorActionPreference = 'Stop'

"Sati 1.3.11 repair collision report"
"Run at   : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
"Server   : $SqlServer"
"Database : $DatabaseName"
"This report makes no changes."
""

$connection = New-Object System.Data.SqlClient.SqlConnection `
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=30;"
$connection.Open()
try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 300
    $command.CommandText = @'
SET NOCOUNT ON;

-- 1. The settings the whole calculation depends on. More than one row for an agency is
--    itself the bug: every form would be computed once per settings row.
SELECT N'settings' AS Section, s.Id, s.AgencyId,
       (SELECT COUNT(*) FROM dbo.Settings AS s2 WHERE s2.AgencyId = s.AgencyId) AS RowsForThisAgency,
       s.Q4RDaysBeforeAnniversary AS Q4, s.PcpDaysBeforeAnniversary AS Pcp,
       s.CompAssessmentDaysBeforeAnniversary AS CompAssessment,
       s.ReclassificationDaysBeforeAnniversary AS Reclass,
       s.SafetyPlanDaysBeforeAnniversary AS SafetyPlan,
       s.PrivacyPracticesDaysBeforeAnniversary AS Privacy,
       s.ReleaseAgencyDaysBeforeAnniversary AS RelAgency,
       s.ReleaseDhhsDaysBeforeAnniversary AS RelDhhs,
       s.ReleaseMedicalDaysBeforeAnniversary AS RelMedical
FROM dbo.Settings AS s
ORDER BY s.AgencyId, s.Id;

-- 1b. Offsets outside 1..364 days break the assumption that a recomputed deadline still
--     belongs to the year it was derived from, which is how two rows from different
--     years can be recomputed onto one date.
SELECT N'offset-out-of-range' AS Section, s.AgencyId, v.Name AS Setting, v.Days
FROM dbo.Settings AS s
CROSS APPLY (VALUES
    (N'Q4R', s.Q4RDaysBeforeAnniversary), (N'PCP', s.PcpDaysBeforeAnniversary),
    (N'ComprehensiveAssessment', s.CompAssessmentDaysBeforeAnniversary),
    (N'Reclassification', s.ReclassificationDaysBeforeAnniversary),
    (N'SafetyPlan', s.SafetyPlanDaysBeforeAnniversary),
    (N'PrivacyPractices', s.PrivacyPracticesDaysBeforeAnniversary),
    (N'Release_Agency', s.ReleaseAgencyDaysBeforeAnniversary),
    (N'Release_DHHS', s.ReleaseDhhsDaysBeforeAnniversary),
    (N'Release_Medical', s.ReleaseMedicalDaysBeforeAnniversary)) AS v(Name, Days)
WHERE v.Days < 1 OR v.Days > 364;

-- 2. Consumers whose agency has no settings row, or more than one.
SELECT N'agency-settings-mismatch' AS Section, p.AgencyId,
       COUNT(DISTINCT p.Id) AS Consumers,
       (SELECT COUNT(*) FROM dbo.Settings AS s WHERE s.AgencyId = p.AgencyId) AS SettingsRows
FROM dbo.People AS p
GROUP BY p.AgencyId
HAVING (SELECT COUNT(*) FROM dbo.Settings AS s WHERE s.AgencyId = p.AgencyId) <> 1;

-- The repair's own derivation, reproduced exactly.
WITH c AS (
    SELECT f.Id, f.PersonId, f.[Type], CAST(f.DueDate AS date) AS DueDate,
           CAST(p.EffectiveDate AS date) AS Eff, p.AgencyId,
           f.CompletedDate, f.OpenedDate,
           (SELECT COUNT(*) FROM dbo.FormAttestations AS fa WHERE fa.FormId = f.Id) AS Attestations,
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
), ranked AS (
    -- The repair reduces duplicates first, keeping the row that carries evidence.
    SELECT f2.*, ROW_NUMBER() OVER (
        PARTITION BY f2.PersonId, f2.[Type], f2.Target
        ORDER BY CASE WHEN f2.Attestations > 0 THEN 0 ELSE 1 END,
                 CASE WHEN f2.CompletedDate IS NOT NULL THEN 0 ELSE 1 END,
                 CASE WHEN f2.OpenedDate IS NOT NULL THEN 0 ELSE 1 END,
                 f2.Id) AS Rank
    FROM f2
), surviving AS (
    -- Everything still present after the repair's duplicate removal.
    SELECT * FROM ranked WHERE Rank = 1 OR Attestations > 0
), stillDuplicated AS (
    -- Groups the repair cannot reduce, whose deadlines it therefore leaves alone.
    SELECT PersonId, [Type], Target FROM surviving
    GROUP BY PersonId, [Type], Target HAVING COUNT(*) > 1
), final AS (
    -- Where every surviving row would sit once the repair finished.
    SELECT s.*,
           CASE WHEN EXISTS (SELECT 1 FROM stillDuplicated AS sd
                             WHERE sd.PersonId = s.PersonId AND sd.[Type] = s.[Type]
                               AND sd.Target = s.Target)
                THEN s.DueDate
                WHEN (s.[Type] = N'ComprehensiveAssessment'
                       AND s.DueDate NOT IN (s.Expected,
                            DATEADD(day, -60, s.NextAnniversary),
                            DATEADD(day, -120, s.NextAnniversary)))
                     OR (s.[Type] <> N'ComprehensiveAssessment' AND s.DueDate <> s.Expected)
                THEN s.Expected ELSE s.DueDate END AS FinalDate
    FROM surviving AS s
), colliding AS (
    SELECT PersonId, [Type], FinalDate
    FROM final
    GROUP BY PersonId, [Type], FinalDate
    HAVING COUNT(DISTINCT Id) > 1
)
-- 3. Every collision, row by row. This is what stopped the repair.
SELECT N'collision' AS Section, f.PersonId, f.[Type],
       CONVERT(char(10), f.FinalDate, 23) AS WouldLandOn,
       f.Id AS FormId,
       CONVERT(char(10), f.DueDate, 23) AS StoredDueDate,
       CONVERT(char(10), f.Target, 23) AS DerivedTarget,
       CONVERT(char(10), f.Eff, 23) AS ConsumerEffectiveDate,
       CONVERT(char(10), f.NextAnniversary, 23) AS NextAnniversary,
       f.Attestations, f.Rank,
       CASE WHEN f.CompletedDate IS NULL THEN 0 ELSE 1 END AS HasCompletion,
       CASE WHEN f.OpenedDate IS NULL THEN 0 ELSE 1 END AS HasOpening
FROM final AS f
INNER JOIN colliding AS x
        ON x.PersonId = f.PersonId AND x.[Type] = f.[Type] AND x.FinalDate = f.FinalDate
ORDER BY f.PersonId, f.[Type], f.FinalDate, f.Id;
'@
    $reader = $command.ExecuteReader()
    $resultIndex = 0
    do {
        if ($reader.FieldCount -gt 0) {
            $rows = 0
            while ($reader.Read()) {
                if ($rows -eq 0) { "" }
                $rows++
                $parts = @()
                for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                    $value = $reader.GetValue($i)
                    if ($value -is [DBNull]) { $value = '' }
                    $parts += "{0}={1}" -f $reader.GetName($i), $value
                }
                $parts -join '  '
            }
            if ($rows -eq 0) {
                ""
                switch ($resultIndex) {
                    1 { 'offset-out-of-range: none. Every agency offset is a sane number of days.' }
                    2 { 'agency-settings-mismatch: none. Every agency has exactly one settings row.' }
                    3 { 'collision: none found. The repair should not have stopped; send this file to Josh.' }
                    default { '(no rows)' }
                }
            }
        }
        $resultIndex++
    } while ($reader.NextResult())
    $reader.Close()
    ""
    'Report complete. Send this file to Josh. Nothing was changed.'
}
finally {
    $connection.Dispose()
}
