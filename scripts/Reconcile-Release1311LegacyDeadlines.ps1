<#
.SYNOPSIS
    Reconciles legacy annual deadlines that sit exactly one day before the documented
    legacy calculator, so the 2026-09-15 conversion can attribute them.

.DESCRIPTION
    `20260915004541_CorrectAnnualComplianceAndBillingPolicy` refuses to convert a database
    whose stored deadlines do not match the documented post-2026-06-29 legacy calculator.
    Refusing is correct: an unattributable deadline cannot be turned into an annual target
    without guessing, and guessing would move a real billing boundary.

    One family of mismatches is mechanical rather than ambiguous. Those rows fall exactly one
    day before the expected deadline, which is the old hard-coded "one day before the
    anniversary" offset left behind when the agency setting became 0. This script corrects
    only that family, using the migration's own target derivation:

        target   = the greatest effective-date anniversary strictly before the stored deadline
        expected = the anniversary after that target, less the agency's configured offset

    A row is corrected only when its stored deadline is exactly `expected - 1 day`. Every
    other mismatch is left untouched and reported, because those need a human decision.

    Completion, opening, and attestation evidence is never read or written here; only
    `Forms.DueDate` moves, and only by one day, for rows in that family.

    This script does not create the annual target column or apply any migration. Run it
    before `Apply-Release1311Migrations.ps1` when that script's conversion aborts on the
    legacy-calculator guard.

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    ./scripts/Reconcile-Release1311LegacyDeadlines.ps1 -DatabaseName SatiDemo `
        -SqlServer sati-demo-satilogica-central.database.windows.net -AccessToken $token -WhatIfOnly
#>
[CmdletBinding()]
param(
    [ValidateSet('SatiDemo', 'SatiProduction')]
    [Parameter(Mandatory)]
    [string]$DatabaseName,

    [string]$SqlServer = '(localdb)\MSSQLLocalDB',

    [string]$AccessToken,

    # The second family: rows the pre-repair generator stamped with one deadline for every
    # type, so no per-type offset was ever stored. Recomputing them from the documented
    # calculator is what the current rules would produce, but unlike the one-day family it
    # can move the deadline on a row that already carries completion or opening evidence.
    # That is a deliberate data correction, so it needs its own switch and its own record.
    [switch]$RecomputeUnattributableDeadlines,

    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
$expectedEnvironment = if ($DatabaseName -ceq 'SatiDemo') { 'Demo' } else { 'Production' }
$usesAccessToken = -not [string]::IsNullOrWhiteSpace($AccessToken)

if ($DatabaseName -ceq 'SatiDemo' -and -not $usesAccessToken) {
    throw 'SatiDemo requires an Entra access token; integrated workstation credentials are not permitted.'
}

$connectionString = if ($usesAccessToken) {
    "Server=$SqlServer;Database=$DatabaseName;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;"
}
else {
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=30;"
}

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
if ($usesAccessToken) { $connection.AccessToken = $AccessToken }
$connection.Open()

try {
    $transaction = $connection.BeginTransaction()
    $committed = $false
    try {
        $command = $connection.CreateCommand()
        $command.Transaction = $transaction
        $command.CommandTimeout = 600
        $command.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
        $command.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
        $command.Parameters.AddWithValue('@recomputeOther', [bool]$RecomputeUnattributableDeadlines) | Out-Null
        $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() COLLATE Latin1_General_100_BIN2 <> @expectedDatabase COLLATE Latin1_General_100_BIN2
    THROW 52600, 'The connected database is not the exact database requested.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1
      AND EnvironmentName COLLATE Latin1_General_100_BIN2 = @expectedEnvironment COLLATE Latin1_General_100_BIN2)
    THROW 52601, 'The database identity marker does not match the requested environment.', 1;
IF COL_LENGTH(N'dbo.Forms', N'TargetEffectiveDate') IS NOT NULL
    THROW 52602, 'Forms.TargetEffectiveDate already exists; this database is already converted.', 1;

WITH candidate AS (
    SELECT f.Id, f.PersonId, f.[Type], CAST(f.DueDate AS date) AS DueDate,
           CAST(p.EffectiveDate AS date) AS Eff, p.AgencyId,
           DATEDIFF(year, CAST(p.EffectiveDate AS date), CAST(f.DueDate AS date)) AS N
    FROM dbo.Forms AS f
    INNER JOIN dbo.People AS p ON p.Id = f.PersonId
), anniversary AS (
    SELECT c.*, DATEADD(year, c.N, c.Eff) AS Anniversary FROM candidate AS c
), resolved AS (
    -- The migration's rule: the cycle whose start is the greatest effective-date
    -- anniversary strictly before the stored deadline.
    SELECT a.*, CASE WHEN a.Anniversary >= a.DueDate
                     THEN DATEADD(year, a.N - 1, a.Eff)
                     ELSE a.Anniversary END AS Target
    FROM anniversary AS a
), expected AS (
    SELECT r.*, DATEADD(year, DATEDIFF(year, r.Eff, r.Target) + 1, r.Eff) AS NextAnniversary,
           s.Q4RDaysBeforeAnniversary, s.PcpDaysBeforeAnniversary,
           s.CompAssessmentDaysBeforeAnniversary, s.ReclassificationDaysBeforeAnniversary,
           s.SafetyPlanDaysBeforeAnniversary, s.PrivacyPracticesDaysBeforeAnniversary,
           s.ReleaseAgencyDaysBeforeAnniversary, s.ReleaseDhhsDaysBeforeAnniversary,
           s.ReleaseMedicalDaysBeforeAnniversary
    FROM resolved AS r
    INNER JOIN dbo.Settings AS s ON s.AgencyId = r.AgencyId
), final AS (
    SELECT e.*, CASE e.[Type]
        WHEN N'Q1R' THEN DATEADD(day, 90, e.Target)
        WHEN N'Q2R' THEN DATEADD(day, 180, e.Target)
        WHEN N'Q3R' THEN DATEADD(day, 270, e.Target)
        WHEN N'Q4R' THEN DATEADD(day, -e.Q4RDaysBeforeAnniversary, e.NextAnniversary)
        WHEN N'PCP' THEN DATEADD(day, -e.PcpDaysBeforeAnniversary, e.NextAnniversary)
        WHEN N'ComprehensiveAssessment' THEN DATEADD(day, -e.CompAssessmentDaysBeforeAnniversary, e.NextAnniversary)
        WHEN N'Reclassification' THEN DATEADD(day, -e.ReclassificationDaysBeforeAnniversary, e.NextAnniversary)
        WHEN N'SafetyPlan' THEN DATEADD(day, -e.SafetyPlanDaysBeforeAnniversary, e.NextAnniversary)
        WHEN N'PrivacyPractices' THEN DATEADD(day, -e.PrivacyPracticesDaysBeforeAnniversary, e.NextAnniversary)
        WHEN N'Release_Agency' THEN DATEADD(day, -e.ReleaseAgencyDaysBeforeAnniversary, e.NextAnniversary)
        WHEN N'Release_DHHS' THEN DATEADD(day, -e.ReleaseDhhsDaysBeforeAnniversary, e.NextAnniversary)
        WHEN N'Release_Medical' THEN DATEADD(day, -e.ReleaseMedicalDaysBeforeAnniversary, e.NextAnniversary)
    END AS Expected
    FROM expected AS e
)
SELECT Id, [Type], DueDate, Expected, PersonId
INTO #mismatch
FROM final
WHERE ([Type] = N'ComprehensiveAssessment'
        AND DueDate NOT IN (Expected,
                            DATEADD(day, -60, NextAnniversary),
                            DATEADD(day, -120, NextAnniversary)))
   OR ([Type] <> N'ComprehensiveAssessment' AND DueDate <> Expected);

DECLARE @minusOneBefore int = (SELECT COUNT(*) FROM #mismatch WHERE DATEDIFF(day, Expected, DueDate) = -1);
DECLARE @otherBefore int = (SELECT COUNT(*) FROM #mismatch WHERE DATEDIFF(day, Expected, DueDate) <> -1);

UPDATE f
   SET f.DueDate = m.Expected
  FROM dbo.Forms AS f
  INNER JOIN #mismatch AS m ON m.Id = f.Id
 WHERE DATEDIFF(day, m.Expected, m.DueDate) = -1;
DECLARE @corrected int = @@ROWCOUNT;

-- How much dated evidence the second pass would touch, counted before it runs so the
-- number is reported whether or not the switch was given.
DECLARE @otherWithEvidence int = (
    SELECT COUNT(*)
    FROM #mismatch AS m
    INNER JOIN dbo.Forms AS f ON f.Id = m.Id
    WHERE DATEDIFF(day, m.Expected, m.DueDate) <> -1
      AND (f.CompletedDate IS NOT NULL OR f.OpenedDate IS NOT NULL));

DECLARE @recomputed int = 0;
IF @recomputeOther = 1
BEGIN
    UPDATE f
       SET f.DueDate = m.Expected
      FROM dbo.Forms AS f
      INNER JOIN #mismatch AS m ON m.Id = f.Id
     WHERE DATEDIFF(day, m.Expected, m.DueDate) <> -1;
    SET @recomputed = @@ROWCOUNT;
END;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    @minusOneBefore AS MinusOneFamilyFound,
    @corrected AS DeadlinesCorrected,
    @otherBefore AS OtherMismatchesFound,
    @recomputed AS DeadlinesRecomputed,
    @otherWithEvidence AS RecomputedRowsCarryingEvidence,
    (SELECT COUNT(DISTINCT PersonId) FROM #mismatch WHERE DATEDIFF(day, Expected, DueDate) <> -1) AS OtherMismatchPeople;

DROP TABLE #mismatch;
'@
        $reader = $command.ExecuteReader()
        if (-not $reader.Read()) { throw 'The reconciliation summary row was not returned.' }
        $result = [pscustomobject][ordered]@{
            DatabaseName = $reader.GetString(0)
            EnvironmentName = $reader.GetString(1)
            MinusOneFamilyFound = $reader.GetInt32(2)
            DeadlinesCorrected = $reader.GetInt32(3)
            OtherMismatchesFound = $reader.GetInt32(4)
            DeadlinesRecomputed = $reader.GetInt32(5)
            RecomputedRowsCarryingEvidence = $reader.GetInt32(6)
            OtherMismatchPeople = $reader.GetInt32(7)
            RolledBack = [bool]$WhatIfOnly
        }
        $reader.Close()

        if ($result.DeadlinesCorrected -ne $result.MinusOneFamilyFound) {
            throw "Expected to correct $($result.MinusOneFamilyFound) rows, corrected $($result.DeadlinesCorrected)."
        }
        if ($RecomputeUnattributableDeadlines -and
            $result.DeadlinesRecomputed -ne $result.OtherMismatchesFound) {
            throw "Expected to recompute $($result.OtherMismatchesFound) rows, recomputed $($result.DeadlinesRecomputed)."
        }

        if ($WhatIfOnly) { $transaction.Rollback() }
        else { $transaction.Commit(); $committed = $true }
        $result
    }
    finally {
        if (-not $committed -and -not $WhatIfOnly) { try { $transaction.Rollback() } catch { } }
        $transaction.Dispose()
    }
}
finally {
    $connection.Dispose()
}
