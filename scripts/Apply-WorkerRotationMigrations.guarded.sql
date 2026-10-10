-- Controlled rotation migrations 130-132; the caller owns one transaction.
-- Parameters: @expectedDatabase nvarchar(128), @preflightOnly bit.
-- Preflight writes no persistent target data/schema; temporary expected CHECK
-- metadata is compiled by SQL Server in a connection-local scope and destroyed.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT<>1 OR XACT_STATE()<>1
    THROW 54010, 'Caller must own exactly one committable migration transaction.', 1;
IF @preflightOnly IS NULL OR @expectedDatabase IS NULL OR
   (DB_NAME()+N'#') COLLATE Latin1_General_100_BIN2<>(@expectedDatabase+N'#') COLLATE Latin1_General_100_BIN2 OR
   (@expectedDatabase COLLATE Latin1_General_100_BIN2<>N'SatiDemo' AND
    (LEN(@expectedDatabase)<>54 OR LEFT(@expectedDatabase,22) COLLATE Latin1_General_100_BIN2<>N'SatiSyntheticPipeline_' OR
     SUBSTRING(@expectedDatabase,23,32) COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9a-fA-F]%')) OR
   OBJECT_ID(N'dbo.SatiDatabaseIdentity',N'U') IS NULL
    THROW 54010, 'Refusing an environment outside marked Demo or a private synthetic rehearsal.', 1;
IF (SELECT COUNT(*) FROM dbo.SatiDatabaseIdentity)<>1 OR NOT EXISTS
   (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id=1 AND
    (EnvironmentName+N'#') COLLATE Latin1_General_100_BIN2=N'Demo#' AND
    InstanceId IS NOT NULL AND InstanceId<>'00000000-0000-0000-0000-000000000000')
    THROW 54010, 'Database identity does not describe the reviewed Demo environment.', 1;
DECLARE @lockResult int;
EXEC @lockResult=sys.sp_getapplock @Resource=N'SatiDemo.FullReset',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=60000;
IF @lockResult<0 OR COALESCE(APPLOCK_MODE(N'public',N'SatiDemo.FullReset',N'Transaction'),N'')<>N'Exclusive'
    THROW 54013, 'Exclusive Demo reset exclusion is unavailable.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL
    THROW 54011, 'Migration history is missing.', 1;
DECLARE @predecessors TABLE(MigrationId nvarchar(150) COLLATE Latin1_General_100_BIN2 PRIMARY KEY);
INSERT @predecessors VALUES
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
 (N'20261007111016_AddAssessmentReviewCycles'),
 (N'20261009183720_AddClearinghousePreflightReadiness')
;
DECLARE @migrations TABLE(Number int PRIMARY KEY, MigrationId nvarchar(150) COLLATE Latin1_General_100_BIN2, WasApplied bit);
INSERT @migrations VALUES
 (130,N'20261010021210_AddClearinghouseDispatchRotation',0),
 (131,N'20261010032625_AddClearinghousePollRotation',0),
 (132,N'20261010042710_AddSignatureWorkRotation',0);
UPDATE expected SET WasApplied=1 FROM @migrations expected WHERE EXISTS
 (SELECT 1 FROM dbo.__EFMigrationsHistory h WHERE h.MigrationId COLLATE Latin1_General_100_BIN2=expected.MigrationId);
DECLARE @beforeCount int=(SELECT COUNT(*) FROM dbo.__EFMigrationsHistory);
-- HISTORY_GUARD_BEGIN
IF (SELECT COUNT(*) FROM @predecessors)<>129 OR
   EXISTS(SELECT MigrationId FROM @predecessors EXCEPT SELECT MigrationId COLLATE Latin1_General_100_BIN2 FROM dbo.__EFMigrationsHistory) OR
   EXISTS(SELECT MigrationId COLLATE Latin1_General_100_BIN2 FROM dbo.__EFMigrationsHistory EXCEPT
          SELECT MigrationId FROM (SELECT MigrationId FROM @predecessors UNION ALL SELECT MigrationId FROM @migrations) allowed)
    THROW 54011, 'History differs from the exact reviewed predecessor and rotation chain.', 1;
IF @beforeCount<>129+(SELECT COUNT(*) FROM @migrations WHERE WasApplied=1) OR
   EXISTS(SELECT 1 FROM @migrations later JOIN @migrations earlier ON earlier.Number<later.Number WHERE later.WasApplied=1 AND earlier.WasApplied=0) OR
   EXISTS(SELECT 1 FROM dbo.__EFMigrationsHistory h JOIN @migrations m ON h.MigrationId COLLATE Latin1_General_100_BIN2=m.MigrationId
          WHERE h.ProductVersion COLLATE Latin1_General_100_BIN2<>N'10.0.5')
    THROW 54011, 'History has duplicates, a rotation gap, or an incompatible target version.', 1;
-- HISTORY_GUARD_END

DECLARE @tables TABLE(Number int, TableName sysname PRIMARY KEY, ColumnsSql nvarchar(max), KeysSql nvarchar(200), CheckSql nvarchar(max));
INSERT @tables VALUES
 (130,N'ClearinghouseAgencyDispatchRotation',N'AgencyId int NOT NULL,LastAccountId uniqueidentifier NULL,Revision bigint NOT NULL',N'AgencyId',N'[AgencyId] > 0 AND [Revision] > 0'),
 (130,N'ClearinghouseDispatchRotation',N'Id int NOT NULL,LastAgencyId int NULL,Revision bigint NOT NULL',N'Id',N'[Id] = 1 AND [Revision] > 0 AND ([LastAgencyId] IS NULL OR [LastAgencyId] > 0)'),
 (131,N'ClearinghouseAccountPollRotation',N'AgencyId int NOT NULL,AccountId uniqueidentifier NOT NULL,LastFeedKind int NULL,Revision bigint NOT NULL',N'AgencyId,AccountId',N'[AgencyId] > 0 AND [Revision] > 0 AND ([LastFeedKind] IS NULL OR [LastFeedKind] IN (1,2))'),
 (131,N'ClearinghouseAgencyPollRotation',N'AgencyId int NOT NULL,LastAccountId uniqueidentifier NULL,Revision bigint NOT NULL',N'AgencyId',N'[AgencyId] > 0 AND [Revision] > 0'),
 (131,N'ClearinghousePollRotation',N'Id int NOT NULL,LastAgencyId int NULL,Revision bigint NOT NULL',N'Id',N'[Id] = 1 AND [Revision] > 0 AND ([LastAgencyId] IS NULL OR [LastAgencyId] > 0)'),
 (132,N'SignatureWorkRotation',N'WorkKind int NOT NULL,LastAgencyId int NULL,Revision bigint NOT NULL',N'WorkKind',N'[WorkKind] IN (1,2,3) AND [Revision] > 0 AND ([LastAgencyId] IS NULL OR [LastAgencyId] > 0)'),
 (132,N'SignatureAgencyWorkRotation',N'AgencyId int NOT NULL,WorkKind int NOT NULL,LastItemId bigint NULL,Revision bigint NOT NULL',N'AgencyId,WorkKind',N'[AgencyId] > 0 AND [WorkKind] IN (1,2,3) AND [Revision] > 0 AND ([LastItemId] IS NULL OR [LastItemId] > 0)');
DECLARE @columns TABLE(TableName sysname,ColumnName sysname,TypeName sysname,MaxLength int,IsNullable bit);
INSERT @columns VALUES
 (N'ClearinghouseAgencyDispatchRotation',N'AgencyId',N'int',4,0),
 (N'ClearinghouseAgencyDispatchRotation',N'LastAccountId',N'uniqueidentifier',16,1),
 (N'ClearinghouseAgencyDispatchRotation',N'Revision',N'bigint',8,0),
 (N'ClearinghouseDispatchRotation',N'Id',N'int',4,0),
 (N'ClearinghouseDispatchRotation',N'LastAgencyId',N'int',4,1),
 (N'ClearinghouseDispatchRotation',N'Revision',N'bigint',8,0),
 (N'ClearinghouseAccountPollRotation',N'AgencyId',N'int',4,0),
 (N'ClearinghouseAccountPollRotation',N'AccountId',N'uniqueidentifier',16,0),
 (N'ClearinghouseAccountPollRotation',N'LastFeedKind',N'int',4,1),
 (N'ClearinghouseAccountPollRotation',N'Revision',N'bigint',8,0),
 (N'ClearinghouseAgencyPollRotation',N'AgencyId',N'int',4,0),
 (N'ClearinghouseAgencyPollRotation',N'LastAccountId',N'uniqueidentifier',16,1),
 (N'ClearinghouseAgencyPollRotation',N'Revision',N'bigint',8,0),
 (N'ClearinghousePollRotation',N'Id',N'int',4,0),
 (N'ClearinghousePollRotation',N'LastAgencyId',N'int',4,1),
 (N'ClearinghousePollRotation',N'Revision',N'bigint',8,0),
 (N'SignatureWorkRotation',N'WorkKind',N'int',4,0),
 (N'SignatureWorkRotation',N'LastAgencyId',N'int',4,1),
 (N'SignatureWorkRotation',N'Revision',N'bigint',8,0),
 (N'SignatureAgencyWorkRotation',N'AgencyId',N'int',4,0),
 (N'SignatureAgencyWorkRotation',N'WorkKind',N'int',4,0),
 (N'SignatureAgencyWorkRotation',N'LastItemId',N'bigint',8,1),
 (N'SignatureAgencyWorkRotation',N'Revision',N'bigint',8,0),
 (N'Agencies',N'Id',N'int',4,0),
 (N'ClearinghouseAccounts',N'AgencyId',N'int',4,0),
 (N'ClearinghouseAccounts',N'Id',N'uniqueidentifier',16,0),
 (N'ClearinghouseAccounts',N'IsEnabled',N'bit',1,0),
 (N'ClearinghouseAccounts',N'IsTest',N'bit',1,0),
 (N'ClearinghouseAccounts',N'ConnectorKind',N'int',4,0),
 (N'ClearinghouseDispatches',N'AgencyId',N'int',4,0),
 (N'ClearinghouseDispatches',N'AccountId',N'uniqueidentifier',16,0),
 (N'ClearinghouseDispatches',N'RequestedAtUtc',N'datetime2',8,0),
 (N'ClearinghouseDispatches',N'Id',N'uniqueidentifier',16,0),
 (N'ClearinghouseDispatches',N'State',N'int',4,0),
 (N'ClearinghouseFeedCheckpoints',N'AgencyId',N'int',4,0),
 (N'ClearinghouseFeedCheckpoints',N'AccountId',N'uniqueidentifier',16,0),
 (N'ClearinghouseFeedCheckpoints',N'FeedKind',N'int',4,0),
 (N'SignatureOutbox',N'AgencyId',N'int',4,0),
 (N'SignatureOutbox',N'Id',N'bigint',8,0),
 (N'SignatureCompletions',N'AgencyId',N'int',4,0),
 (N'SignatureCompletions',N'Id',N'int',4,0);

-- Validate prerequisite index/FK columns, without claiming a whole predecessor-schema audit.
IF EXISTS(SELECT 1 FROM @columns expected WHERE NOT EXISTS(SELECT 1 FROM @tables WHERE TableName=expected.TableName)
 AND NOT EXISTS(SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'dbo.'+expected.TableName,N'U') AND c.name=expected.ColumnName
    AND t.name=expected.TypeName AND t.is_user_defined=0 AND c.max_length=expected.MaxLength
    AND c.is_nullable=expected.IsNullable AND c.is_computed=0 AND (t.name<>N'datetime2' OR c.scale=7)))
    THROW 54012, 'A prerequisite worker column is missing or incompatible.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.ClearinghouseAccounts')
 AND i.is_unique=1 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
 AND N'AgencyId,Id'=(SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP(ORDER BY ic.key_ordinal)
    FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0))
    THROW 54012, 'The predecessor composite account key is missing or incompatible.', 1;

