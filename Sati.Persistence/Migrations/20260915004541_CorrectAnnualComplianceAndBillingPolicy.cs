using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class CorrectAnnualComplianceAndBillingPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This migration changes the identity of an annual obligation from its
            // deadline to its annual effective date.  Refuse to guess when the old
            // rows do not contain enough trustworthy information for that conversion.
            // The migration is transactional on SQL Server, but doing these checks
            // first also gives the operator a useful remediation message.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM dbo.Forms AS f
                    LEFT JOIN dbo.People AS p ON p.Id = f.PersonId
                    WHERE p.Id IS NULL OR p.EffectiveDate IS NULL
                )
                    THROW 50000, 'Cannot assign annual form effective dates: every form must belong to a person with an EffectiveDate. Correct the affected consumer records and retry.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM dbo.Forms AS f
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM (VALUES
                            (N'Q1R'), (N'Q2R'), (N'Q3R'), (N'Q4R'), (N'PCP'),
                            (N'ComprehensiveAssessment'), (N'Reclassification'),
                            (N'SafetyPlan'), (N'PrivacyPractices'), (N'Release_Agency'),
                            (N'Release_DHHS'), (N'Release_Medical')) AS supported([Type])
                        WHERE f.[Type] COLLATE Latin1_General_100_BIN2 =
                              supported.[Type] COLLATE Latin1_General_100_BIN2
                          AND DATALENGTH(f.[Type]) = DATALENGTH(supported.[Type])
                    )
                )
                    THROW 50000, 'Cannot assign annual form effective dates: dbo.Forms contains an unknown Type. Map it to a supported type and retry.', 1;

                IF EXISTS (
                    SELECT 1 FROM dbo.Notes
                    WHERE ComplianceOverride = 1
                )
                    THROW 50000, 'Legacy note compliance overrides cannot be converted into attested, blocker-specific exceptions. Review and resolve those notes before retrying this migration.', 1;

                IF EXISTS (
                    SELECT 1 FROM dbo.Notes
                    WHERE DATALENGTH(OverrideReason) > 8000
                )
                    THROW 50000, 'A note override reason is longer than 4000 characters. Shorten it without losing required evidence before retrying this migration.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Forms_PersonId_Type_DueDate",
                table: "Forms");

            migrationBuilder.DropIndex(
                name: "IX_DocumentArtifacts_OneLivePerCycle",
                table: "DocumentArtifacts");

            migrationBuilder.AlterColumn<int>(
                name: "BillingComplianceRequirements",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 7,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 31);

            migrationBuilder.AddColumn<bool>(
                name: "AllowPastBillingPolicyEffectiveDates",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AssignmentKnownOn",
                table: "PersonProviders",
                type: "date",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OverrideReason",
                table: "Notes",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OverrideAttestationConfirmed",
                table: "Notes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OverrideObligationIdsJson",
                table: "Notes",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TargetEffectiveDate",
                table: "Forms",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReleaseObligationId",
                table: "DocumentArtifacts",
                type: "bigint",
                nullable: true);

            // Stage the new identity as nullable and validate the exact legacy
            // shape before changing anything. The old runtime generated every row
            // for a cycle beginning at TargetEffectiveDate, but stored annual
            // deadlines relative to the *next* anniversary. That is the one-year
            // shift this migration corrects: a legacy PCP due 2027-03-07 for the
            // cycle that began 2026-03-07 becomes target/due 2026-03-07. Reviews
            // already counted forward from that same cycle start.
            //
            // DATEADD from the person's original EffectiveDate preserves SQL
            // Server's Feb-29 anniversary semantics. No completion, opening, or
            // attestation evidence is inferred or rewritten.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM dbo.Forms AS f
                    INNER JOIN dbo.People AS p ON p.Id = f.PersonId
                    WHERE CAST(p.EffectiveDate AS date) < '1900-01-01'
                       OR CAST(f.DueDate AS date) < '1900-01-01'
                )
                    THROW 50000, 'Cannot assign annual form effective dates: an EffectiveDate or DueDate is before 1900-01-01.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM dbo.Forms AS f
                    INNER JOIN dbo.People AS p ON p.Id = f.PersonId
                    LEFT JOIN dbo.Settings AS s ON s.AgencyId = p.AgencyId
                    WHERE p.AgencyId IS NULL OR s.Id IS NULL
                )
                    THROW 50000, 'Cannot correct annual deadlines: every form owner must belong to an agency with one Settings row. Correct tenant ownership/settings and retry.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM dbo.Forms AS f
                    INNER JOIN dbo.People AS p ON p.Id = f.PersonId
                    INNER JOIN dbo.Settings AS s ON s.AgencyId = p.AgencyId
                    WHERE s.Q4RDaysBeforeAnniversary NOT BETWEEN 0 AND 364
                       OR s.PcpDaysBeforeAnniversary NOT BETWEEN 0 AND 364
                       OR s.CompAssessmentDaysBeforeAnniversary NOT BETWEEN 0 AND 364
                       OR s.ReclassificationDaysBeforeAnniversary NOT BETWEEN 0 AND 364
                       OR s.SafetyPlanDaysBeforeAnniversary NOT BETWEEN 0 AND 364
                       OR s.PrivacyPracticesDaysBeforeAnniversary NOT BETWEEN 0 AND 364
                       OR s.ReleaseAgencyDaysBeforeAnniversary NOT BETWEEN 0 AND 364
                       OR s.ReleaseDhhsDaysBeforeAnniversary NOT BETWEEN 0 AND 364
                       OR s.ReleaseMedicalDaysBeforeAnniversary NOT BETWEEN 0 AND 364
                )
                    THROW 50000, 'Cannot correct annual deadlines: an anniversary offset is outside 0-364 days and can cross the cycle boundary. Review that agency setting and retry.', 1;

                ;WITH AnniversaryCandidate AS (
                    SELECT
                        f.Id,
                        CAST(f.DueDate AS date) AS DueDate,
                        CAST(p.EffectiveDate AS date) AS OriginalEffectiveDate,
                        DATEDIFF(
                            year,
                            CAST(p.EffectiveDate AS date),
                            CAST(f.DueDate AS date)) AS AnniversaryNumber
                    FROM dbo.Forms AS f
                    INNER JOIN dbo.People AS p ON p.Id = f.PersonId
                ), SameYearAnniversary AS (
                    SELECT
                        c.*,
                        DATEADD(year, c.AnniversaryNumber, c.OriginalEffectiveDate) AS Anniversary
                    FROM AnniversaryCandidate AS c
                ), ResolvedTarget AS (
                    SELECT
                        a.Id,
                        CASE
                            -- All old rows were created for the cycle whose start is
                            -- the greatest effective-date anniversary strictly before
                            -- the stored deadline. An exact-anniversary deadline was
                            -- the old cycle end, not the new target identity.
                            WHEN a.Anniversary >= a.DueDate
                                THEN DATEADD(year, a.AnniversaryNumber - 1, a.OriginalEffectiveDate)
                            ELSE a.Anniversary
                        END AS TargetEffectiveDate
                    FROM SameYearAnniversary AS a
                )
                UPDATE f
                   SET TargetEffectiveDate = CAST(r.TargetEffectiveDate AS date)
                  FROM dbo.Forms AS f
                  INNER JOIN ResolvedTarget AS r ON r.Id = f.Id;

                IF EXISTS (
                    SELECT 1 FROM dbo.Forms
                    WHERE TargetEffectiveDate IS NULL
                       OR TargetEffectiveDate < '1900-01-01'
                       OR TargetEffectiveDate >= '9999-01-01'
                )
                    THROW 50000, 'Annual form effective-date backfill produced a missing, pre-1900, or year-9999 target that cannot have a representable next anniversary. Correct the source dates and retry.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM dbo.Forms AS f
                    INNER JOIN dbo.People AS p ON p.Id = f.PersonId
                    WHERE f.TargetEffectiveDate < CAST(p.EffectiveDate AS date)
                       OR DATEDIFF(
                              year,
                              CAST(p.EffectiveDate AS date),
                              f.TargetEffectiveDate) >= 150
                )
                    THROW 50000, 'Annual form effective-date backfill produced a target before admission or beyond the supported 150-cycle history. Reconcile that source row and retry.', 1;

                -- Prove that every row has a recognized post-2026-06-29 legacy
                -- deadline shape before relying on the conversion. CA explicitly
                -- accepts 120 and 60 days as well as the current value because the
                -- 2026-08-07 migration changed that setting from 120 to 60 without
                -- rewriting already-stored Forms; both generations therefore exist.
                -- Other mismatches abort rather than guessing at rows created before
                -- the documented repair or manually altered afterward.
                IF EXISTS (
                    SELECT 1
                    FROM dbo.Forms AS f
                    INNER JOIN dbo.People AS p ON p.Id = f.PersonId
                    INNER JOIN dbo.Settings AS s ON s.AgencyId = p.AgencyId
                    CROSS APPLY (
                        SELECT DATEADD(
                            year,
                            DATEDIFF(
                                year,
                                CAST(p.EffectiveDate AS date),
                                f.TargetEffectiveDate) + 1,
                            CAST(p.EffectiveDate AS date)) AS NextAnniversary
                    ) AS nextCycle
                    CROSS APPLY (
                        SELECT CASE f.[Type]
                            WHEN N'Q1R' THEN DATEADD(day, 90, f.TargetEffectiveDate)
                            WHEN N'Q2R' THEN DATEADD(day, 180, f.TargetEffectiveDate)
                            WHEN N'Q3R' THEN DATEADD(day, 270, f.TargetEffectiveDate)
                            WHEN N'Q4R' THEN DATEADD(day, -s.Q4RDaysBeforeAnniversary, nextCycle.NextAnniversary)
                            WHEN N'PCP' THEN DATEADD(day, -s.PcpDaysBeforeAnniversary, nextCycle.NextAnniversary)
                            WHEN N'ComprehensiveAssessment' THEN DATEADD(day, -s.CompAssessmentDaysBeforeAnniversary, nextCycle.NextAnniversary)
                            WHEN N'Reclassification' THEN DATEADD(day, -s.ReclassificationDaysBeforeAnniversary, nextCycle.NextAnniversary)
                            WHEN N'SafetyPlan' THEN DATEADD(day, -s.SafetyPlanDaysBeforeAnniversary, nextCycle.NextAnniversary)
                            WHEN N'PrivacyPractices' THEN DATEADD(day, -s.PrivacyPracticesDaysBeforeAnniversary, nextCycle.NextAnniversary)
                            WHEN N'Release_Agency' THEN DATEADD(day, -s.ReleaseAgencyDaysBeforeAnniversary, nextCycle.NextAnniversary)
                            WHEN N'Release_DHHS' THEN DATEADD(day, -s.ReleaseDhhsDaysBeforeAnniversary, nextCycle.NextAnniversary)
                            WHEN N'Release_Medical' THEN DATEADD(day, -s.ReleaseMedicalDaysBeforeAnniversary, nextCycle.NextAnniversary)
                        END AS ExpectedDueDate
                    ) AS expected
                    WHERE (
                            f.[Type] = N'ComprehensiveAssessment'
                            AND CAST(f.DueDate AS date) NOT IN (
                                expected.ExpectedDueDate,
                                DATEADD(day, -60, nextCycle.NextAnniversary),
                                DATEADD(day, -120, nextCycle.NextAnniversary))
                          )
                       OR (
                            f.[Type] <> N'ComprehensiveAssessment'
                            AND CAST(f.DueDate AS date) <> expected.ExpectedDueDate
                          )
                )
                    THROW 50000, 'A form deadline does not match the documented post-backfill legacy calculator. Reconcile the anomalous row without changing completion/opening/attestation evidence, then retry.', 1;

                -- Offset-zero annual rows are observationally ambiguous by
                -- themselves: before the June repair, an annual deadline on an
                -- anniversary could be either a cycle start or a later cycle end.
                -- A same-cycle Q4 row whose configured offset is not the old
                -- hard-coded one-day value is the positive witness that the
                -- transactional post-backfill shape is present. Refuse annual
                -- conversion when that witness is unavailable.
                IF EXISTS (
                    SELECT 1
                    FROM dbo.Forms AS annualForm
                    INNER JOIN dbo.People AS p ON p.Id = annualForm.PersonId
                    INNER JOIN dbo.Settings AS s ON s.AgencyId = p.AgencyId
                    WHERE annualForm.[Type] NOT IN (N'Q1R', N'Q2R', N'Q3R', N'Q4R')
                      AND (
                          s.Q4RDaysBeforeAnniversary = 1
                          OR NOT EXISTS (
                              SELECT 1
                              FROM dbo.Forms AS q4
                              WHERE q4.PersonId = annualForm.PersonId
                                AND q4.[Type] = N'Q4R'
                                AND q4.TargetEffectiveDate = annualForm.TargetEffectiveDate
                          )
                      )
                )
                    THROW 50000, 'Cannot prove that annual rows have the post-2026-06-29 deadline shape. A non-legacy same-target Q4 witness is required; reconcile or document the source rows before retrying.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM dbo.Forms
                    GROUP BY PersonId, [Type], TargetEffectiveDate
                    HAVING COUNT(*) > 1
                )
                    THROW 50000, 'dbo.Forms contains duplicate (PersonId, Type, TargetEffectiveDate) obligations. Merge the retained records without losing attestations, then retry.', 1;
                """);

            // Correct only values that exactly match known legacy defaults. Custom
            // agency choices survive. AuditEvent has no actor FK and already uses
            // actor 0 for migration/system work, so this records the change without
            // pretending that an administrator made it.
            migrationBuilder.Sql("""
                INSERT INTO dbo.AuditEvents
                    (EventId, AgencyId, ActorUserId, [Action], ResourceType, ResourceId,
                     OccurredAtUtc, CorrelationId, MetadataJson)
                SELECT
                    NEWID(),
                    s.AgencyId,
                    0,
                    N'settings.compliance-defaults-corrected',
                    N'Settings',
                    CONVERT(nvarchar(100), s.Id),
                    SYSUTCDATETIME(),
                    N'migration-CorrectAnnualComplianceAndBillingPolicy',
                    CONCAT(
                        N'{"migration":"CorrectAnnualComplianceAndBillingPolicy","recognizedLegacyValues":{',
                        N'"billingMask31":', CASE WHEN s.BillingComplianceRequirements = 31 THEN N'true' ELSE N'false' END, N',',
                        N'"reclassOpen15":', CASE WHEN s.ReclassificationOpenDaysBefore = 15 THEN N'true' ELSE N'false' END, N',',
                        N'"safetyOpen60":', CASE WHEN s.SafetyPlanOpenDaysBefore = 60 THEN N'true' ELSE N'false' END, N',',
                        N'"privacyOpen30":', CASE WHEN s.PrivacyPracticesOpenDaysBefore = 30 THEN N'true' ELSE N'false' END, N',',
                        N'"agencyReleaseOpen30":', CASE WHEN s.ReleaseAgencyOpenDaysBefore = 30 THEN N'true' ELSE N'false' END, N',',
                        N'"dhhsReleaseOpen30":', CASE WHEN s.ReleaseDhhsOpenDaysBefore = 30 THEN N'true' ELSE N'false' END, N',',
                        N'"medicalReleaseOpen30":', CASE WHEN s.ReleaseMedicalOpenDaysBefore = 30 THEN N'true' ELSE N'false' END, N',',
                        N'"assessmentDue60Or120":', CASE WHEN s.CompAssessmentDaysBeforeAnniversary IN (60, 120) THEN N'true' ELSE N'false' END,
                        N'}}')
                FROM dbo.Settings AS s
                WHERE s.BillingComplianceRequirements = 31
                   OR s.ReclassificationOpenDaysBefore = 15
                   OR s.SafetyPlanOpenDaysBefore = 60
                   OR s.PrivacyPracticesOpenDaysBefore = 30
                   OR s.ReleaseAgencyOpenDaysBefore = 30
                   OR s.ReleaseDhhsOpenDaysBefore = 30
                   OR s.ReleaseMedicalOpenDaysBefore = 30
                   OR s.CompAssessmentDaysBeforeAnniversary IN (60, 120);

                UPDATE dbo.Settings
                   SET BillingComplianceRequirements =
                           CASE WHEN BillingComplianceRequirements = 31 THEN 7 ELSE BillingComplianceRequirements END,
                       ReclassificationOpenDaysBefore =
                           CASE WHEN ReclassificationOpenDaysBefore = 15 THEN 60 ELSE ReclassificationOpenDaysBefore END,
                       SafetyPlanOpenDaysBefore =
                           CASE WHEN SafetyPlanOpenDaysBefore = 60 THEN 90 ELSE SafetyPlanOpenDaysBefore END,
                       PrivacyPracticesOpenDaysBefore =
                           CASE WHEN PrivacyPracticesOpenDaysBefore = 30 THEN 90 ELSE PrivacyPracticesOpenDaysBefore END,
                       ReleaseAgencyOpenDaysBefore =
                           CASE WHEN ReleaseAgencyOpenDaysBefore = 30 THEN 90 ELSE ReleaseAgencyOpenDaysBefore END,
                       ReleaseDhhsOpenDaysBefore =
                           CASE WHEN ReleaseDhhsOpenDaysBefore = 30 THEN 90 ELSE ReleaseDhhsOpenDaysBefore END,
                       ReleaseMedicalOpenDaysBefore =
                           CASE WHEN ReleaseMedicalOpenDaysBefore = 30 THEN 90 ELSE ReleaseMedicalOpenDaysBefore END,
                       CompAssessmentDaysBeforeAnniversary =
                           CASE WHEN CompAssessmentDaysBeforeAnniversary IN (60, 120) THEN 90 ELSE CompAssessmentDaysBeforeAnniversary END
                 WHERE BillingComplianceRequirements = 31
                    OR ReclassificationOpenDaysBefore = 15
                    OR SafetyPlanOpenDaysBefore = 60
                    OR PrivacyPracticesOpenDaysBefore = 30
                    OR ReleaseAgencyOpenDaysBefore = 30
                    OR ReleaseDhhsOpenDaysBefore = 30
                    OR ReleaseMedicalOpenDaysBefore = 30
                    OR CompAssessmentDaysBeforeAnniversary IN (60, 120);

                -- Now that target identity and corrected settings are authoritative,
                -- derive the new deadline once into a batch-local table. Every use
                -- below (identity audit, deadline audit, update, and verification)
                -- consumes these exact same values so the steps cannot drift.
                CREATE TABLE #CorrectedSchedule (
                    Id int NOT NULL PRIMARY KEY,
                    AgencyId int NOT NULL,
                    [Type] nvarchar(64) NOT NULL,
                    TargetEffectiveDate date NOT NULL,
                    OldDueDate date NOT NULL,
                    NewDueDate date NOT NULL
                );

                INSERT INTO #CorrectedSchedule
                    (Id, AgencyId, [Type], TargetEffectiveDate, OldDueDate, NewDueDate)
                SELECT
                    f.Id,
                    p.AgencyId,
                    f.[Type],
                    f.TargetEffectiveDate,
                    CAST(f.DueDate AS date),
                    CASE f.[Type]
                        WHEN N'Q1R' THEN DATEADD(day, 90, f.TargetEffectiveDate)
                        WHEN N'Q2R' THEN DATEADD(day, 180, f.TargetEffectiveDate)
                        WHEN N'Q3R' THEN DATEADD(day, 270, f.TargetEffectiveDate)
                        WHEN N'Q4R' THEN DATEADD(day, 360, f.TargetEffectiveDate)
                        WHEN N'PCP' THEN DATEADD(day, -s.PcpDaysBeforeAnniversary, f.TargetEffectiveDate)
                        WHEN N'ComprehensiveAssessment' THEN DATEADD(day, -s.CompAssessmentDaysBeforeAnniversary, f.TargetEffectiveDate)
                        WHEN N'Reclassification' THEN DATEADD(day, -s.ReclassificationDaysBeforeAnniversary, f.TargetEffectiveDate)
                        WHEN N'SafetyPlan' THEN DATEADD(day, -s.SafetyPlanDaysBeforeAnniversary, f.TargetEffectiveDate)
                        WHEN N'PrivacyPractices' THEN DATEADD(day, -s.PrivacyPracticesDaysBeforeAnniversary, f.TargetEffectiveDate)
                        WHEN N'Release_Agency' THEN DATEADD(day, -s.ReleaseAgencyDaysBeforeAnniversary, f.TargetEffectiveDate)
                        WHEN N'Release_DHHS' THEN DATEADD(day, -s.ReleaseDhhsDaysBeforeAnniversary, f.TargetEffectiveDate)
                        WHEN N'Release_Medical' THEN DATEADD(day, -s.ReleaseMedicalDaysBeforeAnniversary, f.TargetEffectiveDate)
                    END
                FROM dbo.Forms AS f
                INNER JOIN dbo.People AS p ON p.Id = f.PersonId
                INNER JOIN dbo.Settings AS s ON s.AgencyId = p.AgencyId;

                -- TargetEffectiveDate is a new official record identity even when a
                -- review's deadline happens not to move. Record every assignment.
                INSERT INTO dbo.AuditEvents
                    (EventId, AgencyId, ActorUserId, [Action], ResourceType, ResourceId,
                     OccurredAtUtc, CorrelationId, MetadataJson)
                SELECT
                    NEWID(),
                    d.AgencyId,
                    0,
                    N'form.target-effective-date-assigned',
                    N'Form',
                    CONVERT(nvarchar(100), d.Id),
                    SYSUTCDATETIME(),
                    N'migration-CorrectAnnualComplianceAndBillingPolicy',
                    CONCAT(
                        N'{"migration":"CorrectAnnualComplianceAndBillingPolicy","formType":"', d.[Type],
                        N'","targetEffectiveDate":"', CONVERT(char(10), d.TargetEffectiveDate, 23),
                        N'","legacyDueDate":"', CONVERT(char(10), d.OldDueDate, 23), N'"}')
                FROM #CorrectedSchedule AS d;

                -- Preserve each changed historical billing boundary separately.
                INSERT INTO dbo.AuditEvents
                    (EventId, AgencyId, ActorUserId, [Action], ResourceType, ResourceId,
                     OccurredAtUtc, CorrelationId, MetadataJson)
                SELECT
                    NEWID(),
                    d.AgencyId,
                    0,
                    N'form.deadline-corrected',
                    N'Form',
                    CONVERT(nvarchar(100), d.Id),
                    SYSUTCDATETIME(),
                    N'migration-CorrectAnnualComplianceAndBillingPolicy',
                    CONCAT(
                        N'{"migration":"CorrectAnnualComplianceAndBillingPolicy","formType":"', d.[Type],
                        N'","targetEffectiveDate":"', CONVERT(char(10), d.TargetEffectiveDate, 23),
                        N'","oldDueDate":"', CONVERT(char(10), d.OldDueDate, 23),
                        N'","newDueDate":"', CONVERT(char(10), d.NewDueDate, 23), N'"}')
                FROM #CorrectedSchedule AS d
                WHERE d.OldDueDate <> d.NewDueDate;

                UPDATE f
                   SET DueDate = d.NewDueDate
                  FROM dbo.Forms AS f
                  INNER JOIN #CorrectedSchedule AS d ON d.Id = f.Id
                 WHERE CAST(f.DueDate AS date) <> d.NewDueDate;

                IF EXISTS (
                    SELECT 1
                    FROM dbo.Forms AS f
                    INNER JOIN #CorrectedSchedule AS expected ON expected.Id = f.Id
                    WHERE CAST(f.DueDate AS date) <> expected.NewDueDate
                )
                    THROW 50000, 'Annual deadline correction did not produce the authoritative target-based schedule. The transaction has been aborted.', 1;

                DROP TABLE #CorrectedSchedule;
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "TargetEffectiveDate",
                table: "Forms",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "BillingCompliancePolicyVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    EffectiveOn = table.Column<DateTime>(type: "date", nullable: false),
                    Requirements = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCompliancePolicyVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingCompliancePolicyVersions_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingCompliancePolicyVersions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillingComplianceRecoveryDecisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    AdminUserId = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    AttestationConfirmed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingComplianceRecoveryDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingComplianceRecoveryDecisions_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingComplianceRecoveryDecisions_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingComplianceRecoveryDecisions_Users_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseObligations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObligationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    StableKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TargetEffectiveDate = table.Column<DateTime>(type: "date", nullable: false),
                    AssignmentKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RecipientProviderId = table.Column<int>(type: "int", nullable: true),
                    RecipientDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AvailableOn = table.Column<DateTime>(type: "date", nullable: false),
                    DueOn = table.Column<DateTime>(type: "date", nullable: false),
                    AppliesFromOn = table.Column<DateTime>(type: "date", nullable: false),
                    RetiredOn = table.Column<DateTime>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RetirementRecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseObligations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseObligations_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReleaseObligations_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseObligations_Providers_RecipientProviderId",
                        column: x => x.RecipientProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillingComplianceRecoveryNotes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BillingComplianceRecoveryDecisionId = table.Column<long>(type: "bigint", nullable: false),
                    NoteId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingComplianceRecoveryNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingComplianceRecoveryNotes_BillingComplianceRecoveryDecisions_BillingComplianceRecoveryDecisionId",
                        column: x => x.BillingComplianceRecoveryDecisionId,
                        principalTable: "BillingComplianceRecoveryDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillingComplianceRecoveryNotes_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillingComplianceRecoveryObligations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BillingComplianceRecoveryDecisionId = table.Column<long>(type: "bigint", nullable: false),
                    ObligationId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DueDate = table.Column<DateTime>(type: "date", nullable: false),
                    CompletedDate = table.Column<DateTime>(type: "date", nullable: false),
                    EvidenceId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingComplianceRecoveryObligations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingComplianceRecoveryObligations_BillingComplianceRecoveryDecisions_BillingComplianceRecoveryDecisionId",
                        column: x => x.BillingComplianceRecoveryDecisionId,
                        principalTable: "BillingComplianceRecoveryDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseAuthorizationEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReleaseObligationId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OccurredOn = table.Column<DateTime>(type: "date", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseAuthorizationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseAuthorizationEvents_ReleaseObligations_ReleaseObligationId",
                        column: x => x.ReleaseObligationId,
                        principalTable: "ReleaseObligations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseAuthorizationEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseObligationAttestations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReleaseObligationId = table.Column<long>(type: "bigint", nullable: false),
                    CompletedOn = table.Column<DateTime>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ActorKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    SignerCapacity = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SignatureCompletionId = table.Column<int>(type: "int", nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseObligationAttestations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseObligationAttestations_ReleaseObligations_ReleaseObligationId",
                        column: x => x.ReleaseObligationId,
                        principalTable: "ReleaseObligations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseObligationAttestations_SignatureCompletions_SignatureCompletionId",
                        column: x => x.SignatureCompletionId,
                        principalTable: "SignatureCompletions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReleaseObligationAttestations_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SignatureComplianceProjections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    CompletionId = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    FrozenDocumentId = table.Column<int>(type: "int", nullable: false),
                    DocumentArtifactId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    DocumentKind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TargetKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<long>(type: "bigint", nullable: false),
                    FormAttestationId = table.Column<long>(type: "bigint", nullable: true),
                    ReleaseObligationAttestationId = table.Column<long>(type: "bigint", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedOn = table.Column<DateTime>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SignerCapacity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SignerContactId = table.Column<int>(type: "int", nullable: true),
                    ExistingCompletedOn = table.Column<DateTime>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureComplianceProjections", x => x.Id);
                    table.CheckConstraint("CK_SignatureComplianceProjections_Outcome", "[Outcome] IN ('Applied','AlreadySatisfied')");
                    table.CheckConstraint("CK_SignatureComplianceProjections_Result", "([Outcome] = 'Applied' AND [ExistingCompletedOn] IS NULL AND (([TargetKind] = 'Form' AND [FormAttestationId] IS NOT NULL AND [ReleaseObligationAttestationId] IS NULL) OR ([TargetKind] = 'ReleaseObligation' AND [ReleaseObligationAttestationId] IS NOT NULL AND [FormAttestationId] IS NULL))) OR ([Outcome] = 'AlreadySatisfied' AND [FormAttestationId] IS NULL AND [ReleaseObligationAttestationId] IS NULL AND [ExistingCompletedOn] IS NOT NULL)");
                    table.CheckConstraint("CK_SignatureComplianceProjections_Signer", "[SignerCapacity] IN ('Consumer','Guardian')");
                    table.CheckConstraint("CK_SignatureComplianceProjections_Target", "[TargetKind] IN ('Form','ReleaseObligation') AND [TargetId] > 0");
                    table.CheckConstraint("CK_SignatureComplianceProjections_Time", "[RecordedAtUtc] >= [SignedAtUtc]");
                    table.ForeignKey(
                        name: "FK_SignatureComplianceProjections_DocumentArtifacts_AgencyId_PersonId_DocumentArtifactId",
                        columns: x => new { x.AgencyId, x.PersonId, x.DocumentArtifactId },
                        principalTable: "DocumentArtifacts",
                        principalColumns: new[] { "AgencyId", "PersonId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SignatureComplianceProjections_FormAttestations_FormAttestationId",
                        column: x => x.FormAttestationId,
                        principalTable: "FormAttestations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SignatureComplianceProjections_ReleaseObligationAttestations_ReleaseObligationAttestationId",
                        column: x => x.ReleaseObligationAttestationId,
                        principalTable: "ReleaseObligationAttestations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SignatureComplianceProjections_SignatureCompletions_AgencyId_RequestId_CompletionId",
                        columns: x => new { x.AgencyId, x.RequestId, x.CompletionId },
                        principalTable: "SignatureCompletions",
                        principalColumns: new[] { "AgencyId", "RequestId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Forms_PersonId_Type_TargetEffectiveDate",
                table: "Forms",
                columns: new[] { "PersonId", "Type", "TargetEffectiveDate" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Forms_TargetEffectiveDate_Valid",
                table: "Forms",
                sql: "[TargetEffectiveDate] >= '1900-01-01'");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentArtifacts_OneLivePerCycle",
                table: "DocumentArtifacts",
                columns: new[] { "PersonId", "Kind", "CycleStart" },
                unique: true,
                filter: "[ReleaseObligationId] IS NULL AND [SupersededByArtifactId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentArtifacts_OneLivePerReleaseObligation",
                table: "DocumentArtifacts",
                columns: new[] { "ReleaseObligationId", "Kind" },
                unique: true,
                filter: "[ReleaseObligationId] IS NOT NULL AND [SupersededByArtifactId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCompliancePolicyVersions_AgencyId_EffectiveOn_Id",
                table: "BillingCompliancePolicyVersions",
                columns: new[] { "AgencyId", "EffectiveOn", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCompliancePolicyVersions_CreatedByUserId",
                table: "BillingCompliancePolicyVersions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCompliancePolicyVersions_VersionId",
                table: "BillingCompliancePolicyVersions",
                column: "VersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingComplianceRecoveryDecisions_AdminUserId",
                table: "BillingComplianceRecoveryDecisions",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingComplianceRecoveryDecisions_AgencyId_PersonId_RecordedAtUtc",
                table: "BillingComplianceRecoveryDecisions",
                columns: new[] { "AgencyId", "PersonId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingComplianceRecoveryDecisions_DecisionId",
                table: "BillingComplianceRecoveryDecisions",
                column: "DecisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingComplianceRecoveryDecisions_PersonId",
                table: "BillingComplianceRecoveryDecisions",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingComplianceRecoveryNotes_BillingComplianceRecoveryDecisionId_NoteId",
                table: "BillingComplianceRecoveryNotes",
                columns: new[] { "BillingComplianceRecoveryDecisionId", "NoteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingComplianceRecoveryNotes_NoteId",
                table: "BillingComplianceRecoveryNotes",
                column: "NoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingComplianceRecoveryObligations_BillingComplianceRecoveryDecisionId_ObligationId",
                table: "BillingComplianceRecoveryObligations",
                columns: new[] { "BillingComplianceRecoveryDecisionId", "ObligationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseAuthorizationEvents_ActorUserId",
                table: "ReleaseAuthorizationEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseAuthorizationEvents_ReleaseObligationId_RecordedAtUtc",
                table: "ReleaseAuthorizationEvents",
                columns: new[] { "ReleaseObligationId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseObligationAttestations_ActorUserId",
                table: "ReleaseObligationAttestations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseObligationAttestations_ReleaseObligationId_RecordedAtUtc",
                table: "ReleaseObligationAttestations",
                columns: new[] { "ReleaseObligationId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseObligationAttestations_SignatureCompletionId",
                table: "ReleaseObligationAttestations",
                column: "SignatureCompletionId",
                unique: true,
                filter: "[SignatureCompletionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseObligations_AgencyId_PersonId_TargetEffectiveDate",
                table: "ReleaseObligations",
                columns: new[] { "AgencyId", "PersonId", "TargetEffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseObligations_ObligationId",
                table: "ReleaseObligations",
                column: "ObligationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseObligations_PersonId_StableKey",
                table: "ReleaseObligations",
                columns: new[] { "PersonId", "StableKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseObligations_RecipientProviderId",
                table: "ReleaseObligations",
                column: "RecipientProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureComplianceProjections_AgencyId_PersonId_DocumentArtifactId",
                table: "SignatureComplianceProjections",
                columns: new[] { "AgencyId", "PersonId", "DocumentArtifactId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureComplianceProjections_AgencyId_PersonId_TargetKind_TargetId",
                table: "SignatureComplianceProjections",
                columns: new[] { "AgencyId", "PersonId", "TargetKind", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureComplianceProjections_AgencyId_RequestId_CompletionId",
                table: "SignatureComplianceProjections",
                columns: new[] { "AgencyId", "RequestId", "CompletionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureComplianceProjections_CompletionId",
                table: "SignatureComplianceProjections",
                column: "CompletionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureComplianceProjections_FormAttestationId",
                table: "SignatureComplianceProjections",
                column: "FormAttestationId",
                unique: true,
                filter: "[FormAttestationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureComplianceProjections_ReleaseObligationAttestationId",
                table: "SignatureComplianceProjections",
                column: "ReleaseObligationAttestationId",
                unique: true,
                filter: "[ReleaseObligationAttestationId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentArtifacts_ReleaseObligations_ReleaseObligationId",
                table: "DocumentArtifacts",
                column: "ReleaseObligationId",
                principalTable: "ReleaseObligations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The prior schema cannot represent any of these facts.  Permit a clean
            // rollback immediately after a truly empty/no-change deployment, but
            // never erase policy, recovery, release, signature-projection,
            // exception, assignment, target identity, corrected deadline, or
            // normalized-setting evidence merely because an older binary was
            // deployed. Legacy due dates and 60-vs-120 CA settings cannot be
            // reconstructed losslessly.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.Forms)
                   OR EXISTS (
                       SELECT 1 FROM dbo.AuditEvents
                       WHERE CorrelationId = N'migration-CorrectAnnualComplianceAndBillingPolicy')
                   OR EXISTS (SELECT 1 FROM dbo.BillingCompliancePolicyVersions)
                   OR EXISTS (SELECT 1 FROM dbo.BillingComplianceRecoveryDecisions)
                   OR EXISTS (SELECT 1 FROM dbo.ReleaseObligations)
                   OR EXISTS (SELECT 1 FROM dbo.SignatureComplianceProjections)
                   OR EXISTS (
                       SELECT 1 FROM dbo.Notes
                       WHERE OverrideAttestationConfirmed = 1
                          OR OverrideObligationIdsJson IS NOT NULL)
                   OR EXISTS (
                       SELECT 1 FROM dbo.PersonProviders
                       WHERE AssignmentKnownOn IS NOT NULL)
                   OR EXISTS (
                       SELECT 1 FROM dbo.Settings
                       WHERE AllowPastBillingPolicyEffectiveDates = 1)
                    THROW 51001, 'Cannot safely downgrade: the corrected compliance schema contains retained records or settings that the prior schema cannot represent.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentArtifacts_ReleaseObligations_ReleaseObligationId",
                table: "DocumentArtifacts");

            migrationBuilder.DropTable(
                name: "BillingCompliancePolicyVersions");

            migrationBuilder.DropTable(
                name: "BillingComplianceRecoveryNotes");

            migrationBuilder.DropTable(
                name: "BillingComplianceRecoveryObligations");

            migrationBuilder.DropTable(
                name: "ReleaseAuthorizationEvents");

            migrationBuilder.DropTable(
                name: "SignatureComplianceProjections");

            migrationBuilder.DropTable(
                name: "BillingComplianceRecoveryDecisions");

            migrationBuilder.DropTable(
                name: "ReleaseObligationAttestations");

            migrationBuilder.DropTable(
                name: "ReleaseObligations");

            migrationBuilder.DropIndex(
                name: "IX_Forms_PersonId_Type_TargetEffectiveDate",
                table: "Forms");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Forms_TargetEffectiveDate_Valid",
                table: "Forms");

            migrationBuilder.DropIndex(
                name: "IX_DocumentArtifacts_OneLivePerCycle",
                table: "DocumentArtifacts");

            migrationBuilder.DropIndex(
                name: "IX_DocumentArtifacts_OneLivePerReleaseObligation",
                table: "DocumentArtifacts");

            migrationBuilder.DropColumn(
                name: "AllowPastBillingPolicyEffectiveDates",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "AssignmentKnownOn",
                table: "PersonProviders");

            migrationBuilder.DropColumn(
                name: "OverrideAttestationConfirmed",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "OverrideObligationIdsJson",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "TargetEffectiveDate",
                table: "Forms");

            migrationBuilder.DropColumn(
                name: "ReleaseObligationId",
                table: "DocumentArtifacts");

            migrationBuilder.AlterColumn<int>(
                name: "BillingComplianceRequirements",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 31,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 7);

            migrationBuilder.AlterColumn<string>(
                name: "OverrideReason",
                table: "Notes",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Forms_PersonId_Type_DueDate",
                table: "Forms",
                columns: new[] { "PersonId", "Type", "DueDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentArtifacts_OneLivePerCycle",
                table: "DocumentArtifacts",
                columns: new[] { "PersonId", "Kind", "CycleStart" },
                unique: true,
                filter: "[SupersededByArtifactId] IS NULL");
        }
    }
}
