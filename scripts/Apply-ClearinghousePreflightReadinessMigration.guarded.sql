-- Reviewed migration 129. Caller owns one transaction; this batch never commits it.
-- Parameters: @expectedDatabase nvarchar(128), @preflightOnly bit.
-- Preflight makes no persistent target changes. A connection-local temp table lets
-- SQL Server canonicalize the expected CHECK without weakening Boolean grouping.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT <> 1 OR XACT_STATE() <> 1
    THROW 53910, 'Caller must own exactly one committable migration transaction.', 1;
IF @preflightOnly IS NULL OR @expectedDatabase IS NULL OR
   DB_NAME() COLLATE Latin1_General_100_BIN2 <> @expectedDatabase COLLATE Latin1_General_100_BIN2 OR
   (@expectedDatabase COLLATE Latin1_General_100_BIN2 <> N'SatiDemo' AND
    (LEN(@expectedDatabase) <> 54 OR
     LEFT(@expectedDatabase,22) COLLATE Latin1_General_100_BIN2 <> N'SatiSyntheticPipeline_' OR
     SUBSTRING(@expectedDatabase,23,32) COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9a-fA-F]%')) OR
   OBJECT_ID(N'dbo.SatiDatabaseIdentity',N'U') IS NULL
    THROW 53910, 'Refusing an environment outside marked Demo or a private synthetic rehearsal.', 1;
IF (SELECT COUNT(*) FROM dbo.SatiDatabaseIdentity) <> 1 OR NOT EXISTS
   (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id=1 AND
    EnvironmentName COLLATE Latin1_General_100_BIN2=N'Demo' AND
    InstanceId IS NOT NULL AND InstanceId<>'00000000-0000-0000-0000-000000000000')
    THROW 53910, 'The database identity does not describe the reviewed Demo environment.', 1;

DECLARE @lockResult int;
EXEC @lockResult=sys.sp_getapplock @Resource=N'SatiDemo.FullReset',
    @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=60000;
IF @lockResult<0 OR COALESCE(APPLOCK_MODE(N'public',N'SatiDemo.FullReset',N'Transaction'),N'')<>N'Exclusive'
    THROW 53913, 'Exclusive Demo reset exclusion is unavailable.', 1;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL
    THROW 53911, 'Migration history is missing.', 1;
DECLARE @migration nvarchar(150)=N'20261009183720_AddClearinghousePreflightReadiness';
DECLARE @predecessors TABLE (MigrationId nvarchar(150) COLLATE Latin1_General_100_BIN2 PRIMARY KEY);
INSERT @predecessors(MigrationId) VALUES
    (N'20260210004007_InitialCreate'),
    (N'20260214004735_AddNoteFields'),
    (N'20260214221707_UpdateNoteFields'),
    (N'20260215141526_UpdateNoteFields2'),
    (N'20260223232745_User'),
    (N'20260311014528_ExplicitModelConfiguration'),
    (N'20260315195240_RenameToSati'),
    (N'20260317225243_AddAbandonedNote Status'),
    (N'20260319235433_AddSettings'),
    (N'20260321000012_AddScratchpad'),
    (N'20260322005212_AddIncentive'),
    (N'20260322212418_ExcludedDates'),
    (N'20260325000833_DayAfterThanksgivingExceptionAdded'),
    (N'20260325224841_AddFormDealinesSettings'),
    (N'20260326234556_AddUserIdToPerson'),
    (N'20260327000752_NoActionDeleteBehavior'),
    (N'20260327001520_FixedIt'),
    (N'20260328212504_AddFormTypeToNote'),
    (N'20260329215120_AddNoteTypeToNote'),
    (N'20260408231437_AddUnitsPerDayToIncentive'),
    (N'20260409130740_AddSupervisorHeirarchy'),
    (N'20260409135922_ConvertRoleToString'),
    (N'20260411165647_MakeEffectiveDateNullable'),
    (N'20260416011235_AddAgencyId'),
    (N'20260418231143_AddCompletedDateToForm'),
    (N'20260419142127_AddBillingAndPersonFields'),
    (N'20260419144051_AddNoteApprovalFields'),
    (N'20260419165405_UnitstoDecimal'),
    (N'20260425184458_AddOpenedDateAndAnniversaryOffsets'),
    (N'20260425223649_AddClaimLineComplianceException'),
    (N'20260426102326_AddPersonGenderAndAgencyBillingFields'),
    (N'20260503122020_AddSupervisorFieldsToNote'),
    (N'20260503144031_SyncModelState'),
    (N'20260504233818_CaseManagerJustification'),
    (N'20260530134142_AddIncentiveExcludedDates'),
    (N'20260531145624_AddClientContactDetailsAndHealthcareSystems'),
    (N'20260628135359_AddQ4RDaysBeforeAnniversary'),
    (N'20260723140242_AddWaiverServiceFlags'),
    (N'20260723141819_AddReviewItem'),
    (N'20260725142003_AddATRequests'),
    (N'20260725150459_AddATRequestSnapshot'),
    (N'20260725153008_AddPassthroughRate'),
    (N'20260729181732_AddPersonJournal'),
    (N'20260805143429_Appointment'),
    (N'20260806133826_AddATRequestItemUrl'),
    (N'20260806194347_AddProviderAndSalesTax'),
    (N'20260807120000_AddComprehensiveAssessments'),
    (N'20260807160000_AddPersonContactsAndVisitDocumentation'),
    (N'20260810130000_AddScratchpadComments'),
    (N'20260812090000_TenantScopeSettingsAndProviders'),
    (N'20260812153000_AddAuditEvents'),
    (N'20260812153100_AddAssessmentRevision'),
    (N'20260812153200_RequireOneClaimLinePerNote'),
    (N'20260812153300_AddPersonLifecycleHistory'),
    (N'20260812213000_ReconcileTenantOwnership'),
    (N'20260812223000_AddNoteRevision'),
    (N'20260812230000_AddAtRequestRevision'),
    (N'20260812233000_AddSettingsRevision'),
    (N'20260812234500_AddScratchpadRevision'),
    (N'20260812235500_AddEdiIdempotency'),
    (N'20260813110000_AddBillingPipelineConfiguration'),
    (N'20260813162000_AddIncidentHealthPipeline'),
    (N'20260813210000_AddIncidentScope'),
    (N'20260815184142_AddProviderDurableIdentifiers'),
    (N'20260815192035_AddAtRequestSalesTaxOverride'),
    (N'20260815212109_AddAtRequestAttestation'),
    (N'20260815223729_AddAtRequestItemScreenshot'),
    (N'20260815230835_AddAtRequestPassthroughRate'),
    (N'20260816120000_AddNoteMinutesAndStartTime'),
    (N'20260818220245_AddEncryptedSsn'),
    (N'20260822210734_AddRepresentativePayeeProfile'),
    (N'20260825144021_AddConsumerNavigationFlags'),
    (N'20260825163103_AddConsumerEmail'),
    (N'20260827141239_AddBillingComplianceRequirements'),
    (N'20260828180603_AddProviderAffiliation'),
    (N'20260828182608_AddConsumerProviderList'),
    (N'20260828193518_AddProviderContacts'),
    (N'20260828195515_AddTestConsumerMarker'),
    (N'20260829231646_AddBillingExchangeHistory'),
    (N'20260830001538_AddRemittanceDeposits'),
    (N'20260830224423_AddUserPermissions'),
    (N'20260830231500_SeparateAgencyWideSupervision'),
    (N'20260901150802_AddUniqueFormPersonTypeDueDateIndex'),
    (N'20260901154714_AddDerivedFormCompliance'),
    (N'20260901232228_AddPersonCredibleClientId'),
    (N'20260902140636_AddCredibleProfileUpdateSetting'),
    (N'20260902142303_AddVocationalRehabilitationAssignments'),
    (N'20260903152847_AddFormAttestations'),
    (N'20260903173950_AddDocumentArtifacts'),
    (N'20260903175219_AddPersonCreatedAtAndStatus'),
    (N'20260903183136_AddLegalHolds'),
    (N'20260903185920_AddDocumentTemplatesAndSafetyPlans'),
    (N'20260903190302_AddSafetyPlans'),
    (N'20260903200511_CompleteAnnualDocumentWorkflow'),
    (N'20260906001943_AddTeamChat'),
    (N'20260906013301_AddSignatureEvidence'),
    (N'20260909153255_AddCheckRequests'),
    (N'20260910102153_AddCrashDiagnosticReadback'),
    (N'20260911022820_AddClearinghouseResponseIntake'),
    (N'20260911120000_AddAccountSessionLifecycle'),
    (N'20260912053013_AddPersonPhotos'),
    (N'20260913164040_AddOadsAuthoringSettings'),
    (N'20260914015314_AddRepresentativePayeeWorkflow'),
    (N'20260914023645_AddWeeklyCheckRequestAutomation'),
    (N'20260914030703_AddGoalProgressToCaseNotes'),
    (N'20260915004541_CorrectAnnualComplianceAndBillingPolicy'),
    (N'20260915013852_AddBillingCompliancePolicyReviewFlags'),
    (N'20260915153000_AllowSupersedingBillingComplianceRecovery'),
    (N'20260917203349_AddServiceDayInclusions'),
    (N'20260919212417_AddEftDepositsAndClaimCorrections'),
    (N'20260921235404_LinkNotesToExactFormObligations'),
    (N'20260921235644_AddFormAttestationChangeReviewFlags'),
    (N'20260922154932_AddMultiActivityNotes'),
    (N'20260922161704_LinkReleaseNotesToExactObligations'),
    (N'20260922162222_TrackReleaseAttestationRevocation'),
    (N'20260922191918_SupportReleaseAttestationReviewFlags'),
    (N'20260923180000_ReconcileDuplicateScheduledAgendaNotes'),
    (N'20260926183942_AddClearinghouseDispatchFoundation'),
    (N'20260927152031_AddExternalSignatureEvidence'),
    (N'20260927232039_SupportDurableOneOffReleases'),
    (N'20260928140919_AddAnnualPcpAndUnbilledNotes'),
    (N'20260929185110_AddFormWizardProgress'),
    (N'20260930142103_AddConsumerSchedule'),
    (N'20261002221023_AddScheduledNoteMoves'),
    (N'20261004120026_AddNoteAmendments'),
    (N'20261004204633_AddPayerBillingConfigurationVersions'),
    (N'20261007004626_AddRecordsGovernance'),
    (N'20261007111016_AddAssessmentReviewCycles')
;
IF (SELECT COUNT(*) FROM @predecessors)<>128 OR
   EXISTS (SELECT MigrationId FROM @predecessors EXCEPT
           SELECT MigrationId COLLATE Latin1_General_100_BIN2 FROM dbo.__EFMigrationsHistory) OR
   EXISTS (SELECT MigrationId COLLATE Latin1_General_100_BIN2 FROM dbo.__EFMigrationsHistory
           WHERE MigrationId COLLATE Latin1_General_100_BIN2<>@migration
           EXCEPT SELECT MigrationId FROM @predecessors)
    THROW 53911, 'Migration history differs from the exact reviewed predecessor chain.', 1;
DECLARE @wasApplied bit=CASE WHEN EXISTS
    (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId COLLATE Latin1_General_100_BIN2=@migration)
    THEN 1 ELSE 0 END;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>128+CONVERT(int,@wasApplied) OR
   EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId COLLATE Latin1_General_100_BIN2=@migration
           AND ProductVersion COLLATE Latin1_General_100_BIN2<>N'10.0.5')
    THROW 53911, 'Migration history has duplicates or an incompatible target version.', 1;

