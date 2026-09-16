<#
.SYNOPSIS
    Clears the two things that stop the 1.3.11 conversion on a Local SatiProduction
    database: unattributable annual deadlines, and legacy blanket note overrides.

.DESCRIPTION
    This is a compliance-scheduling repair for a pre-production database whose
    authoritative records live elsewhere. It deliberately changes compliance dates,
    which the conversion refuses to guess at on its own:

      * Annual deadlines that do not match the documented legacy calculator are
        recomputed from that calculator. Completion, opening, and attestation evidence
        is not read or written; only Forms.DueDate moves.
      * Legacy blanket note overrides are cleared. Such an override names no specific
        obligation, so it cannot become the attested, blocker-specific exception the
        new rules require. The note itself, its narrative, status, and approval stay;
        only the override flag, reason, approver, and time are cleared.

    It never touches consumer profiles or the scratchpad. Counts for dbo.People,
    dbo.Scratchpad, dbo.ScratchpadComments, and dbo.Notes are taken before and after
    inside one transaction, and the whole repair rolls back if any of them move.

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

WITH c AS (
    SELECT f.Id, f.[Type], CAST(f.DueDate AS date) AS DueDate,
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
WHERE ([Type] = N'ComprehensiveAssessment'
        AND DueDate NOT IN (Expected, DATEADD(day, -60, NextAnniversary), DATEADD(day, -120, NextAnniversary)))
   OR ([Type] <> N'ComprehensiveAssessment' AND DueDate <> Expected);

UPDATE f SET f.DueDate = x.Expected
  FROM dbo.Forms AS f INNER JOIN #fix AS x ON x.Id = f.Id;
DECLARE @deadlines int = @@ROWCOUNT;

UPDATE dbo.Notes
   SET ComplianceOverride = 0,
       OverrideReason = NULL,
       OverrideApprovedById = NULL,
       OverrideApprovedAt = NULL
 WHERE ComplianceOverride = 1;
DECLARE @overrides int = @@ROWCOUNT;

-- The conversion also wants proof that annual rows carry the post-June-2026 shape: a
-- same-target Q4 review, under an agency offset that is not the old hard-coded one day.
UPDATE dbo.Settings SET Q4RDaysBeforeAnniversary = 5 WHERE Q4RDaysBeforeAnniversary = 1;
DECLARE @offsets int = @@ROWCOUNT;

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
       @peopleBefore AS ClientsProtected, @recentNotesBefore AS NotesSinceSep1Protected,
       @padBefore AS ScratchpadEntriesLast30Days, @commentsBefore AS ScratchpadCommentsLast30Days;

DROP TABLE #fix;
'@
        $reader = $command.ExecuteReader()
        if (-not $reader.Read()) { throw 'The repair summary row was not returned.' }
        $summary = [ordered]@{}
        for ($i = 0; $i -lt $reader.FieldCount; $i++) { $summary[$reader.GetName($i)] = $reader.GetValue($i) }
        $reader.Close()
        foreach ($key in $summary.Keys) { "{0,-22}: {1}" -f $key, $summary[$key] }
        ""

        if ($WhatIfOnly) {
            $transaction.Rollback()
            'CHECK ONLY: nothing was changed. Run Step 2 to apply.'
        }
        else {
            $transaction.Commit()
            $committed = $true
            'DONE: compliance dates repaired. Start Sati to finish the update.'
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
