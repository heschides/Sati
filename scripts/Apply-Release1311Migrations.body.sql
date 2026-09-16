BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    DROP INDEX [IX_Forms_PersonId_Type_DueDate] ON [Forms];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    DROP INDEX [IX_DocumentArtifacts_OneLivePerCycle] ON [DocumentArtifacts];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Settings]') AND [c].[name] = N'BillingComplianceRequirements');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Settings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Settings] ADD DEFAULT 7 FOR [BillingComplianceRequirements];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    ALTER TABLE [Settings] ADD [AllowPastBillingPolicyEffectiveDates] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    ALTER TABLE [PersonProviders] ADD [AssignmentKnownOn] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Notes]') AND [c].[name] = N'OverrideReason');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Notes] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Notes] ALTER COLUMN [OverrideReason] nvarchar(4000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    ALTER TABLE [Notes] ADD [OverrideAttestationConfirmed] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    ALTER TABLE [Notes] ADD [OverrideObligationIdsJson] nvarchar(4000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    ALTER TABLE [Forms] ADD [TargetEffectiveDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    ALTER TABLE [DocumentArtifacts] ADD [ReleaseObligationId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Forms]') AND [c].[name] = N'TargetEffectiveDate');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Forms] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [Forms] ALTER COLUMN [TargetEffectiveDate] date NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE TABLE [BillingCompliancePolicyVersions] (
        [Id] bigint NOT NULL IDENTITY,
        [VersionId] uniqueidentifier NOT NULL,
        [AgencyId] int NOT NULL,
        [EffectiveOn] date NOT NULL,
        [Requirements] int NOT NULL,
        [CreatedByUserId] int NOT NULL,
        [RecordedAtUtc] datetime2 NOT NULL,
        [Explanation] nvarchar(1000) NULL,
        CONSTRAINT [PK_BillingCompliancePolicyVersions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BillingCompliancePolicyVersions_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BillingCompliancePolicyVersions_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE TABLE [BillingComplianceRecoveryDecisions] (
        [Id] bigint NOT NULL IDENTITY,
        [DecisionId] uniqueidentifier NOT NULL,
        [AgencyId] int NOT NULL,
        [PersonId] int NOT NULL,
        [AdminUserId] int NOT NULL,
        [RecordedAtUtc] datetime2 NOT NULL,
        [Explanation] nvarchar(4000) NOT NULL,
        [AttestationConfirmed] bit NOT NULL,
        CONSTRAINT [PK_BillingComplianceRecoveryDecisions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BillingComplianceRecoveryDecisions_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BillingComplianceRecoveryDecisions_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BillingComplianceRecoveryDecisions_Users_AdminUserId] FOREIGN KEY ([AdminUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE TABLE [ReleaseObligations] (
        [Id] bigint NOT NULL IDENTITY,
        [ObligationId] uniqueidentifier NOT NULL,
        [AgencyId] int NOT NULL,
        [PersonId] int NOT NULL,
        [StableKey] nvarchar(300) NOT NULL,
        [Category] nvarchar(20) NOT NULL,
        [Trigger] nvarchar(30) NOT NULL,
        [TargetEffectiveDate] date NOT NULL,
        [AssignmentKey] nvarchar(100) NULL,
        [RecipientProviderId] int NULL,
        [RecipientDisplayName] nvarchar(200) NULL,
        [AvailableOn] date NOT NULL,
        [DueOn] date NOT NULL,
        [AppliesFromOn] date NOT NULL,
        [RetiredOn] date NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [RetirementRecordedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_ReleaseObligations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReleaseObligations_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ReleaseObligations_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ReleaseObligations_Providers_RecipientProviderId] FOREIGN KEY ([RecipientProviderId]) REFERENCES [Providers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE TABLE [BillingComplianceRecoveryNotes] (
        [Id] bigint NOT NULL IDENTITY,
        [BillingComplianceRecoveryDecisionId] bigint NOT NULL,
        [NoteId] int NOT NULL,
        CONSTRAINT [PK_BillingComplianceRecoveryNotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BillingComplianceRecoveryNotes_BillingComplianceRecoveryDecisions_BillingComplianceRecoveryDecisionId] FOREIGN KEY ([BillingComplianceRecoveryDecisionId]) REFERENCES [BillingComplianceRecoveryDecisions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_BillingComplianceRecoveryNotes_Notes_NoteId] FOREIGN KEY ([NoteId]) REFERENCES [Notes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE TABLE [BillingComplianceRecoveryObligations] (
        [Id] bigint NOT NULL IDENTITY,
        [BillingComplianceRecoveryDecisionId] bigint NOT NULL,
        [ObligationId] nvarchar(500) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [DueDate] date NOT NULL,
        [CompletedDate] date NOT NULL,
        [EvidenceId] nvarchar(500) NOT NULL,
        CONSTRAINT [PK_BillingComplianceRecoveryObligations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BillingComplianceRecoveryObligations_BillingComplianceRecoveryDecisions_BillingComplianceRecoveryDecisionId] FOREIGN KEY ([BillingComplianceRecoveryDecisionId]) REFERENCES [BillingComplianceRecoveryDecisions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE TABLE [ReleaseAuthorizationEvents] (
        [Id] bigint NOT NULL IDENTITY,
        [ReleaseObligationId] bigint NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [OccurredOn] date NOT NULL,
        [ActorUserId] int NOT NULL,
        [RecordedAtUtc] datetime2 NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        CONSTRAINT [PK_ReleaseAuthorizationEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReleaseAuthorizationEvents_ReleaseObligations_ReleaseObligationId] FOREIGN KEY ([ReleaseObligationId]) REFERENCES [ReleaseObligations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ReleaseAuthorizationEvents_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE TABLE [ReleaseObligationAttestations] (
        [Id] bigint NOT NULL IDENTITY,
        [ReleaseObligationId] bigint NOT NULL,
        [CompletedOn] date NOT NULL,
        [Source] nvarchar(30) NOT NULL,
        [ActorKind] nvarchar(20) NOT NULL,
        [ActorUserId] int NULL,
        [SignerCapacity] nvarchar(30) NULL,
        [SignatureCompletionId] int NULL,
        [RecordedAtUtc] datetime2 NOT NULL,
        [Reason] nvarchar(500) NULL,
        CONSTRAINT [PK_ReleaseObligationAttestations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReleaseObligationAttestations_ReleaseObligations_ReleaseObligationId] FOREIGN KEY ([ReleaseObligationId]) REFERENCES [ReleaseObligations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ReleaseObligationAttestations_SignatureCompletions_SignatureCompletionId] FOREIGN KEY ([SignatureCompletionId]) REFERENCES [SignatureCompletions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ReleaseObligationAttestations_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE TABLE [SignatureComplianceProjections] (
        [Id] bigint NOT NULL IDENTITY,
        [AgencyId] int NOT NULL,
        [CompletionId] int NOT NULL,
        [RequestId] int NOT NULL,
        [FrozenDocumentId] int NOT NULL,
        [DocumentArtifactId] int NOT NULL,
        [PersonId] int NOT NULL,
        [DocumentKind] nvarchar(40) NOT NULL,
        [TargetKind] nvarchar(32) NOT NULL,
        [TargetId] bigint NOT NULL,
        [FormAttestationId] bigint NULL,
        [ReleaseObligationAttestationId] bigint NULL,
        [Outcome] nvarchar(32) NOT NULL,
        [SignedAtUtc] datetime2 NOT NULL,
        [CompletedOn] date NOT NULL,
        [RecordedAtUtc] datetime2 NOT NULL,
        [SignerCapacity] nvarchar(32) NOT NULL,
        [SignerContactId] int NULL,
        [ExistingCompletedOn] date NULL,
        CONSTRAINT [PK_SignatureComplianceProjections] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SignatureComplianceProjections_Outcome] CHECK ([Outcome] IN ('Applied','AlreadySatisfied')),
        CONSTRAINT [CK_SignatureComplianceProjections_Result] CHECK (([Outcome] = 'Applied' AND [ExistingCompletedOn] IS NULL AND (([TargetKind] = 'Form' AND [FormAttestationId] IS NOT NULL AND [ReleaseObligationAttestationId] IS NULL) OR ([TargetKind] = 'ReleaseObligation' AND [ReleaseObligationAttestationId] IS NOT NULL AND [FormAttestationId] IS NULL))) OR ([Outcome] = 'AlreadySatisfied' AND [FormAttestationId] IS NULL AND [ReleaseObligationAttestationId] IS NULL AND [ExistingCompletedOn] IS NOT NULL)),
        CONSTRAINT [CK_SignatureComplianceProjections_Signer] CHECK ([SignerCapacity] IN ('Consumer','Guardian')),
        CONSTRAINT [CK_SignatureComplianceProjections_Target] CHECK ([TargetKind] IN ('Form','ReleaseObligation') AND [TargetId] > 0),
        CONSTRAINT [CK_SignatureComplianceProjections_Time] CHECK ([RecordedAtUtc] >= [SignedAtUtc]),
        CONSTRAINT [FK_SignatureComplianceProjections_DocumentArtifacts_AgencyId_PersonId_DocumentArtifactId] FOREIGN KEY ([AgencyId], [PersonId], [DocumentArtifactId]) REFERENCES [DocumentArtifacts] ([AgencyId], [PersonId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SignatureComplianceProjections_FormAttestations_FormAttestationId] FOREIGN KEY ([FormAttestationId]) REFERENCES [FormAttestations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SignatureComplianceProjections_ReleaseObligationAttestations_ReleaseObligationAttestationId] FOREIGN KEY ([ReleaseObligationAttestationId]) REFERENCES [ReleaseObligationAttestations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SignatureComplianceProjections_SignatureCompletions_AgencyId_RequestId_CompletionId] FOREIGN KEY ([AgencyId], [RequestId], [CompletionId]) REFERENCES [SignatureCompletions] ([AgencyId], [RequestId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Forms_PersonId_Type_TargetEffectiveDate] ON [Forms] ([PersonId], [Type], [TargetEffectiveDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    EXEC(N'ALTER TABLE [Forms] ADD CONSTRAINT [CK_Forms_TargetEffectiveDate_Valid] CHECK ([TargetEffectiveDate] >= ''1900-01-01'')');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_DocumentArtifacts_OneLivePerCycle] ON [DocumentArtifacts] ([PersonId], [Kind], [CycleStart]) WHERE [ReleaseObligationId] IS NULL AND [SupersededByArtifactId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_DocumentArtifacts_OneLivePerReleaseObligation] ON [DocumentArtifacts] ([ReleaseObligationId], [Kind]) WHERE [ReleaseObligationId] IS NOT NULL AND [SupersededByArtifactId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_BillingCompliancePolicyVersions_AgencyId_EffectiveOn_Id] ON [BillingCompliancePolicyVersions] ([AgencyId], [EffectiveOn], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_BillingCompliancePolicyVersions_CreatedByUserId] ON [BillingCompliancePolicyVersions] ([CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BillingCompliancePolicyVersions_VersionId] ON [BillingCompliancePolicyVersions] ([VersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_BillingComplianceRecoveryDecisions_AdminUserId] ON [BillingComplianceRecoveryDecisions] ([AdminUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_BillingComplianceRecoveryDecisions_AgencyId_PersonId_RecordedAtUtc] ON [BillingComplianceRecoveryDecisions] ([AgencyId], [PersonId], [RecordedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BillingComplianceRecoveryDecisions_DecisionId] ON [BillingComplianceRecoveryDecisions] ([DecisionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_BillingComplianceRecoveryDecisions_PersonId] ON [BillingComplianceRecoveryDecisions] ([PersonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BillingComplianceRecoveryNotes_BillingComplianceRecoveryDecisionId_NoteId] ON [BillingComplianceRecoveryNotes] ([BillingComplianceRecoveryDecisionId], [NoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BillingComplianceRecoveryNotes_NoteId] ON [BillingComplianceRecoveryNotes] ([NoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BillingComplianceRecoveryObligations_BillingComplianceRecoveryDecisionId_ObligationId] ON [BillingComplianceRecoveryObligations] ([BillingComplianceRecoveryDecisionId], [ObligationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_ReleaseAuthorizationEvents_ActorUserId] ON [ReleaseAuthorizationEvents] ([ActorUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_ReleaseAuthorizationEvents_ReleaseObligationId_RecordedAtUtc] ON [ReleaseAuthorizationEvents] ([ReleaseObligationId], [RecordedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_ReleaseObligationAttestations_ActorUserId] ON [ReleaseObligationAttestations] ([ActorUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_ReleaseObligationAttestations_ReleaseObligationId_RecordedAtUtc] ON [ReleaseObligationAttestations] ([ReleaseObligationId], [RecordedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ReleaseObligationAttestations_SignatureCompletionId] ON [ReleaseObligationAttestations] ([SignatureCompletionId]) WHERE [SignatureCompletionId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_ReleaseObligations_AgencyId_PersonId_TargetEffectiveDate] ON [ReleaseObligations] ([AgencyId], [PersonId], [TargetEffectiveDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ReleaseObligations_ObligationId] ON [ReleaseObligations] ([ObligationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ReleaseObligations_PersonId_StableKey] ON [ReleaseObligations] ([PersonId], [StableKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_ReleaseObligations_RecipientProviderId] ON [ReleaseObligations] ([RecipientProviderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_SignatureComplianceProjections_AgencyId_PersonId_DocumentArtifactId] ON [SignatureComplianceProjections] ([AgencyId], [PersonId], [DocumentArtifactId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_SignatureComplianceProjections_AgencyId_PersonId_TargetKind_TargetId] ON [SignatureComplianceProjections] ([AgencyId], [PersonId], [TargetKind], [TargetId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE INDEX [IX_SignatureComplianceProjections_AgencyId_RequestId_CompletionId] ON [SignatureComplianceProjections] ([AgencyId], [RequestId], [CompletionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SignatureComplianceProjections_CompletionId] ON [SignatureComplianceProjections] ([CompletionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SignatureComplianceProjections_FormAttestationId] ON [SignatureComplianceProjections] ([FormAttestationId]) WHERE [FormAttestationId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SignatureComplianceProjections_ReleaseObligationAttestationId] ON [SignatureComplianceProjections] ([ReleaseObligationAttestationId]) WHERE [ReleaseObligationAttestationId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    ALTER TABLE [DocumentArtifacts] ADD CONSTRAINT [FK_DocumentArtifacts_ReleaseObligations_ReleaseObligationId] FOREIGN KEY ([ReleaseObligationId]) REFERENCES [ReleaseObligations] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260915004541_CorrectAnnualComplianceAndBillingPolicy', N'10.0.5');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915013852_AddBillingCompliancePolicyReviewFlags'
)
BEGIN
    CREATE TABLE [BillingCompliancePolicyReviewFlags] (
        [Id] bigint NOT NULL IDENTITY,
        [FlagId] uniqueidentifier NOT NULL,
        [AgencyId] int NOT NULL,
        [PolicyVersionId] bigint NOT NULL,
        [PersonId] int NOT NULL,
        [NoteId] int NOT NULL,
        [ClaimLineId] int NULL,
        [RecordKey] nvarchar(80) NOT NULL,
        [ServiceDate] date NOT NULL,
        [ChangeKind] nvarchar(40) NOT NULL,
        [PreviousBlockingObligationIdsJson] nvarchar(max) NOT NULL,
        [NewBlockingObligationIdsJson] nvarchar(max) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_BillingCompliancePolicyReviewFlags] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BillingCompliancePolicyReviewFlags_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BillingCompliancePolicyReviewFlags_BillingCompliancePolicyVersions_PolicyVersionId] FOREIGN KEY ([PolicyVersionId]) REFERENCES [BillingCompliancePolicyVersions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BillingCompliancePolicyReviewFlags_ClaimLines_ClaimLineId] FOREIGN KEY ([ClaimLineId]) REFERENCES [ClaimLines] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BillingCompliancePolicyReviewFlags_Notes_NoteId] FOREIGN KEY ([NoteId]) REFERENCES [Notes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BillingCompliancePolicyReviewFlags_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915013852_AddBillingCompliancePolicyReviewFlags'
)
BEGIN
    CREATE INDEX [IX_BillingCompliancePolicyReviewFlags_AgencyId_CreatedAtUtc] ON [BillingCompliancePolicyReviewFlags] ([AgencyId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915013852_AddBillingCompliancePolicyReviewFlags'
)
BEGIN
    CREATE INDEX [IX_BillingCompliancePolicyReviewFlags_ClaimLineId] ON [BillingCompliancePolicyReviewFlags] ([ClaimLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915013852_AddBillingCompliancePolicyReviewFlags'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BillingCompliancePolicyReviewFlags_FlagId] ON [BillingCompliancePolicyReviewFlags] ([FlagId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915013852_AddBillingCompliancePolicyReviewFlags'
)
BEGIN
    CREATE INDEX [IX_BillingCompliancePolicyReviewFlags_NoteId] ON [BillingCompliancePolicyReviewFlags] ([NoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915013852_AddBillingCompliancePolicyReviewFlags'
)
BEGIN
    CREATE INDEX [IX_BillingCompliancePolicyReviewFlags_PersonId] ON [BillingCompliancePolicyReviewFlags] ([PersonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915013852_AddBillingCompliancePolicyReviewFlags'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BillingCompliancePolicyReviewFlags_PolicyVersionId_RecordKey] ON [BillingCompliancePolicyReviewFlags] ([PolicyVersionId], [RecordKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915013852_AddBillingCompliancePolicyReviewFlags'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260915013852_AddBillingCompliancePolicyReviewFlags', N'10.0.5');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915153000_AllowSupersedingBillingComplianceRecovery'
)
BEGIN
    DROP INDEX [IX_BillingComplianceRecoveryNotes_NoteId] ON [BillingComplianceRecoveryNotes];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915153000_AllowSupersedingBillingComplianceRecovery'
)
BEGIN
    CREATE INDEX [IX_BillingComplianceRecoveryNotes_NoteId] ON [BillingComplianceRecoveryNotes] ([NoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915153000_AllowSupersedingBillingComplianceRecovery'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260915153000_AllowSupersedingBillingComplianceRecovery', N'10.0.5');
END;

COMMIT;
GO