IF OBJECT_ID(N'dbo.ClearinghouseAccounts',N'U') IS NULL OR
   OBJECT_ID(N'dbo.ClearinghouseDispatches',N'U') IS NULL
    THROW 53912, 'The predecessor clearinghouse tables are missing.', 1;
IF EXISTS (SELECT 1 FROM (VALUES
    (N'ClearinghouseAccounts',N'AgencyId',N'int',4),
    (N'ClearinghouseAccounts',N'Id',N'uniqueidentifier',16),
    (N'ClearinghouseDispatches',N'State',N'int',4),
    (N'ClearinghouseDispatches',N'RequestedAtUtc',N'datetime2',8),
    (N'ClearinghouseDispatches',N'Id',N'uniqueidentifier',16),
    (N'ClearinghouseDispatches',N'AgencyId',N'int',4),
    (N'ClearinghouseDispatches',N'AccountId',N'uniqueidentifier',16)
) expected(TableName,ColumnName,TypeName,MaxLength)
WHERE NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'dbo.'+expected.TableName) AND c.name=expected.ColumnName
    AND t.name=expected.TypeName AND t.is_user_defined=0 AND c.max_length=expected.MaxLength
    AND c.is_nullable=0 AND c.is_computed=0 AND (t.name<>N'datetime2' OR c.scale=7)))
    THROW 53912, 'A prerequisite clearinghouse column has incompatible semantics.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.ClearinghouseAccounts')
    AND i.is_unique=1 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
    AND 2=(SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)
    AND N'AgencyId'=COL_NAME(i.object_id,(SELECT column_id FROM sys.index_columns WHERE object_id=i.object_id AND index_id=i.index_id AND key_ordinal=1))
    AND N'Id'=COL_NAME(i.object_id,(SELECT column_id FROM sys.index_columns WHERE object_id=i.object_id AND index_id=i.index_id AND key_ordinal=2)))
    THROW 53912, 'The predecessor composite account key is missing or incompatible.', 1;

