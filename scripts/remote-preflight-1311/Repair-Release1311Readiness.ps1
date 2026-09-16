<#
.SYNOPSIS
    Clears everything that stops the 1.3.11 conversion on a Local SatiProduction
    database: duplicate obligations, unattributable annual deadlines, legacy blanket
    note overrides, and annual rows with no same-target quarterly review.

.DESCRIPTION
    This is a compliance-scheduling repair for a pre-production database whose
    authoritative records live elsewhere. It deliberately changes compliance dates,
    which the conversion refuses to guess at on its own:

      * The agency quarterly-review offset is moved off the old hard-coded one day,
        because the conversion treats that value as evidence of the pre-June-2026 shape.
      * Two stored rows that resolve to one annual obligation are reduced to one. The
        row carrying evidence is kept and its twin is deleted. A row with attestations
        is never deleted; a pair that both carry attestations is left alone and
        reported, because choosing between two attested records is not a script's call.
      * Annual deadlines that do not match the documented legacy calculator are
        recomputed from that calculator. Completion, opening, and attestation evidence
        is not read or written; only Forms.DueDate moves.
      * Legacy blanket note overrides are cleared. Such an override names no specific
        obligation, so it cannot become the attested, blocker-specific exception the
        new rules require. The note itself, its narrative, status, and approval stay;
        only the override flag, reason, approver, and time are cleared.
      * A missing same-target quarterly review is added, open and uncompleted.
      * Forms whose cycle began before the consumer's current effective date are removed,
        with their attestations. The conversion has no cycle to put them in. This is the
        one step that removes rows carrying recorded work; the database's owner has said
        compliance history here is not authoritative, and the records live in Credible.

    Order matters. The offset moves first, so every later date is computed in the new
    shape; duplicates go next, because recomputing two rows of one obligation onto the
    same deadline would collide with the unique (PersonId, Type, DueDate) index.

    It never touches consumer profiles or the scratchpad. Contents of dbo.People,
    dbo.Scratchpad, dbo.ScratchpadComments, and dbo.Notes are fingerprinted before and
    after inside one transaction, and the whole repair rolls back if any of them move.

    Run the check first. Use -WhatIfOnly to see what would change without committing.
