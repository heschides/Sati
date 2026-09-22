-- Read-only review of annual Comprehensive Assessment assignments.
-- Run only against the identity-marked Local Production SatiProduction database.
-- Returns IDs and dates, not names or clinical content. No row is changed.
-- A flagged row is a review candidate, not proof that its attestation is wrong:
-- completing an older assessment late can be legitimate.

SET NOCOUNT ON;

DECLARE @AsOf date = CAST(GETDATE() AS date);

IF DB_NAME() <> N'SatiProduction'
   OR NOT EXISTS (
       SELECT 1 FROM dbo.SatiDatabaseIdentity
       WHERE Id = 1 AND EnvironmentName = N'Production')
    THROW 50000, 'Stopped: this is not the identity-marked SatiProduction database.', 1;

IF COL_LENGTH(N'dbo.Forms', N'TargetEffectiveDate') IS NULL
   OR OBJECT_ID(N'dbo.FormAttestations', N'U') IS NULL
    THROW 50000, 'Stopped: annual target or attestation schema is unavailable.', 1;

;WITH Assessments AS (
    SELECT
        f.Id AS FormId,
        f.PersonId,
        CAST(f.TargetEffectiveDate AS date) AS PlanStartsOn,
        CAST(f.DueDate AS date) AS StoredDueOn,
        CAST(f.CompletedDate AS date) AS CompletedOn,
        LAG(CAST(f.TargetEffectiveDate AS date)) OVER (
            PARTITION BY f.PersonId ORDER BY f.TargetEffectiveDate, f.Id) AS PreviousPlanStartsOn,
        LAG(CAST(f.CompletedDate AS date)) OVER (
            PARTITION BY f.PersonId ORDER BY f.TargetEffectiveDate, f.Id) AS PreviousCompletedOn,
        LEAD(f.Id) OVER (
            PARTITION BY f.PersonId ORDER BY f.TargetEffectiveDate, f.Id) AS NextFormId,
        LEAD(CAST(f.TargetEffectiveDate AS date)) OVER (
            PARTITION BY f.PersonId ORDER BY f.TargetEffectiveDate, f.Id) AS NextPlanStartsOn,
        LEAD(CAST(f.DueDate AS date)) OVER (
            PARTITION BY f.PersonId ORDER BY f.TargetEffectiveDate, f.Id) AS NextDueOn,
        LEAD(CAST(f.CompletedDate AS date)) OVER (
            PARTITION BY f.PersonId ORDER BY f.TargetEffectiveDate, f.Id) AS NextCompletedOn
    FROM dbo.Forms AS f
    WHERE f.[Type] = N'ComprehensiveAssessment'
), LatestLedger AS (
    SELECT
        a.FormId,
        a.Kind,
        CAST(a.CompletedOn AS date) AS LedgerCompletedOn,
        a.ActorKind,
        a.RecordedAtUtc,
        ROW_NUMBER() OVER (
            PARTITION BY a.FormId ORDER BY a.RecordedAtUtc DESC, a.Id DESC) AS SequenceNumber
    FROM dbo.FormAttestations AS a
), EarlierSystemEvidence AS (
    SELECT DISTINCT a.FormId
    FROM dbo.FormAttestations AS a
    WHERE a.Kind = N'Attested'
      AND a.ActorKind = N'System'
      AND a.CompletedOn IS NOT NULL
      AND CAST(a.CompletedOn AS date) > CAST(a.RecordedAtUtc AS date)
)
SELECT
    assessment.PersonId,
    assessment.FormId,
    assessment.PlanStartsOn,
    assessment.StoredDueOn,
    DATEADD(day, -90, assessment.PlanStartsOn) AS DefaultNinetyDayDueOn,
    assessment.CompletedOn,
    assessment.PreviousPlanStartsOn,
    assessment.PreviousCompletedOn,
    assessment.NextFormId,
    assessment.NextPlanStartsOn,
    assessment.NextDueOn,
    assessment.NextCompletedOn,
    ledger.Kind AS LatestLedgerKind,
    ledger.LedgerCompletedOn,
    ledger.ActorKind AS LatestLedgerActorKind,
    ledger.RecordedAtUtc AS LatestLedgerRecordedAtUtc,
    CASE
        WHEN assessment.CompletedOn IS NULL AND assessment.StoredDueOn < @AsOf
            THEN N'Overdue assessment is incomplete; verify the correct plan-year evidence'
        WHEN assessment.CompletedOn > assessment.PlanStartsOn
             AND assessment.NextFormId IS NOT NULL
             AND assessment.NextCompletedOn IS NULL
            THEN N'Completion after this plan started; later plan remains incomplete'
        WHEN assessment.CompletedOn > assessment.PlanStartsOn
            THEN N'Completion after this plan started; verify intended plan'
        WHEN assessment.CompletedOn IS NOT NULL AND ledger.FormId IS NULL
            THEN N'Completed form has no attestation ledger row'
        WHEN assessment.CompletedOn IS NULL AND ledger.Kind = N'Attested'
            THEN N'Latest ledger attests a form whose completion is empty'
        WHEN assessment.CompletedOn IS NOT NULL AND
             (ledger.Kind <> N'Attested' OR
              ledger.LedgerCompletedOn <> assessment.CompletedOn)
            THEN N'Latest ledger and form completion disagree'
        WHEN earlier.FormId IS NOT NULL
            THEN N'Earlier system evidence recorded before its completion date'
        ELSE N'Review'
    END AS ReviewReason
FROM Assessments AS assessment
LEFT JOIN LatestLedger AS ledger
    ON ledger.FormId = assessment.FormId AND ledger.SequenceNumber = 1
LEFT JOIN EarlierSystemEvidence AS earlier
    ON earlier.FormId = assessment.FormId
WHERE assessment.CompletedOn > assessment.PlanStartsOn
   OR (assessment.CompletedOn IS NULL AND assessment.StoredDueOn < @AsOf)
   OR (assessment.CompletedOn IS NOT NULL AND ledger.FormId IS NULL)
   OR (assessment.CompletedOn IS NULL AND ledger.Kind = N'Attested')
   OR (assessment.CompletedOn IS NOT NULL AND
       (ledger.Kind <> N'Attested' OR
        ledger.LedgerCompletedOn <> assessment.CompletedOn))
   OR earlier.FormId IS NOT NULL
ORDER BY assessment.PersonId, assessment.PlanStartsOn;