DECLARE @changes int=0;
IF OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness') IS NOT NULL AND
   OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness',N'U') IS NULL
    THROW 53912, 'The readiness name belongs to an incompatible object.', 1;
IF OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness',N'U') IS NULL
BEGIN
    IF @wasApplied=1 THROW 53912, 'Migration history exists without the readiness table.', 1;
    SET @changes+=1;
    IF @preflightOnly=0
        EXEC(N'CREATE TABLE dbo.ClearinghouseDispatchReadiness (
            AgencyId int NOT NULL, AccountId uniqueidentifier NOT NULL,
            Disposition int NOT NULL, FailureCount int NOT NULL,
            RecoveryCycleId uniqueidentifier NOT NULL, NextEligibleAtUtc datetime2 NULL,
            LastFailureAtUtc datetime2 NULL, SafeFailureCode nvarchar(40) NULL,
            ValidatedAccountRevision bigint NOT NULL, Revision bigint NOT NULL);');
END;
-- The EF model has ten stored columns; count and every property are checked below.
IF OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness',N'U') IS NOT NULL
BEGIN
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness'))<>10 OR
       EXISTS (SELECT 1 FROM (VALUES
           (N'AgencyId',N'int',4,0), (N'AccountId',N'uniqueidentifier',16,0),
           (N'Disposition',N'int',4,0), (N'FailureCount',N'int',4,0),
           (N'RecoveryCycleId',N'uniqueidentifier',16,0), (N'NextEligibleAtUtc',N'datetime2',8,1),
           (N'LastFailureAtUtc',N'datetime2',8,1), (N'SafeFailureCode',N'nvarchar',80,1),
           (N'ValidatedAccountRevision',N'bigint',8,0), (N'Revision',N'bigint',8,0)
       ) expected(ColumnName,TypeName,MaxLength,IsNullable)
       WHERE NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
           WHERE c.object_id=OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness') AND c.name=expected.ColumnName
           AND t.name=expected.TypeName AND t.is_user_defined=0 AND c.max_length=expected.MaxLength
           AND c.is_nullable=expected.IsNullable AND c.is_identity=0 AND c.is_computed=0
           AND c.is_sparse=0 AND c.is_rowguidcol=0 AND c.default_object_id=0
           AND c.generated_always_type=0 AND c.encryption_type IS NULL
           AND (t.name<>N'datetime2' OR c.scale=7)))
        THROW 53912, 'Readiness columns differ from the reviewed model.', 1;
