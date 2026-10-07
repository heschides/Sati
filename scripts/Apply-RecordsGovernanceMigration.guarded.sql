-- Reviewed additive migration 127. Caller owns the entire transaction.
SET XACT_ABORT ON;
IF @@TRANCOUNT=0 THROW 53800, 'Caller must own the migration transaction.', 1;
IF DB_NAME()<>@expectedDatabase OR
   (@expectedDatabase<>N'SatiDemo' AND @expectedDatabase NOT LIKE N'SatiSyntheticPipeline[_]%') OR
   NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id=1 AND EnvironmentName=N'Demo')
    THROW 53800, 'Refusing an environment outside marked Demo or a private synthetic rehearsal.', 1;
DECLARE @migration nvarchar(150)=N'20261007004626_AddRecordsGovernance';
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 53801, 'Migration history is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@migration)
BEGIN
    IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>126 OR
       (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory)<>N'20261004204633_AddPayerBillingConfigurationVersions'
        THROW 53801, 'Expected exactly the reviewed 126-migration predecessor.', 1;
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name IN (N'RecordsGovernanceStates',N'RecordsHolds',N'RecordsHoldEvents',N'RecordsRetentionPolicies',N'RecordsRetentionPlans',N'RecordsRetentionBatches'))
        THROW 53802, 'Partial or untracked governance schema requires investigation.', 1;
