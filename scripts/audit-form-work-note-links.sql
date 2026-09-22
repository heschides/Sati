-- Read-only inventory for the form-note/date/billing correction.
-- Run on the identity-marked Joshu Local Production database after the new
-- Note.FormId migration is applied. Do not infer or update links from this list.
-- IDs and dates only: no names, note narrative, or clinical text are returned.
SET NOCOUNT ON;

IF DB_NAME() <> N'SatiProduction'
   OR NOT EXISTS (
       SELECT 1 FROM dbo.SatiDatabaseIdentity
       WHERE Id = 1 AND EnvironmentName = N'Production')
    THROW 50000, 'Stopped: this is not the identity-marked SatiProduction database.', 1;

IF COL_LENGTH(N'dbo.Notes', N'FormId') IS NULL
   OR COL_LENGTH(N'dbo.Forms', N'TargetEffectiveDate') IS NULL
   OR OBJECT_ID(N'dbo.FormAttestations', N'U') IS NULL
    THROW 50000, 'Stopped: exact form-note link or attestation schema is unavailable.', 1;

;WITH FormNotes AS (
    SELECT n.Id AS NoteId, n.PersonId, n.FormId, n.FormType,
           n.[Status] AS NoteStatus, CAST(n.EventDate AS date) AS ActivityDate,
           f.PersonId AS LinkedPersonId, f.[Type] AS LinkedFormType,
           CAST(f.DueDate AS date) AS DueDate,
           CAST(f.CompletedDate AS date) AS CompletedDate
    FROM dbo.Notes AS n
    LEFT JOIN dbo.Forms AS f ON f.Id = n.FormId
    WHERE n.NoteType = 2 -- NoteType.Form, persisted enum value
      AND (n.FormType IS NULL OR n.FormType NOT IN (9, 10, 11)) -- releases excluded
), Claimed AS (
    SELECT c.NoteId, COUNT_BIG(*) AS ClaimLineCount,
           MIN(c.Id) AS FirstClaimLineId,
           MAX(c.Id) AS LastClaimLineId
    FROM dbo.ClaimLines AS c
    GROUP BY c.NoteId
), LatestAttestation AS (
    SELECT a.FormId, a.Kind, CAST(a.CompletedOn AS date) AS LedgerDate,
           a.EvidenceNoteId,
           ROW_NUMBER() OVER (PARTITION BY a.FormId
                              ORDER BY a.RecordedAtUtc DESC, a.Id DESC) AS rn
    FROM dbo.FormAttestations AS a
)
SELECT n.PersonId, n.NoteId, n.FormId, n.FormType, n.NoteStatus,
       n.ActivityDate, n.DueDate, n.CompletedDate,
       a.Kind AS LatestLedgerKind, a.LedgerDate,
       a.EvidenceNoteId AS LatestEvidenceNoteId,
       COALESCE(c.ClaimLineCount, 0) AS ClaimLineCount,
       c.FirstClaimLineId, c.LastClaimLineId,
       CASE
           WHEN n.FormId IS NULL THEN N'Unlinked legacy form note: inspect exact obligation'
           WHEN n.LinkedPersonId IS NULL THEN N'Linked form missing'
           WHEN n.LinkedPersonId <> n.PersonId OR
                n.LinkedFormType <> CASE n.FormType
                    WHEN 0 THEN N'Q1R' WHEN 1 THEN N'Q2R'
                    WHEN 2 THEN N'Q3R' WHEN 3 THEN N'Q4R'
                    WHEN 4 THEN N'PCP' WHEN 5 THEN N'ComprehensiveAssessment'
                    WHEN 6 THEN N'Reclassification' WHEN 7 THEN N'SafetyPlan'
                    WHEN 8 THEN N'PrivacyPractices' ELSE N'Unknown' END
                THEN N'Form person or type mismatch'
           WHEN n.ActivityDate IS NULL THEN N'Missing activity date'
           WHEN n.CompletedDate IS NULL THEN N'No live form attestation'
           WHEN n.ActivityDate <> n.CompletedDate THEN N'Note date differs from attestation'
           WHEN n.CompletedDate > n.DueDate THEN N'Form work completed after due date'
           WHEN a.FormId IS NULL OR a.Kind <> N'Attested' OR a.LedgerDate <> n.CompletedDate
                THEN N'Completion projection and latest ledger disagree'
           ELSE N'Linked and on time'
       END AS ReviewReason
FROM FormNotes AS n
LEFT JOIN Claimed AS c ON c.NoteId = n.NoteId
LEFT JOIN LatestAttestation AS a ON a.FormId = n.FormId AND a.rn = 1
ORDER BY CASE WHEN c.ClaimLineCount > 0 THEN 0 ELSE 1 END,
         n.PersonId, n.NoteId;

-- Separate seed-cutoff review. These are candidates for human verification;
-- an old overdue form may genuinely have remained incomplete.
;WITH LatestAttestation AS (
    SELECT a.FormId, a.Kind, CAST(a.CompletedOn AS date) AS LedgerDate,
           ROW_NUMBER() OVER (PARTITION BY a.FormId
                              ORDER BY a.RecordedAtUtc DESC, a.Id DESC) AS rn
    FROM dbo.FormAttestations AS a
)
SELECT f.PersonId, f.Id AS FormId, f.[Type] AS FormType,
       CAST(f.TargetEffectiveDate AS date) AS TargetEffectiveDate,
       CAST(f.DueDate AS date) AS DueDate,
       CAST(f.CompletedDate AS date) AS CompletedDate,
       a.Kind AS LatestLedgerKind, a.LedgerDate,
       CASE WHEN f.CompletedDate IS NULL THEN N'Pre-September due form has no completion'
            ELSE N'Completion and latest attestation ledger disagree' END AS ReviewReason
FROM dbo.Forms AS f
LEFT JOIN LatestAttestation AS a ON a.FormId = f.Id AND a.rn = 1
WHERE f.DueDate < CONVERT(date, '2026-09-01')
  AND (f.CompletedDate IS NULL OR a.FormId IS NULL OR
       a.Kind <> N'Attested' OR a.LedgerDate <> CAST(f.CompletedDate AS date))
ORDER BY f.PersonId, f.DueDate, f.Id;