END;

IF OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness',N'U') IS NOT NULL
    EXEC(N'IF EXISTS (SELECT 1 FROM dbo.ClearinghouseDispatchReadiness GROUP BY AgencyId,AccountId HAVING COUNT_BIG(*)>1)
        OR EXISTS (SELECT 1 FROM dbo.ClearinghouseDispatchReadiness r WHERE NOT EXISTS
            (SELECT 1 FROM dbo.ClearinghouseAccounts a WHERE a.AgencyId=r.AgencyId AND a.Id=r.AccountId))
        THROW 53912, ''Existing readiness rows violate the reviewed account key or ownership.'', 1;');

DECLARE @indexes TABLE (IndexName sysname, TableName sysname, KeyColumns nvarchar(200),
    IncludeColumns nvarchar(200), IsPrimaryKey bit, CreateSql nvarchar(max));
INSERT @indexes VALUES
    (N'PK_ClearinghouseDispatchReadiness',N'ClearinghouseDispatchReadiness',N'AgencyId,AccountId',N'',1,
     N'ALTER TABLE dbo.ClearinghouseDispatchReadiness ADD CONSTRAINT PK_ClearinghouseDispatchReadiness PRIMARY KEY (AgencyId,AccountId);'),
    (N'IX_ClearinghouseDispatchReadiness_Disposition_NextEligibleAtUtc',N'ClearinghouseDispatchReadiness',N'Disposition,NextEligibleAtUtc',N'',0,
     N'CREATE INDEX IX_ClearinghouseDispatchReadiness_Disposition_NextEligibleAtUtc ON dbo.ClearinghouseDispatchReadiness (Disposition,NextEligibleAtUtc);'),
    (N'IX_ClearinghouseDispatches_State_RequestedAtUtc_Id',N'ClearinghouseDispatches',N'State,RequestedAtUtc,Id',N'AccountId,AgencyId',0,
     N'CREATE INDEX IX_ClearinghouseDispatches_State_RequestedAtUtc_Id ON dbo.ClearinghouseDispatches (State,RequestedAtUtc,Id) INCLUDE (AgencyId,AccountId);');
DECLARE @indexName sysname,@tableName sysname,@keyColumns nvarchar(200),@includeColumns nvarchar(200),@primary bit,@createSql nvarchar(max);
DECLARE indexCursor CURSOR LOCAL FAST_FORWARD FOR SELECT * FROM @indexes ORDER BY IsPrimaryKey DESC,IndexName;
OPEN indexCursor;
FETCH NEXT FROM indexCursor INTO @indexName,@tableName,@keyColumns,@includeColumns,@primary,@createSql;
WHILE @@FETCH_STATUS=0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.'+@tableName) AND name=@indexName)
    BEGIN
        IF @wasApplied=1 THROW 53912, 'Migration history exists without a required readiness index.', 1;
        IF @primary=1 AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.'+@tableName) AND is_primary_key=1)
            THROW 53912, 'An unexpected readiness primary key already exists.', 1;
        SET @changes+=1;
        IF @preflightOnly=0 EXEC sys.sp_executesql @createSql;
    END;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.'+@tableName) AND name=@indexName)
       AND NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.'+@tableName) AND i.name=@indexName
        AND i.is_primary_key=@primary AND i.is_unique=@primary AND i.type=CASE WHEN @primary=1 THEN 1 ELSE 2 END
        AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0 AND i.ignore_dup_key=0
        AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.is_descending_key=1)
        AND @keyColumns=(SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
            FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)
        AND @includeColumns=COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY c.name COLLATE Latin1_General_100_BIN2)
            FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.is_included_column=1),N''))
        THROW 53912, 'A readiness index differs from the reviewed keys, includes, or uniqueness.', 1;
    FETCH NEXT FROM indexCursor INTO @indexName,@tableName,@keyColumns,@includeColumns,@primary,@createSql;