CREATE TABLE [RecordsGovernanceStates] (
    [AgencyId] int NOT NULL,
    [Revision] int NOT NULL,
    CONSTRAINT [PK_RecordsGovernanceStates] PRIMARY KEY ([AgencyId]),
    CONSTRAINT [FK_RecordsGovernanceStates_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [RecordsHolds] (
    [Id] uniqueidentifier NOT NULL,
    [AgencyId] int NOT NULL,
    [Revision] int NOT NULL,
    [Scope] int NOT NULL,
    [RecordClass] int NULL,
    [PersonId] int NULL,
    [RecordId] nvarchar(80) NULL,
    [IsReleased] bit NOT NULL,
    [PlacedById] int NOT NULL,
    [ReleaseRequestedById] int NULL,
    [ReleaseRequestId] uniqueidentifier NULL,
    [LegacyHoldId] int NULL,
    CONSTRAINT [PK_RecordsHolds] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RecordsHolds_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RecordsHolds_Users_PlacedById] FOREIGN KEY ([PlacedById]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [RecordsRetentionPolicies] (
    [Id] bigint NOT NULL IDENTITY,
    [AgencyId] int NOT NULL,
    [OperationId] uniqueidentifier NOT NULL,
    [RequestHash] nvarchar(64) NOT NULL,
    [Version] int NOT NULL,
    [RecordClass] int NOT NULL,
    [RetentionDays] int NULL,
    [AuthorId] int NOT NULL,
    [RecordedAtUtc] datetime2 NOT NULL,
    [Reason] nvarchar(500) NOT NULL,
    CONSTRAINT [PK_RecordsRetentionPolicies] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RecordsRetentionPolicies_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RecordsRetentionPolicies_Users_AuthorId] FOREIGN KEY ([AuthorId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [RecordsHoldEvents] (
    [Id] bigint NOT NULL IDENTITY,
    [HoldId] uniqueidentifier NOT NULL,
    [AgencyId] int NOT NULL,
    [OperationId] uniqueidentifier NOT NULL,
    [RequestHash] nvarchar(64) NOT NULL,
    [Revision] int NOT NULL,
    [Action] int NOT NULL,
    [ActorId] int NOT NULL,
    [RecordedAtUtc] datetime2 NOT NULL,
    [Reason] nvarchar(500) NOT NULL,
    [CaseReference] nvarchar(100) NULL,
    [IssuedBy] nvarchar(150) NULL,
    CONSTRAINT [PK_RecordsHoldEvents] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RecordsHoldEvents_RecordsHolds_HoldId] FOREIGN KEY ([HoldId]) REFERENCES [RecordsHolds] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RecordsHoldEvents_Users_ActorId] FOREIGN KEY ([ActorId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [RecordsRetentionPlans] (
    [Id] uniqueidentifier NOT NULL,
    [AgencyId] int NOT NULL,
    [PolicyId] bigint NOT NULL,
    [GovernanceRevision] int NOT NULL,
    [PreparedAtUtc] datetime2 NOT NULL,
    [PreviewJson] nvarchar(max) NOT NULL,
    [CandidatesJson] nvarchar(max) NOT NULL,
    [Checkpoint] int NOT NULL,
    [Revision] int NOT NULL,
    [Completed] bit NOT NULL,
    CONSTRAINT [PK_RecordsRetentionPlans] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RecordsRetentionPlans_RecordsRetentionPolicies_PolicyId] FOREIGN KEY ([PolicyId]) REFERENCES [RecordsRetentionPolicies] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [RecordsRetentionBatches] (
    [Id] bigint NOT NULL IDENTITY,
    [AgencyId] int NOT NULL,
    [PlanId] uniqueidentifier NOT NULL,
    [OperationId] uniqueidentifier NOT NULL,
    [ActorId] int NOT NULL,
    [Checkpoint] int NOT NULL,
    [DeletedCount] int NOT NULL,
    [RecordedAtUtc] datetime2 NOT NULL,
    [PreservationJson] nvarchar(max) NOT NULL,
    [Completed] bit NOT NULL,
    CONSTRAINT [PK_RecordsRetentionBatches] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RecordsRetentionBatches_RecordsRetentionPlans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [RecordsRetentionPlans] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RecordsRetentionBatches_Users_ActorId] FOREIGN KEY ([ActorId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_RecordsHoldEvents_ActorId] ON [RecordsHoldEvents] ([ActorId]);

CREATE UNIQUE INDEX [IX_RecordsHoldEvents_AgencyId_OperationId] ON [RecordsHoldEvents] ([AgencyId], [OperationId]);

CREATE UNIQUE INDEX [IX_RecordsHoldEvents_HoldId_Revision] ON [RecordsHoldEvents] ([HoldId], [Revision]);

CREATE INDEX [IX_RecordsHolds_AgencyId_IsReleased] ON [RecordsHolds] ([AgencyId], [IsReleased]);

CREATE UNIQUE INDEX [IX_RecordsHolds_AgencyId_LegacyHoldId] ON [RecordsHolds] ([AgencyId], [LegacyHoldId]) WHERE [LegacyHoldId] IS NOT NULL;

CREATE INDEX [IX_RecordsHolds_PlacedById] ON [RecordsHolds] ([PlacedById]);

CREATE INDEX [IX_RecordsRetentionBatches_ActorId] ON [RecordsRetentionBatches] ([ActorId]);

CREATE UNIQUE INDEX [IX_RecordsRetentionBatches_AgencyId_OperationId] ON [RecordsRetentionBatches] ([AgencyId], [OperationId]);

CREATE UNIQUE INDEX [IX_RecordsRetentionBatches_PlanId_Checkpoint] ON [RecordsRetentionBatches] ([PlanId], [Checkpoint]);

CREATE INDEX [IX_RecordsRetentionPlans_PolicyId] ON [RecordsRetentionPlans] ([PolicyId]);

CREATE UNIQUE INDEX [IX_RecordsRetentionPolicies_AgencyId_OperationId] ON [RecordsRetentionPolicies] ([AgencyId], [OperationId]);

CREATE UNIQUE INDEX [IX_RecordsRetentionPolicies_AgencyId_RecordClass_Version] ON [RecordsRetentionPolicies] ([AgencyId], [RecordClass], [Version]);

CREATE INDEX [IX_RecordsRetentionPolicies_AuthorId] ON [RecordsRetentionPolicies] ([AuthorId]);

INSERT dbo.RecordsGovernanceStates(AgencyId, Revision)
SELECT DISTINCT AgencyId, 1 FROM dbo.LegalHolds WHERE IsReleased=0;
INSERT dbo.RecordsHolds(Id, AgencyId, Revision, Scope, RecordClass, PersonId, RecordId,
    IsReleased, PlacedById, ReleaseRequestedById, ReleaseRequestId, LegacyHoldId)
SELECT NEWID(), AgencyId, 1, 1, NULL, PersonId, NULL, 0, PlacedByUserId, NULL, NULL, Id
    FROM dbo.LegalHolds WHERE IsReleased=0;
INSERT dbo.RecordsHoldEvents(HoldId, AgencyId, OperationId, RequestHash, Revision, Action,
    ActorId, RecordedAtUtc, Reason, CaseReference, IssuedBy)
SELECT h.Id, h.AgencyId, NEWID(), REPLICATE(N'0',64), 1, 0, l.PlacedByUserId,
    l.PlacedAtUtc, l.Reason, l.CaseReference, l.IssuedBy
    FROM dbo.RecordsHolds h JOIN dbo.LegalHolds l ON l.Id=h.LegacyHoldId AND l.AgencyId=h.AgencyId;





END
ELSE IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>127 OR
    (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory)<>@migration
    THROW 53801, 'Expected exactly the reviewed 127-migration outcome.', 1;
IF EXISTS (SELECT * FROM (VALUES
(N'RecordsGovernanceStates',N'AgencyId',N'int',4,0,0),
(N'RecordsGovernanceStates',N'Revision',N'int',4,0,0),
(N'RecordsHolds',N'Id',N'uniqueidentifier',16,0,0),
(N'RecordsHolds',N'AgencyId',N'int',4,0,0),
(N'RecordsHolds',N'Revision',N'int',4,0,0),
(N'RecordsHolds',N'Scope',N'int',4,0,0),
(N'RecordsHolds',N'RecordClass',N'int',4,1,0),
(N'RecordsHolds',N'PersonId',N'int',4,1,0),
(N'RecordsHolds',N'RecordId',N'nvarchar',160,1,0),
(N'RecordsHolds',N'IsReleased',N'bit',1,0,0),
(N'RecordsHolds',N'PlacedById',N'int',4,0,0),
(N'RecordsHolds',N'ReleaseRequestedById',N'int',4,1,0),
(N'RecordsHolds',N'ReleaseRequestId',N'uniqueidentifier',16,1,0),
(N'RecordsHolds',N'LegacyHoldId',N'int',4,1,0),
(N'RecordsRetentionPolicies',N'Id',N'bigint',8,0,1),
(N'RecordsRetentionPolicies',N'AgencyId',N'int',4,0,0),
(N'RecordsRetentionPolicies',N'OperationId',N'uniqueidentifier',16,0,0),
(N'RecordsRetentionPolicies',N'RequestHash',N'nvarchar',128,0,0),
(N'RecordsRetentionPolicies',N'Version',N'int',4,0,0),
(N'RecordsRetentionPolicies',N'RecordClass',N'int',4,0,0),
(N'RecordsRetentionPolicies',N'RetentionDays',N'int',4,1,0),
(N'RecordsRetentionPolicies',N'AuthorId',N'int',4,0,0),
(N'RecordsRetentionPolicies',N'RecordedAtUtc',N'datetime2',8,0,0),
(N'RecordsRetentionPolicies',N'Reason',N'nvarchar',1000,0,0),
(N'RecordsHoldEvents',N'Id',N'bigint',8,0,1),
(N'RecordsHoldEvents',N'HoldId',N'uniqueidentifier',16,0,0),
(N'RecordsHoldEvents',N'AgencyId',N'int',4,0,0),
(N'RecordsHoldEvents',N'OperationId',N'uniqueidentifier',16,0,0),
(N'RecordsHoldEvents',N'RequestHash',N'nvarchar',128,0,0),
(N'RecordsHoldEvents',N'Revision',N'int',4,0,0),
(N'RecordsHoldEvents',N'Action',N'int',4,0,0),
(N'RecordsHoldEvents',N'ActorId',N'int',4,0,0),
(N'RecordsHoldEvents',N'RecordedAtUtc',N'datetime2',8,0,0),
(N'RecordsHoldEvents',N'Reason',N'nvarchar',1000,0,0),
(N'RecordsHoldEvents',N'CaseReference',N'nvarchar',200,1,0),
(N'RecordsHoldEvents',N'IssuedBy',N'nvarchar',300,1,0),
(N'RecordsRetentionPlans',N'Id',N'uniqueidentifier',16,0,0),
(N'RecordsRetentionPlans',N'AgencyId',N'int',4,0,0),
(N'RecordsRetentionPlans',N'PolicyId',N'bigint',8,0,0),
(N'RecordsRetentionPlans',N'GovernanceRevision',N'int',4,0,0),
(N'RecordsRetentionPlans',N'PreparedAtUtc',N'datetime2',8,0,0),
(N'RecordsRetentionPlans',N'PreviewJson',N'nvarchar',-1,0,0),
(N'RecordsRetentionPlans',N'CandidatesJson',N'nvarchar',-1,0,0),
(N'RecordsRetentionPlans',N'Checkpoint',N'int',4,0,0),
(N'RecordsRetentionPlans',N'Revision',N'int',4,0,0),
(N'RecordsRetentionPlans',N'Completed',N'bit',1,0,0),
(N'RecordsRetentionBatches',N'Id',N'bigint',8,0,1),
(N'RecordsRetentionBatches',N'AgencyId',N'int',4,0,0),
(N'RecordsRetentionBatches',N'PlanId',N'uniqueidentifier',16,0,0),
(N'RecordsRetentionBatches',N'OperationId',N'uniqueidentifier',16,0,0),
(N'RecordsRetentionBatches',N'ActorId',N'int',4,0,0),
(N'RecordsRetentionBatches',N'Checkpoint',N'int',4,0,0),
(N'RecordsRetentionBatches',N'DeletedCount',N'int',4,0,0),
(N'RecordsRetentionBatches',N'RecordedAtUtc',N'datetime2',8,0,0),
(N'RecordsRetentionBatches',N'PreservationJson',N'nvarchar',-1,0,0),
(N'RecordsRetentionBatches',N'Completed',N'bit',1,0,0)
) expected(tableName,columnName,typeName,length,nullableColumn,identityColumn)
WHERE NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
WHERE c.object_id=OBJECT_ID(N'dbo.'+expected.tableName) AND c.name=expected.columnName
AND t.name=expected.typeName AND c.max_length=expected.length AND c.is_nullable=expected.nullableColumn
AND c.is_identity=expected.identityColumn AND c.is_computed=0 AND (expected.typeName<>N'datetime2' OR c.scale=7)))
OR EXISTS (SELECT * FROM (VALUES
(N'RecordsGovernanceStates',2),
(N'RecordsHolds',12),
(N'RecordsRetentionPolicies',10),
(N'RecordsHoldEvents',12),
(N'RecordsRetentionPlans',10),
(N'RecordsRetentionBatches',10)
) expected(tableName,columnCount)
WHERE (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.'+expected.tableName))<>expected.columnCount)
    THROW 53802, 'Governance columns differ from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.RecordsHoldEvents') AND i.name=N'IX_RecordsHoldEvents_ActorId' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'ActorId') THROW 53802, 'Governance index differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.RecordsHoldEvents') AND i.name=N'IX_RecordsHoldEvents_AgencyId_OperationId' AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'AgencyId,OperationId') THROW 53802, 'Governance index differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.RecordsHoldEvents') AND i.name=N'IX_RecordsHoldEvents_HoldId_Revision' AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'HoldId,Revision') THROW 53802, 'Governance index differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.RecordsHolds') AND i.name=N'IX_RecordsHolds_AgencyId_IsReleased' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'AgencyId,IsReleased') THROW 53802, 'Governance index differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.RecordsHolds') AND i.name=N'IX_RecordsHolds_AgencyId_LegacyHoldId' AND i.is_unique=1 AND i.is_disabled=0 AND REPLACE(REPLACE(i.filter_definition,N'(',N''),N')',N'')=N'[LegacyHoldId] IS NOT NULL' AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'AgencyId,LegacyHoldId') THROW 53802, 'Governance index differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.RecordsHolds') AND i.name=N'IX_RecordsHolds_PlacedById' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'PlacedById') THROW 53802, 'Governance index differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.RecordsRetentionBatches') AND i.name=N'IX_RecordsRetentionBatches_ActorId' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'ActorId') THROW 53802, 'Governance index differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.RecordsRetentionBatches') AND i.name=N'IX_RecordsRetentionBatches_AgencyId_OperationId' AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'AgencyId,OperationId') THROW 53802, 'Governance index differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.RecordsRetentionBatches') AND i.name=N'IX_RecordsRetentionBatches_PlanId_Checkpoint' AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'PlanId,Checkpoint') THROW 53802, 'Governance index differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.RecordsRetentionPlans') AND i.name=N'IX_RecordsRetentionPlans_PolicyId' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'PolicyId') THROW 53802, 'Governance index differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.RecordsRetentionPolicies') AND i.name=N'IX_RecordsRetentionPolicies_AgencyId_OperationId' AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'AgencyId,OperationId') THROW 53802, 'Governance index differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.RecordsRetentionPolicies') AND i.name=N'IX_RecordsRetentionPolicies_AgencyId_RecordClass_Version' AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'AgencyId,RecordClass,Version') THROW 53802, 'Governance index differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.RecordsRetentionPolicies') AND i.name=N'IX_RecordsRetentionPolicies_AuthorId' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'AuthorId') THROW 53802, 'Governance index differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_RecordsGovernanceStates_Agencies_AgencyId' AND fk.parent_object_id=OBJECT_ID(N'dbo.RecordsGovernanceStates') AND c.name=N'AgencyId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.Agencies') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53802, 'Governance ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'dbo.RecordsGovernanceStates') AND i.name=N'PK_RecordsGovernanceStates' AND i.is_primary_key=1 AND i.is_unique=1 AND i.is_disabled=0 AND c.name=N'AgencyId' AND ic.key_ordinal=1) THROW 53802, 'Governance primary key differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_RecordsHolds_Agencies_AgencyId' AND fk.parent_object_id=OBJECT_ID(N'dbo.RecordsHolds') AND c.name=N'AgencyId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.Agencies') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53802, 'Governance ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_RecordsHolds_Users_PlacedById' AND fk.parent_object_id=OBJECT_ID(N'dbo.RecordsHolds') AND c.name=N'PlacedById' AND fc.referenced_object_id=OBJECT_ID(N'dbo.Users') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53802, 'Governance ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'dbo.RecordsHolds') AND i.name=N'PK_RecordsHolds' AND i.is_primary_key=1 AND i.is_unique=1 AND i.is_disabled=0 AND c.name=N'Id' AND ic.key_ordinal=1) THROW 53802, 'Governance primary key differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_RecordsRetentionPolicies_Agencies_AgencyId' AND fk.parent_object_id=OBJECT_ID(N'dbo.RecordsRetentionPolicies') AND c.name=N'AgencyId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.Agencies') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53802, 'Governance ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_RecordsRetentionPolicies_Users_AuthorId' AND fk.parent_object_id=OBJECT_ID(N'dbo.RecordsRetentionPolicies') AND c.name=N'AuthorId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.Users') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53802, 'Governance ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'dbo.RecordsRetentionPolicies') AND i.name=N'PK_RecordsRetentionPolicies' AND i.is_primary_key=1 AND i.is_unique=1 AND i.is_disabled=0 AND c.name=N'Id' AND ic.key_ordinal=1) THROW 53802, 'Governance primary key differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_RecordsHoldEvents_RecordsHolds_HoldId' AND fk.parent_object_id=OBJECT_ID(N'dbo.RecordsHoldEvents') AND c.name=N'HoldId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.RecordsHolds') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53802, 'Governance ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_RecordsHoldEvents_Users_ActorId' AND fk.parent_object_id=OBJECT_ID(N'dbo.RecordsHoldEvents') AND c.name=N'ActorId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.Users') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53802, 'Governance ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'dbo.RecordsHoldEvents') AND i.name=N'PK_RecordsHoldEvents' AND i.is_primary_key=1 AND i.is_unique=1 AND i.is_disabled=0 AND c.name=N'Id' AND ic.key_ordinal=1) THROW 53802, 'Governance primary key differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_RecordsRetentionPlans_RecordsRetentionPolicies_PolicyId' AND fk.parent_object_id=OBJECT_ID(N'dbo.RecordsRetentionPlans') AND c.name=N'PolicyId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.RecordsRetentionPolicies') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53802, 'Governance ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'dbo.RecordsRetentionPlans') AND i.name=N'PK_RecordsRetentionPlans' AND i.is_primary_key=1 AND i.is_unique=1 AND i.is_disabled=0 AND c.name=N'Id' AND ic.key_ordinal=1) THROW 53802, 'Governance primary key differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_RecordsRetentionBatches_RecordsRetentionPlans_PlanId' AND fk.parent_object_id=OBJECT_ID(N'dbo.RecordsRetentionBatches') AND c.name=N'PlanId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.RecordsRetentionPlans') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53802, 'Governance ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_RecordsRetentionBatches_Users_ActorId' AND fk.parent_object_id=OBJECT_ID(N'dbo.RecordsRetentionBatches') AND c.name=N'ActorId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.Users') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53802, 'Governance ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'dbo.RecordsRetentionBatches') AND i.name=N'PK_RecordsRetentionBatches' AND i.is_primary_key=1 AND i.is_unique=1 AND i.is_disabled=0 AND c.name=N'Id' AND ic.key_ordinal=1) THROW 53802, 'Governance primary key differs from the reviewed model.', 1;
IF EXISTS (SELECT 1 FROM dbo.LegalHolds l WHERE l.IsReleased=0 AND NOT EXISTS
(SELECT 1 FROM dbo.RecordsHolds h JOIN dbo.RecordsHoldEvents e ON e.HoldId=h.Id AND e.Revision=1
 WHERE h.LegacyHoldId=l.Id AND h.AgencyId=l.AgencyId AND h.PersonId=l.PersonId AND h.IsReleased=0
 AND e.ActorId=l.PlacedByUserId AND e.RecordedAtUtc=l.PlacedAtUtc AND e.Reason=l.Reason))
    THROW 53803, 'Active legacy preservation was not imported intact.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@migration)
    INSERT dbo.__EFMigrationsHistory(MigrationId,ProductVersion) VALUES(@migration,N'10.0.5');