#>
[CmdletBinding()]
param(
    [string]$SqlServer = '(localdb)\MSSQLLocalDB',
    [string]$DatabaseName = 'SatiProduction',
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'

"Sati 1.3.11 compliance-date repair"
"Run at   : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
"Server   : $SqlServer"
"Database : $DatabaseName"
if ($WhatIfOnly) { "Mode     : CHECK ONLY - nothing will be committed" } else { "Mode     : APPLY" }
""

$connection = New-Object System.Data.SqlClient.SqlConnection `
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=30;"
$connection.Open()
try {
    $transaction = $connection.BeginTransaction()
    $committed = $false
    try {
        $command = $connection.CreateCommand()
        $command.Transaction = $transaction
        $command.CommandTimeout = 600
        $command.Parameters.AddWithValue('@rollBackOnly', [bool]$WhatIfOnly) | Out-Null
        $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id = 1 AND EnvironmentName = N'Production')
    THROW 52800, 'This repair is only for an identity-marked Production database.', 1;
IF COL_LENGTH(N'dbo.Forms', N'TargetEffectiveDate') IS NOT NULL
    THROW 52801, 'This database is already converted; the repair is not needed.', 1;

-- The three things that must survive this repair unchanged, fingerprinted by content
-- rather than row count so an edit is caught as well as a deletion:
--   the client list, notes on or after 2026-09-01, and the last 30 days of scratchpad.
DECLARE @protectedFrom date = '2026-09-01';
DECLARE @padFrom datetime2 = DATEADD(day, -30, SYSUTCDATETIME());

DECLARE @peopleBefore bigint = (SELECT COUNT_BIG(*) FROM dbo.People);
DECLARE @peopleSumBefore bigint = (SELECT ISNULL(SUM(CONVERT(bigint, CHECKSUM(
    Id, FirstName, LastName, BirthDate, EffectiveDate, AgencyId, UserId))), 0) FROM dbo.People);

DECLARE @notesBefore bigint = (SELECT COUNT_BIG(*) FROM dbo.Notes);
DECLARE @recentNotesBefore bigint = (SELECT COUNT_BIG(*) FROM dbo.Notes WHERE EventDate >= @protectedFrom);
DECLARE @recentNoteSumBefore bigint = (SELECT ISNULL(SUM(CONVERT(bigint, CHECKSUM(
    Id, Narrative, EventDate, Status, Minutes, PersonId, CaseManagerJustification))), 0)
    FROM dbo.Notes WHERE EventDate >= @protectedFrom);

DECLARE @padBefore bigint = -1, @padSumBefore bigint = 0;
DECLARE @commentsBefore bigint = -1, @commentSumBefore bigint = 0;
IF OBJECT_ID(N'dbo.Scratchpad', N'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'SELECT @out = COUNT_BIG(*) FROM dbo.Scratchpad WHERE [Date] >= @from',
        N'@out bigint OUTPUT, @from datetime2', @out = @padBefore OUTPUT, @from = @padFrom;
    EXEC sp_executesql N'SELECT @out = ISNULL(SUM(CONVERT(bigint, CHECKSUM(Id, UserId, [Date], Content))), 0) FROM dbo.Scratchpad WHERE [Date] >= @from',
        N'@out bigint OUTPUT, @from datetime2', @out = @padSumBefore OUTPUT, @from = @padFrom;
END;
IF OBJECT_ID(N'dbo.ScratchpadComments', N'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'SELECT @out = COUNT_BIG(*) FROM dbo.ScratchpadComments WHERE CreatedAtUtc >= @from',
        N'@out bigint OUTPUT, @from datetime2', @out = @commentsBefore OUTPUT, @from = @padFrom;
    EXEC sp_executesql N'SELECT @out = ISNULL(SUM(CONVERT(bigint, CHECKSUM(Id, ScratchpadId, Content, CreatedAtUtc))), 0) FROM dbo.ScratchpadComments WHERE CreatedAtUtc >= @from',
        N'@out bigint OUTPUT, @from datetime2', @out = @commentSumBefore OUTPUT, @from = @padFrom;
END;

-- 1. The conversion reads a one-day quarterly offset as the old hard-coded shape. Move
--    it first, so every deadline computed below already lands in the new shape and no
--    second pass is needed to bring existing quarterly rows along.
UPDATE dbo.Settings SET Q4RDaysBeforeAnniversary = 5 WHERE Q4RDaysBeforeAnniversary = 1;
DECLARE @offsets int = @@ROWCOUNT;

-- 1b. Forms whose cycle falls before the consumer's current effective date. A form due
--     on or before admission derives a target year that began before admission, and the
--     conversion refuses it: the new model has no cycle to put it in. These are
--     compliance rows from before the effective date was set where it is now, so they
--     are removed, with their attestations, before any later step can recompute them or
--     give them a quarterly row. Nothing else references either table.
WITH pc AS (
    SELECT f.Id, CAST(p.EffectiveDate AS date) AS Eff, CAST(f.DueDate AS date) AS DueDate,
           DATEDIFF(year, CAST(p.EffectiveDate AS date), CAST(f.DueDate AS date)) AS N
    FROM dbo.Forms AS f
    INNER JOIN dbo.People AS p ON p.Id = f.PersonId
    WHERE p.EffectiveDate IS NOT NULL
), pa AS (
    SELECT pc.*, DATEADD(year, pc.N, pc.Eff) AS Anniversary FROM pc
), pt AS (
    SELECT pa.Id, pa.Eff,
           CASE WHEN pa.Anniversary >= pa.DueDate
                THEN DATEADD(year, pa.N - 1, pa.Eff) ELSE pa.Anniversary END AS Target
    FROM pa
)
SELECT Id INTO #preAdmission
FROM pt
WHERE Target < Eff OR DATEDIFF(year, Eff, Target) >= 150;

DELETE fa FROM dbo.FormAttestations AS fa INNER JOIN #preAdmission AS x ON x.Id = fa.FormId;
DECLARE @preAdmissionAttestations int = @@ROWCOUNT;
DELETE f FROM dbo.Forms AS f INNER JOIN #preAdmission AS x ON x.Id = f.Id;
DECLARE @preAdmissionForms int = @@ROWCOUNT;
DROP TABLE #preAdmission;

-- 2. Duplicate obligations. Two rows that resolve to one annual identity cannot both
--    become that identity, and recomputing both onto one deadline would collide with
--    the unique (PersonId, Type, DueDate) index, so they are reduced before any date
--    moves. Keep the row carrying evidence and delete its twin. A row with attestations
--    is never deleted: the attestation foreign key restricts it, and that evidence is
--    the reason to prefer that row in the first place.
WITH c2 AS (
    SELECT f.Id, f.PersonId, f.[Type], f.CompletedDate, f.OpenedDate,
           CAST(f.DueDate AS date) AS DueDate, CAST(p.EffectiveDate AS date) AS Eff,
           DATEDIFF(year, CAST(p.EffectiveDate AS date), CAST(f.DueDate AS date)) AS N,
           (SELECT COUNT(*) FROM dbo.FormAttestations AS fa WHERE fa.FormId = f.Id) AS Attestations
    FROM dbo.Forms AS f
    INNER JOIN dbo.People AS p ON p.Id = f.PersonId
    WHERE p.EffectiveDate IS NOT NULL
), a2 AS (
    SELECT c2.*, DATEADD(year, c2.N, c2.Eff) AS Anniversary FROM c2
), t2 AS (
    SELECT a2.*, CASE WHEN a2.Anniversary >= a2.DueDate
                      THEN DATEADD(year, a2.N - 1, a2.Eff) ELSE a2.Anniversary END AS Target
    FROM a2
), ranked AS (
    SELECT t2.*, ROW_NUMBER() OVER (
        PARTITION BY t2.PersonId, t2.[Type], t2.Target
        ORDER BY CASE WHEN t2.Attestations > 0 THEN 0 ELSE 1 END,
                 CASE WHEN t2.CompletedDate IS NOT NULL THEN 0 ELSE 1 END,
                 CASE WHEN t2.OpenedDate IS NOT NULL THEN 0 ELSE 1 END,
                 t2.Id) AS Rank
    FROM t2
)
SELECT Id, PersonId, [Type], Target, Attestations INTO #dupes FROM ranked WHERE Rank > 1;

DELETE f FROM dbo.Forms AS f
 INNER JOIN #dupes AS d ON d.Id = f.Id
 WHERE d.Attestations = 0;
DECLARE @duplicatesRemoved int = @@ROWCOUNT;

-- What could not be reduced. Those groups are left exactly as they are, and their
-- deadlines are not recomputed either, because moving both rows onto one date would
-- fail. The check will still report them, which is the honest outcome.
SELECT DISTINCT PersonId, [Type], Target INTO #stillDuplicated
FROM #dupes WHERE Attestations > 0;
DECLARE @duplicatesKept int = (SELECT COUNT(*) FROM #stillDuplicated);
DROP TABLE #dupes;

-- 3. Deadlines that do not match the documented legacy calculator.
WITH c AS (
    SELECT f.Id, f.PersonId, f.[Type], CAST(f.DueDate AS date) AS DueDate,
           CAST(p.EffectiveDate AS date) AS Eff, p.AgencyId,
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
)
SELECT Id, Expected INTO #fix
FROM f2
WHERE (([Type] = N'ComprehensiveAssessment'
         AND DueDate NOT IN (Expected, DATEADD(day, -60, NextAnniversary), DATEADD(day, -120, NextAnniversary)))
    OR ([Type] <> N'ComprehensiveAssessment' AND DueDate <> Expected))
  AND NOT EXISTS (SELECT 1 FROM #stillDuplicated AS sd
                  WHERE sd.PersonId = f2.PersonId AND sd.[Type] = f2.[Type] AND sd.Target = f2.Target);

UPDATE f SET f.DueDate = x.Expected
  FROM dbo.Forms AS f INNER JOIN #fix AS x ON x.Id = f.Id;
DECLARE @deadlines int = @@ROWCOUNT;
DROP TABLE #fix;
DROP TABLE #stillDuplicated;

-- 4. Legacy blanket note overrides.
UPDATE dbo.Notes
   SET ComplianceOverride = 0,
       OverrideReason = NULL,
       OverrideApprovedById = NULL,
       OverrideApprovedAt = NULL
 WHERE ComplianceOverride = 1;
DECLARE @overrides int = @@ROWCOUNT;

-- 5. The conversion also wants proof that annual rows carry the post-June-2026 shape:
--    a same-target quarterly review. Add the missing ones, open and uncompleted.
WITH c AS (
    SELECT f.PersonId, f.[Type], CAST(f.DueDate AS date) AS DueDate,
           CAST(p.EffectiveDate AS date) AS Eff, p.AgencyId,
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
), missing AS (
    SELECT DISTINCT annual.PersonId, annual.Target, annual.Eff, annual.AgencyId
    FROM t AS annual
    WHERE annual.[Type] NOT IN (N'Q1R', N'Q2R', N'Q3R', N'Q4R')
      AND NOT EXISTS (SELECT 1 FROM t AS q4
                      WHERE q4.PersonId = annual.PersonId
                        AND q4.[Type] = N'Q4R'
                        AND q4.Target = annual.Target)
)
INSERT dbo.Forms (PersonId, [Type], DueDate, CompletedDate)
SELECT m.PersonId, N'Q4R',
       DATEADD(day, -s.Q4RDaysBeforeAnniversary,
               DATEADD(year, DATEDIFF(year, m.Eff, m.Target) + 1, m.Eff)),
       NULL
FROM missing AS m
INNER JOIN dbo.Settings AS s ON s.AgencyId = m.AgencyId;
DECLARE @witnesses int = @@ROWCOUNT;

-- Prove the protected data is untouched, by content. Clearing an override flag is the
-- one permitted change to a note, so the note fingerprint excludes those columns.
IF (SELECT COUNT_BIG(*) FROM dbo.People) <> @peopleBefore
   OR (SELECT ISNULL(SUM(CONVERT(bigint, CHECKSUM(
        Id, FirstName, LastName, BirthDate, EffectiveDate, AgencyId, UserId))), 0) FROM dbo.People) <> @peopleSumBefore
    THROW 52802, 'The client list changed; rolling back.', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.Notes) <> @notesBefore
   OR (SELECT COUNT_BIG(*) FROM dbo.Notes WHERE EventDate >= @protectedFrom) <> @recentNotesBefore
   OR (SELECT ISNULL(SUM(CONVERT(bigint, CHECKSUM(
        Id, Narrative, EventDate, Status, Minutes, PersonId, CaseManagerJustification))), 0)
       FROM dbo.Notes WHERE EventDate >= @protectedFrom) <> @recentNoteSumBefore
    THROW 52803, 'Notes on or after 2026-09-01 changed; rolling back.', 1;

DECLARE @padAfter bigint = -1, @padSumAfter bigint = 0;
DECLARE @commentsAfter bigint = -1, @commentSumAfter bigint = 0;
IF OBJECT_ID(N'dbo.Scratchpad', N'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'SELECT @out = COUNT_BIG(*) FROM dbo.Scratchpad WHERE [Date] >= @from',
        N'@out bigint OUTPUT, @from datetime2', @out = @padAfter OUTPUT, @from = @padFrom;
    EXEC sp_executesql N'SELECT @out = ISNULL(SUM(CONVERT(bigint, CHECKSUM(Id, UserId, [Date], Content))), 0) FROM dbo.Scratchpad WHERE [Date] >= @from',
        N'@out bigint OUTPUT, @from datetime2', @out = @padSumAfter OUTPUT, @from = @padFrom;
END;
IF OBJECT_ID(N'dbo.ScratchpadComments', N'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'SELECT @out = COUNT_BIG(*) FROM dbo.ScratchpadComments WHERE CreatedAtUtc >= @from',
        N'@out bigint OUTPUT, @from datetime2', @out = @commentsAfter OUTPUT, @from = @padFrom;
    EXEC sp_executesql N'SELECT @out = ISNULL(SUM(CONVERT(bigint, CHECKSUM(Id, ScratchpadId, Content, CreatedAtUtc))), 0) FROM dbo.ScratchpadComments WHERE CreatedAtUtc >= @from',
        N'@out bigint OUTPUT, @from datetime2', @out = @commentSumAfter OUTPUT, @from = @padFrom;
END;
IF @padAfter <> @padBefore OR @padSumAfter <> @padSumBefore
   OR @commentsAfter <> @commentsBefore OR @commentSumAfter <> @commentSumBefore
    THROW 52804, 'Recent scratchpad content changed; rolling back.', 1;

SELECT @deadlines AS DeadlinesRecomputed, @overrides AS OverridesCleared,
       @witnesses AS Q4WitnessRowsAdded, @offsets AS AgencyOffsetsCorrected,
       @preAdmissionForms AS PreAdmissionFormsRemoved, @preAdmissionAttestations AS PreAdmissionAttestationsRemoved,
       @duplicatesRemoved AS DuplicateFormsRemoved, @duplicatesKept AS DuplicatesNeedingReview,
       @peopleBefore AS ClientsProtected, @recentNotesBefore AS NotesSinceSep1Protected,
       @padBefore AS ScratchpadEntriesLast30Days, @commentsBefore AS ScratchpadCommentsLast30Days;
'@
        $reader = $command.ExecuteReader()
        if (-not $reader.Read()) { throw 'The repair summary row was not returned.' }
        $summary = [ordered]@{}
        for ($i = 0; $i -lt $reader.FieldCount; $i++) { $summary[$reader.GetName($i)] = $reader.GetValue($i) }
        $reader.Close()
        foreach ($key in $summary.Keys) { "{0,-28}: {1}" -f $key, $summary[$key] }
        ""

        if ([int]$summary['DuplicatesNeedingReview'] -gt 0) {
            "NOTE: $($summary['DuplicatesNeedingReview']) duplicate obligation group(s) could not be reduced, because"
            '      more than one row carries recorded attestations. Nothing was deleted for'
            '      those, and the check will still report them. Send the check output to Josh.'
            ''
        }

        if ($WhatIfOnly) {
            $transaction.Rollback()
            'CHECK ONLY: nothing was changed. Run Step 2 to apply.'
        }
        else {
            $transaction.Commit()
            $committed = $true
            'DONE: compliance dates repaired.'
            'NEXT: install the Sati setup Josh sent, then start it. It backs up the database'
            '      and finishes the update itself. Starting your current version first is'
            '      harmless but does nothing.'
        }
    }
    finally {
        if (-not $committed -and -not $WhatIfOnly) { try { $transaction.Rollback() } catch { } }
        $transaction.Dispose()
    }
}
finally {
    $connection.Dispose()
}