END;
CLOSE indexCursor;
DEALLOCATE indexCursor;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness')
    AND name=N'FK_ClearinghouseDispatchReadiness_ClearinghouseAccounts_AgencyId_AccountId')
BEGIN
    IF @wasApplied=1 THROW 53912, 'Migration history exists without the readiness account relationship.', 1;
    SET @changes+=1;
    IF @preflightOnly=0 EXEC(N'ALTER TABLE dbo.ClearinghouseDispatchReadiness WITH CHECK ADD CONSTRAINT
        FK_ClearinghouseDispatchReadiness_ClearinghouseAccounts_AgencyId_AccountId FOREIGN KEY (AgencyId,AccountId)
        REFERENCES dbo.ClearinghouseAccounts(AgencyId,Id) ON DELETE NO ACTION ON UPDATE NO ACTION;');
END;
IF OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness',N'U') IS NOT NULL AND
   (EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness')
            AND name<>N'FK_ClearinghouseDispatchReadiness_ClearinghouseAccounts_AgencyId_AccountId') OR
    EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.parent_object_id=OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness')
        AND (fk.referenced_object_id<>OBJECT_ID(N'dbo.ClearinghouseAccounts') OR fk.is_disabled<>0 OR fk.is_not_trusted<>0
            OR fk.is_not_for_replication<>0 OR fk.delete_referential_action<>0 OR fk.update_referential_action<>0
            OR 2<>(SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id=fk.object_id)
            OR NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE constraint_object_id=fk.object_id AND constraint_column_id=1
                AND COL_NAME(parent_object_id,parent_column_id)=N'AgencyId' AND COL_NAME(referenced_object_id,referenced_column_id)=N'AgencyId')
            OR NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE constraint_object_id=fk.object_id AND constraint_column_id=2
                AND COL_NAME(parent_object_id,parent_column_id)=N'AccountId' AND COL_NAME(referenced_object_id,referenced_column_id)=N'Id'))))
    THROW 53912, 'The readiness account relationship differs from the reviewed composite restriction.', 1;