-- TRIGGER_GUARD_BEGIN
IF EXISTS(SELECT 1 FROM sys.triggers triggerObject JOIN @tables expected
    ON triggerObject.parent_id=OBJECT_ID(N'dbo.'+expected.TableName)
    WHERE triggerObject.parent_class=1 AND triggerObject.is_disabled=0)
    THROW 54012, 'An enabled DML trigger adds unreviewed behavior to a rotation table.', 1;
-- TRIGGER_GUARD_END
DECLARE @changes int=0,@number int,@table sysname,@columnsSql nvarchar(max),@keys nvarchar(200),@check nvarchar(max),@sql nvarchar(max),@applied bit;
DECLARE tableCursor CURSOR LOCAL FAST_FORWARD FOR SELECT * FROM @tables ORDER BY Number,CASE WHEN TableName=N'SignatureWorkRotation' THEN 0 ELSE 1 END,TableName;
OPEN tableCursor;
FETCH NEXT FROM tableCursor INTO @number,@table,@columnsSql,@keys,@check;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @applied=(SELECT WasApplied FROM @migrations WHERE Number=@number);
 IF OBJECT_ID(N'dbo.'+@table) IS NOT NULL AND OBJECT_ID(N'dbo.'+@table,N'U') IS NULL
    THROW 54012, 'A rotation table name belongs to an incompatible object.', 1;
 IF OBJECT_ID(N'dbo.'+@table,N'U') IS NULL
 BEGIN
    IF @applied=1 THROW 54012, 'Tracked rotation history is missing a table.', 1;
    SET @changes+=1;
    IF @preflightOnly=0
    BEGIN
      SET @sql=N'CREATE TABLE dbo.'+QUOTENAME(@table)+N' ('+@columnsSql+N');';
      EXEC sys.sp_executesql @sql;
    END;
 END;
 IF OBJECT_ID(N'dbo.'+@table,N'U') IS NOT NULL
 BEGIN
    -- COLUMN_GUARD_BEGIN
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.'+@table))<>(SELECT COUNT(*) FROM @columns WHERE TableName=@table) OR
       EXISTS(SELECT 1 FROM @columns expected WHERE expected.TableName=@table AND NOT EXISTS
        (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
         WHERE c.object_id=OBJECT_ID(N'dbo.'+@table) AND c.name=expected.ColumnName AND t.name=expected.TypeName
         AND t.is_user_defined=0 AND c.max_length=expected.MaxLength AND c.is_nullable=expected.IsNullable
         AND c.is_identity=0 AND c.is_computed=0 AND c.is_sparse=0 AND c.is_rowguidcol=0 AND c.default_object_id=0
         AND c.generated_always_type=0 AND c.encryption_type IS NULL))
        THROW 54012, 'Rotation columns differ from the reviewed model.', 1;
    -- COLUMN_GUARD_END
    SET @sql=N'IF EXISTS(SELECT 1 FROM dbo.'+QUOTENAME(@table)+N' WHERE NOT ('+@check+N')) OR EXISTS
       (SELECT 1 FROM dbo.'+QUOTENAME(@table)+N' GROUP BY '+@keys+N' HAVING COUNT_BIG(*)>1)
       THROW 54012, ''Existing rotation rows violate reviewed scope or uniqueness.'', 1;';
    EXEC sys.sp_executesql @sql;
 END;
 DECLARE @checkName sysname=N'CK_'+@table+N'_Scope';
 IF OBJECT_ID(N'dbo.'+@checkName) IS NOT NULL AND NOT EXISTS
    (SELECT 1 FROM sys.check_constraints WHERE object_id=OBJECT_ID(N'dbo.'+@checkName) AND parent_object_id=OBJECT_ID(N'dbo.'+@table))
    THROW 54012, 'A reviewed rotation constraint name belongs to another object.', 1;
 IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.'+@table) AND name=@checkName)
 BEGIN
    IF @applied=1 THROW 54012, 'Tracked rotation history is missing a scope constraint.', 1;
    SET @changes+=1;
    IF @preflightOnly=0
    BEGIN
      SET @sql=N'ALTER TABLE dbo.'+QUOTENAME(@table)+N' WITH CHECK ADD CONSTRAINT '+QUOTENAME(@checkName)+N' CHECK ('+@check+N');';
      EXEC sys.sp_executesql @sql;
    END;
 END;
 IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.'+@table))
 BEGIN
    DECLARE @expectedCheck nvarchar(max)=NULL;
    SET @sql=N'CREATE TABLE #SatiExpectedRotationCheck ('+@columnsSql+N',CHECK ('+@check+N'));
        SELECT @definition=definition FROM tempdb.sys.check_constraints WHERE parent_object_id=OBJECT_ID(N''tempdb..#SatiExpectedRotationCheck'');
        DROP TABLE #SatiExpectedRotationCheck;';
    EXEC sys.sp_executesql @sql,N'@definition nvarchar(max) OUTPUT',@definition=@expectedCheck OUTPUT;
    -- CHECK_GUARD_BEGIN
    IF @expectedCheck IS NULL OR (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.'+@table))<>1 OR
       NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.'+@table) AND name=@checkName
        AND is_disabled=0 AND is_not_trusted=0 AND is_not_for_replication=0
        AND definition COLLATE Latin1_General_100_BIN2=@expectedCheck COLLATE Latin1_General_100_BIN2)
        THROW 54012, 'A rotation scope constraint differs from its reviewed trusted expression.', 1;
    -- CHECK_GUARD_END
 END;
 FETCH NEXT FROM tableCursor INTO @number,@table,@columnsSql,@keys,@check;
END;
CLOSE tableCursor;
DEALLOCATE tableCursor;

DECLARE @indexes TABLE(Number int,TableName sysname,IndexName sysname,KeysSql nvarchar(200),IsPrimary bit,IsUnique bit,FilterSql nvarchar(200));
INSERT @indexes SELECT Number,TableName,N'PK_'+TableName,KeysSql,1,1,NULL FROM @tables;
INSERT @indexes VALUES
 (130,N'ClearinghouseDispatches',N'IX_ClearinghouseDispatches_QueuedLane',N'AgencyId,AccountId,RequestedAtUtc,Id',0,0,N'[State] = 1'),
 (131,N'ClearinghouseFeedCheckpoints',N'IX_ClearinghouseFeedCheckpoints_AgencyId_AccountId_FeedKind',N'AgencyId,AccountId,FeedKind',0,1,NULL),
 (131,N'ClearinghouseAccounts',N'IX_ClearinghouseAccounts_AgencyId_IsEnabled_IsTest_ConnectorKind_Id',N'AgencyId,IsEnabled,IsTest,ConnectorKind,Id',0,0,NULL),
 (132,N'SignatureOutbox',N'IX_SignatureOutbox_AgencyId_Id',N'AgencyId,Id',0,0,NULL),
 (132,N'SignatureCompletions',N'IX_SignatureCompletions_AgencyId_Id',N'AgencyId,Id',0,0,NULL),
 (132,N'SignatureAgencyWorkRotation',N'IX_SignatureAgencyWorkRotation_WorkKind',N'WorkKind',0,0,NULL);
DECLARE @index sysname,@primary bit,@unique bit,@filter nvarchar(200);
DECLARE indexCursor CURSOR LOCAL FAST_FORWARD FOR SELECT * FROM @indexes ORDER BY IsPrimary DESC,Number,IndexName;
OPEN indexCursor;
FETCH NEXT FROM indexCursor INTO @number,@table,@index,@keys,@primary,@unique,@filter;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @applied=(SELECT WasApplied FROM @migrations WHERE Number=@number);
 IF @unique=1 AND OBJECT_ID(N'dbo.'+@table,N'U') IS NOT NULL
 BEGIN
    -- DUPLICATE_GUARD_BEGIN
    SET @sql=N'IF EXISTS(SELECT 1 FROM dbo.'+QUOTENAME(@table)+N' GROUP BY '+@keys+N' HAVING COUNT_BIG(*)>1)
        THROW 54012, ''Existing rows violate reviewed rotation/index uniqueness; no automatic cleanup is allowed.'', 1;';
    EXEC sys.sp_executesql @sql;
    -- DUPLICATE_GUARD_END
 END;
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.'+@table) AND name=@index)
 BEGIN
    IF @applied=1 THROW 54012, 'Tracked rotation history is missing an index.', 1;
    IF @primary=1 AND (OBJECT_ID(N'dbo.'+@index) IS NOT NULL OR EXISTS
      (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.'+@table) AND is_primary_key=1))
       THROW 54012, 'A rotation primary key conflicts with an existing object.', 1;
    SET @changes+=1;
    IF @preflightOnly=0
    BEGIN
      SET @sql=CASE WHEN @primary=1 THEN N'ALTER TABLE dbo.'+QUOTENAME(@table)+N' ADD CONSTRAINT '+QUOTENAME(@index)+N' PRIMARY KEY ('+@keys+N');'
        ELSE N'CREATE '+CASE WHEN @unique=1 THEN N'UNIQUE ' ELSE N'' END+N'INDEX '+QUOTENAME(@index)+N' ON dbo.'+QUOTENAME(@table)+N' ('+@keys+N')'+
            CASE WHEN @filter IS NULL THEN N'' ELSE N' WHERE '+@filter END+N';' END;
      EXEC sys.sp_executesql @sql;
    END;
 END;
 -- INDEX_GUARD_BEGIN
 IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.'+@table) AND name=@index) AND NOT EXISTS
   (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.'+@table) AND i.name=@index
    AND i.is_primary_key=@primary AND i.is_unique=@unique AND i.type=CASE WHEN @primary=1 THEN 1 ELSE 2 END
    AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.ignore_dup_key=0
    AND ((@filter IS NULL AND i.has_filter=0) OR (@filter IS NOT NULL AND i.has_filter=1 AND i.filter_definition COLLATE Latin1_General_100_BIN2=N'([State]=(1))'))
    AND NOT EXISTS(SELECT 1 FROM sys.index_columns WHERE object_id=i.object_id AND index_id=i.index_id AND (is_descending_key=1 OR is_included_column=1))
    AND @keys=(SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP(ORDER BY ic.key_ordinal)
       FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
       WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0))
    THROW 54012, 'A rotation index differs from its reviewed keys, filter, or uniqueness.', 1;
 -- INDEX_GUARD_END
 FETCH NEXT FROM indexCursor INTO @number,@table,@index,@keys,@primary,@unique,@filter;
END;
CLOSE indexCursor;
DEALLOCATE indexCursor;

-- Global seeds are inserted only when absent in an unrecorded migration. Existing
-- positive revisions and last-position values are operational state, never reset.
DECLARE @seeds TABLE(Number int,TableName sysname,KeyName sysname,KeyValue int);
INSERT @seeds VALUES (130,N'ClearinghouseDispatchRotation',N'Id',1),(131,N'ClearinghousePollRotation',N'Id',1),
 (132,N'SignatureWorkRotation',N'WorkKind',1),(132,N'SignatureWorkRotation',N'WorkKind',2),(132,N'SignatureWorkRotation',N'WorkKind',3);
DECLARE @keyName sysname,@keyValue int,@seedExists bit;
DECLARE seedCursor CURSOR LOCAL FAST_FORWARD FOR SELECT * FROM @seeds ORDER BY Number,KeyValue;
OPEN seedCursor;
FETCH NEXT FROM seedCursor INTO @number,@table,@keyName,@keyValue;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @seedExists=0;
 IF OBJECT_ID(N'dbo.'+@table,N'U') IS NOT NULL
 BEGIN
    SET @sql=N'SELECT @exists=CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.'+QUOTENAME(@table)+N' WHERE '+QUOTENAME(@keyName)+N'=@id) THEN 1 ELSE 0 END);';
    EXEC sys.sp_executesql @sql,N'@id int,@exists bit OUTPUT',@id=@keyValue,@exists=@seedExists OUTPUT;
 END;
 IF @seedExists=0
 BEGIN
    IF EXISTS(SELECT 1 FROM @migrations WHERE Number=@number AND WasApplied=1)
        THROW 54012, 'Tracked rotation history is missing a required scheduling seed.', 1;
    SET @changes+=1;
    IF @preflightOnly=0
    BEGIN
      SET @sql=N'INSERT dbo.'+QUOTENAME(@table)+N' ('+QUOTENAME(@keyName)+N',LastAgencyId,Revision) VALUES(@id,NULL,1);';
      EXEC sys.sp_executesql @sql,N'@id int',@id=@keyValue;
    END;
 END;
 FETCH NEXT FROM seedCursor INTO @number,@table,@keyName,@keyValue;
END;
CLOSE seedCursor;
DEALLOCATE seedCursor;

DECLARE @foreignKeys TABLE(Number int,TableName sysname,ConstraintName sysname,KeysSql nvarchar(200),ReferenceTable sysname,ReferenceKeys nvarchar(200),JoinSql nvarchar(400));
INSERT @foreignKeys VALUES
 (130,N'ClearinghouseAgencyDispatchRotation',N'FK_ClearinghouseAgencyDispatchRotation_Agencies_AgencyId',N'AgencyId',N'Agencies',N'Id',N'r.AgencyId=p.Id'),
 (131,N'ClearinghouseAccountPollRotation',N'FK_ClearinghouseAccountPollRotation_ClearinghouseAccounts_AgencyId_AccountId',N'AgencyId,AccountId',N'ClearinghouseAccounts',N'AgencyId,Id',N'r.AgencyId=p.AgencyId AND r.AccountId=p.Id'),
 (131,N'ClearinghouseAgencyPollRotation',N'FK_ClearinghouseAgencyPollRotation_Agencies_AgencyId',N'AgencyId',N'Agencies',N'Id',N'r.AgencyId=p.Id'),
 (132,N'SignatureAgencyWorkRotation',N'FK_SignatureAgencyWorkRotation_Agencies_AgencyId',N'AgencyId',N'Agencies',N'Id',N'r.AgencyId=p.Id'),
 (132,N'SignatureAgencyWorkRotation',N'FK_SignatureAgencyWorkRotation_SignatureWorkRotation_WorkKind',N'WorkKind',N'SignatureWorkRotation',N'WorkKind',N'r.WorkKind=p.WorkKind');
DECLARE @constraint sysname,@referenceTable sysname,@referenceKeys nvarchar(200),@join nvarchar(400);
DECLARE foreignCursor CURSOR LOCAL FAST_FORWARD FOR SELECT * FROM @foreignKeys ORDER BY Number,ConstraintName;
OPEN foreignCursor;
FETCH NEXT FROM foreignCursor INTO @number,@table,@constraint,@keys,@referenceTable,@referenceKeys,@join;
WHILE @@FETCH_STATUS=0
BEGIN
 IF OBJECT_ID(N'dbo.'+@table,N'U') IS NOT NULL
 BEGIN
    -- In preflight a missing unrecorded SignatureWorkRotation seed can be safely
    -- supplied; its child CHECK already restricts WorkKind to those three seeds.
    IF @referenceTable<>N'SignatureWorkRotation' OR @preflightOnly=0 OR EXISTS(SELECT 1 FROM @migrations WHERE Number=132 AND WasApplied=1)
    BEGIN
      SET @sql=N'IF EXISTS(SELECT 1 FROM dbo.'+QUOTENAME(@table)+N' r WHERE NOT EXISTS
        (SELECT 1 FROM dbo.'+QUOTENAME(@referenceTable)+N' p WHERE '+@join+N'))
        THROW 54012, ''Existing rotation rows violate reviewed ownership.'', 1;';
      EXEC sys.sp_executesql @sql;
    END;
 END;
 IF OBJECT_ID(N'dbo.'+@constraint) IS NOT NULL AND NOT EXISTS
    (SELECT 1 FROM sys.foreign_keys WHERE object_id=OBJECT_ID(N'dbo.'+@constraint) AND parent_object_id=OBJECT_ID(N'dbo.'+@table))
    THROW 54012, 'A rotation relationship name conflicts with an existing object.', 1;
 IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.'+@table) AND name=@constraint)
 BEGIN
    IF EXISTS(SELECT 1 FROM @migrations WHERE Number=@number AND WasApplied=1)
        THROW 54012, 'Tracked rotation history is missing a relationship.', 1;
    SET @changes+=1;
    IF @preflightOnly=0
    BEGIN
      SET @sql=N'ALTER TABLE dbo.'+QUOTENAME(@table)+N' WITH CHECK ADD CONSTRAINT '+QUOTENAME(@constraint)+N' FOREIGN KEY ('+@keys+N') REFERENCES dbo.'+
        QUOTENAME(@referenceTable)+N' ('+@referenceKeys+N') ON DELETE NO ACTION ON UPDATE NO ACTION;';
      EXEC sys.sp_executesql @sql;
    END;
 END;
 -- FK_GUARD_BEGIN
 IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.'+@table) AND name=@constraint) AND NOT EXISTS
   (SELECT 1 FROM sys.foreign_keys fk WHERE fk.parent_object_id=OBJECT_ID(N'dbo.'+@table) AND fk.name=@constraint
    AND fk.referenced_object_id=OBJECT_ID(N'dbo.'+@referenceTable) AND fk.is_disabled=0 AND fk.is_not_trusted=0
    AND fk.is_not_for_replication=0 AND fk.delete_referential_action=0 AND fk.update_referential_action=0
    AND @keys=(SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(parent_object_id,parent_column_id)),N',') WITHIN GROUP(ORDER BY constraint_column_id)
      FROM sys.foreign_key_columns WHERE constraint_object_id=fk.object_id)
    AND @referenceKeys=(SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(referenced_object_id,referenced_column_id)),N',') WITHIN GROUP(ORDER BY constraint_column_id)
      FROM sys.foreign_key_columns WHERE constraint_object_id=fk.object_id))
    THROW 54012, 'A rotation relationship differs from its reviewed trusted restriction.', 1;
 -- FK_GUARD_END
 FETCH NEXT FROM foreignCursor INTO @number,@table,@constraint,@keys,@referenceTable,@referenceKeys,@join;
