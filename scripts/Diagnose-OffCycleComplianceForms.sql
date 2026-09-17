/*
================================================================================
  Diagnose: the billing gate blocks on a form the client profile does not show
================================================================================

  Reported 2026-09-17 on release 1.3.14. A 9/9/2026 visit note was refused
  because "Q1 Review was due Sep 6, 2026", while the client profile showed
  Quarter 1 due 06/05/26 and complete, and every annual form completed
  03/07/26.

  The profile shows only the rows for the plan year the client's effective
  date gives today (Person.FindCurrentCycleForm, keyed by TargetEffectiveDate).
  BillingComplianceGate.EvaluateBillingWindow checks EVERY row in Forms. A Q1
  Review due 9/6/2026 belongs to a plan year starting about 6/8/2026, which is
  not an anniversary of a 3/7 effective date. This script finds rows whose
  plan year does not line up with the client's current effective date, and
  shows the history needed to say where they came from.

  READ ONLY. No INSERT, UPDATE, DELETE or DDL.
  Output contains PersonId, form types, dates and flags only. No names.

  HOW TO RUN
  ----------
  On the Joshu login (the one with the real SatiProduction), with Sati closed
  or open, in PowerShell:

      sqlcmd -S "(localdb)\MSSQLLocalDB" -d SatiProduction -E -W -s "|" `
             -i C:\Users\Public\Diagnose-OffCycleComplianceForms.sql `
             -o C:\Users\Public\off-cycle-forms.txt

  Set @FirstName and @LastName below to the client from the report first.
  They are used only to find the PersonId and are never printed.
================================================================================
*/

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

DECLARE @FirstName nvarchar(100) = N'';   -- the reported client's first name
DECLARE @LastName  nvarchar(100) = N'';   -- the reported client's last name
DECLARE @Today date = CAST(GETDATE() AS date);

--------------------------------------------------------------------------------
-- 0. Which database is this?
--------------------------------------------------------------------------------
SELECT N'0-connection' AS Result, DB_NAME() AS DatabaseName, @@SERVERNAME AS ServerName,
       @Today AS Today,
       (SELECT COUNT(*) FROM dbo.People) AS PeopleRows,
       (SELECT COUNT(*) FROM dbo.Forms)  AS FormRows;

--------------------------------------------------------------------------------
-- Every form row, with the plan-year start its effective date implies.
-- A row is off-cycle when its TargetEffectiveDate is not an anniversary of
-- the client's current EffectiveDate. (February 29 admissions can show a
-- false positive on February 28; check those by eye.)
--------------------------------------------------------------------------------
;WITH FormCycle AS (
    SELECT f.Id AS FormId, f.PersonId, f.Type, f.TargetEffectiveDate, f.DueDate,
           f.OpenedDate, f.CompletedDate,
           CAST(p.EffectiveDate AS date) AS PersonEffectiveDate,
           CASE
               WHEN p.EffectiveDate IS NULL THEN N'no effective date'
               WHEN f.TargetEffectiveDate < '1901-01-01' THEN N'no target (legacy)'
               WHEN CAST(f.TargetEffectiveDate AS date) =
                    DATEADD(year,
                            DATEDIFF(year, p.EffectiveDate, f.TargetEffectiveDate),
                            CAST(p.EffectiveDate AS date))
                   THEN N'on cycle'
               ELSE N'OFF CYCLE'
           END AS CycleFit
    FROM dbo.Forms AS f
    JOIN dbo.People AS p ON p.Id = f.PersonId
)
SELECT * INTO #FormCycle FROM FormCycle;

--------------------------------------------------------------------------------
-- 1. Caseload-wide: how many clients have off-cycle rows, and how many of
--    those rows are incomplete and past due (what the gate will block on).
--------------------------------------------------------------------------------
SELECT N'1-off-cycle-by-client' AS Result,
       PersonId,
       MIN(PersonEffectiveDate) AS PersonEffectiveDate,
       COUNT(*) AS OffCycleRows,
       SUM(CASE WHEN CAST(DueDate AS date) < @Today AND CompletedDate IS NULL THEN 1 ELSE 0 END)
           AS OffCyclePastDueIncomplete,
       MIN(CAST(TargetEffectiveDate AS date)) AS EarliestOffCycleTarget,
       MAX(CAST(TargetEffectiveDate AS date)) AS LatestOffCycleTarget
FROM #FormCycle
WHERE CycleFit = N'OFF CYCLE'
GROUP BY PersonId
ORDER BY PersonId;

--------------------------------------------------------------------------------
-- 2. The reported client: its PersonId and current effective date.
--------------------------------------------------------------------------------
DECLARE @PersonId int =
    (SELECT TOP (1) Id FROM dbo.People
     WHERE FirstName = @FirstName AND LastName = @LastName
     ORDER BY Id);

SELECT N'2-client' AS Result, @PersonId AS PersonId,
       (SELECT CAST(EffectiveDate AS date) FROM dbo.People WHERE Id = @PersonId) AS EffectiveDate,
       (SELECT COUNT(*) FROM dbo.People
        WHERE FirstName = @FirstName AND LastName = @LastName) AS PeopleWithThatName;

--------------------------------------------------------------------------------
-- 3. Every form row for that client, oldest plan year first. FormId order is
--    creation order, so a block of higher Ids with a different target shows
--    when the second set was made.
--------------------------------------------------------------------------------
SELECT N'3-client-forms' AS Result, FormId, Type,
       CAST(TargetEffectiveDate AS date) AS TargetEffectiveDate,
       CAST(DueDate AS date) AS DueDate,
       CAST(OpenedDate AS date) AS OpenedDate,
       CAST(CompletedDate AS date) AS CompletedDate,
       CycleFit
FROM #FormCycle
WHERE PersonId = @PersonId
ORDER BY TargetEffectiveDate, DueDate, Type, FormId;

--------------------------------------------------------------------------------
-- 4. Attestation history on that client's forms (who kind, when recorded).
--------------------------------------------------------------------------------
SELECT N'4-client-attestations' AS Result, a.FormId, fc.Type,
       CAST(fc.TargetEffectiveDate AS date) AS TargetEffectiveDate,
       a.Kind, CAST(a.CompletedOn AS date) AS CompletedOn, a.ActorKind,
       a.RecordedAtUtc
FROM dbo.FormAttestations AS a
JOIN #FormCycle AS fc ON fc.FormId = a.FormId
WHERE fc.PersonId = @PersonId
ORDER BY a.RecordedAtUtc, a.Id;

--------------------------------------------------------------------------------
-- 5. When the client record changed (the snapshots are compressed; this is
--    only the timeline, to line up with the form Ids and attestations above).
--------------------------------------------------------------------------------
SELECT N'5-client-versions' AS Result, Version, ChangeKind, ChangedAtUtc
FROM dbo.PersonVersions
WHERE PersonId = @PersonId
ORDER BY Version;

DROP TABLE #FormCycle;