DECLARE @checkExpression nvarchar(max)=N'[AgencyId] > 0 AND [Revision] > 0 AND [ValidatedAccountRevision] >= 0 AND (([Disposition] = 1 AND [FailureCount] = 0 AND [RecoveryCycleId] = ''00000000-0000-0000-0000-000000000000'' AND [NextEligibleAtUtc] IS NULL AND [LastFailureAtUtc] IS NULL AND [SafeFailureCode] IS NULL) OR ([Disposition] = 2 AND [FailureCount] BETWEEN 1 AND 4 AND [RecoveryCycleId] <> ''00000000-0000-0000-0000-000000000000'' AND [NextEligibleAtUtc] IS NOT NULL AND [LastFailureAtUtc] IS NOT NULL AND [NextEligibleAtUtc] > [LastFailureAtUtc] AND [SafeFailureCode] IS NOT NULL AND [SafeFailureCode] = ''account_key_unavailable'') OR ([Disposition] = 3 AND [FailureCount] = 5 AND [RecoveryCycleId] <> ''00000000-0000-0000-0000-000000000000'' AND [NextEligibleAtUtc] IS NULL AND [LastFailureAtUtc] IS NOT NULL AND [SafeFailureCode] IS NOT NULL AND [SafeFailureCode] = ''account_key_unavailable''))';
IF OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness',N'U') IS NOT NULL
BEGIN
    SET @createSql=N'IF EXISTS (SELECT 1 FROM dbo.ClearinghouseDispatchReadiness WHERE NOT ('+@checkExpression+N'))
        THROW 53912, ''Existing readiness rows violate the reviewed state constraint.'', 1;';
    EXEC sys.sp_executesql @createSql;
END;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness')
    AND name=N'CK_ClearinghouseDispatchReadiness_State')
BEGIN
    IF @wasApplied=1 THROW 53912, 'Migration history exists without the readiness state constraint.', 1;
    SET @changes+=1;
    IF @preflightOnly=0
    BEGIN
        SET @createSql=N'ALTER TABLE dbo.ClearinghouseDispatchReadiness WITH CHECK ADD CONSTRAINT CK_ClearinghouseDispatchReadiness_State CHECK ('+@checkExpression+N');';
        EXEC sys.sp_executesql @createSql;
    END;
END;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness'))
BEGIN
    DECLARE @expectedDefinition nvarchar(max);
    -- Dynamic scope destroys its private temp table even if compilation/validation fails.
    SET @createSql=N'CREATE TABLE #SatiReadinessExpectedCheck (AgencyId int NOT NULL, AccountId uniqueidentifier NOT NULL,
        Disposition int NOT NULL, FailureCount int NOT NULL, RecoveryCycleId uniqueidentifier NOT NULL,
        NextEligibleAtUtc datetime2 NULL, LastFailureAtUtc datetime2 NULL, SafeFailureCode nvarchar(40) NULL,
        ValidatedAccountRevision bigint NOT NULL, Revision bigint NOT NULL, CHECK ('+@checkExpression+N'));
        SELECT @definition=definition FROM tempdb.sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N''tempdb..#SatiReadinessExpectedCheck'');
        DROP TABLE #SatiReadinessExpectedCheck;';
    EXEC sys.sp_executesql @createSql,N'@definition nvarchar(max) OUTPUT',@definition=@expectedDefinition OUTPUT;
    IF @expectedDefinition IS NULL OR
       (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness'))<>1 OR
       NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ClearinghouseDispatchReadiness')
           AND name=N'CK_ClearinghouseDispatchReadiness_State' AND is_disabled=0 AND is_not_trusted=0 AND is_not_for_replication=0
           AND definition COLLATE Latin1_General_100_BIN2=@expectedDefinition COLLATE Latin1_General_100_BIN2)
        THROW 53912, 'The readiness state constraint differs from the reviewed trusted expression.', 1;
END;

IF @wasApplied=0
BEGIN
    SET @changes+=1;
    IF @preflightOnly=0
        INSERT dbo.__EFMigrationsHistory(MigrationId,ProductVersion) VALUES(@migration,N'10.0.5');
END;
IF @@TRANCOUNT<>1 OR XACT_STATE()<>1 OR COALESCE(APPLOCK_MODE(N'public',N'SatiDemo.FullReset',N'Transaction'),N'')<>N'Exclusive'
    THROW 53914, 'Migration transaction or reset exclusion was lost.', 1;
IF @preflightOnly=0 AND (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>129
    THROW 53911, 'The migration did not reach the reviewed 129-row history.', 1;
SELECT CONVERT(bit,CASE WHEN @wasApplied=0 AND @preflightOnly=0 THEN 1 ELSE 0 END) AS MigrationWasApplied,
    CONVERT(bit,CASE WHEN @preflightOnly=1 AND @changes>0 THEN 1 ELSE 0 END) AS ChangesRequired,
    (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount;