END;
CLOSE foreignCursor;
DEALLOCATE foreignCursor;
IF EXISTS(SELECT 1 FROM sys.foreign_keys fk JOIN @tables t ON fk.parent_object_id=OBJECT_ID(N'dbo.'+t.TableName)
          WHERE NOT EXISTS(SELECT 1 FROM @foreignKeys expected WHERE expected.TableName=t.TableName AND expected.ConstraintName=fk.name))
    THROW 54012, 'An unexpected relationship exists on a rotation table.', 1;

DECLARE @newHistoryCount int=(SELECT COUNT(*) FROM @migrations WHERE WasApplied=0);
IF @newHistoryCount>0
BEGIN
 SET @changes+=@newHistoryCount;
 IF @preflightOnly=0
    INSERT dbo.__EFMigrationsHistory(MigrationId,ProductVersion)
    SELECT MigrationId,N'10.0.5' FROM @migrations WHERE WasApplied=0;
END;
IF @@TRANCOUNT<>1 OR XACT_STATE()<>1 OR COALESCE(APPLOCK_MODE(N'public',N'SatiDemo.FullReset',N'Transaction'),N'')<>N'Exclusive'
    THROW 54014, 'Migration transaction or reset exclusion was lost.', 1;
IF @preflightOnly=0 AND (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>132
    THROW 54011, 'The migration did not reach the reviewed 132-row history.', 1;
SELECT CONVERT(bit,CASE WHEN @newHistoryCount>0 AND @preflightOnly=0 THEN 1 ELSE 0 END) AS MigrationWasApplied,
 CONVERT(bit,CASE WHEN @changes>0 AND @preflightOnly=1 THEN 1 ELSE 0 END) AS ChangesRequired,
 (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount;
