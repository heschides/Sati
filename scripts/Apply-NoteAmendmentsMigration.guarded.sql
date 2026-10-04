-- Generated offline by Build-NoteAmendmentGuard.py; review before use.
-- Caller owns the transaction, sets @expectedDatabase and holds SatiDemo.FullReset.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME()<>@expectedDatabase OR NOT EXISTS
 (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id=1 AND EnvironmentName=N'Demo')
 THROW 53600, 'Database identity mismatch.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
 WHERE MigrationId=N'20261002221023_AddScheduledNoteMoves')
 THROW 53601, 'Predecessor migration missing.', 1;
DECLARE @applied bit=CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
 WHERE MigrationId=N'20261004120026_AddNoteAmendments') THEN 1 ELSE 0 END;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>124+CONVERT(int,@applied)
 THROW 53602, 'Unexpected migration boundary.', 1;

IF COL_LENGTH(N'dbo.ClaimLines',N'AmendedNoteVersionId') IS NULL
EXEC(N'ALTER TABLE [ClaimLines] ADD [AmendedNoteVersionId] bigint NULL;');

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.ClaimLines') AND c.name=N'AmendedNoteVersionId' AND t.name=N'bigint'
 AND c.max_length=8 AND c.scale=0 AND c.is_nullable=1
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column ClaimLines.AmendedNoteVersionId.', 1;

IF COL_LENGTH(N'dbo.ClaimCorrections',N'AmendedNoteVersionId') IS NULL
EXEC(N'ALTER TABLE [ClaimCorrections] ADD [AmendedNoteVersionId] bigint NULL;');

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.ClaimCorrections') AND c.name=N'AmendedNoteVersionId' AND t.name=N'bigint'
 AND c.max_length=8 AND c.scale=0 AND c.is_nullable=1
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column ClaimCorrections.AmendedNoteVersionId.', 1;

IF COL_LENGTH(N'dbo.ClaimCorrections',N'CorrectedChargeAmount') IS NULL
EXEC(N'ALTER TABLE [ClaimCorrections] ADD [CorrectedChargeAmount] decimal(18,2) NULL;');

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.ClaimCorrections') AND c.name=N'CorrectedChargeAmount' AND t.name=N'decimal'
 AND c.max_length=9 AND c.scale=2 AND c.is_nullable=1
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0 AND c.precision=18)
 THROW 53603, 'Incompatible column ClaimCorrections.CorrectedChargeAmount.', 1;

IF COL_LENGTH(N'dbo.ClaimCorrections',N'CorrectedDateOfService') IS NULL
EXEC(N'ALTER TABLE [ClaimCorrections] ADD [CorrectedDateOfService] datetime2 NULL;');

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.ClaimCorrections') AND c.name=N'CorrectedDateOfService' AND t.name=N'datetime2'
 AND c.max_length=8 AND c.scale=7 AND c.is_nullable=1
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column ClaimCorrections.CorrectedDateOfService.', 1;

IF COL_LENGTH(N'dbo.ClaimCorrections',N'CorrectedUnits') IS NULL
EXEC(N'ALTER TABLE [ClaimCorrections] ADD [CorrectedUnits] decimal(18,2) NULL;');

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.ClaimCorrections') AND c.name=N'CorrectedUnits' AND t.name=N'decimal'
 AND c.max_length=9 AND c.scale=2 AND c.is_nullable=1
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0 AND c.precision=18)
 THROW 53603, 'Incompatible column ClaimCorrections.CorrectedUnits.', 1;

IF OBJECT_ID(N'dbo.NoteAmendments') IS NOT NULL AND OBJECT_ID(N'dbo.NoteAmendments',N'U') IS NULL
 THROW 53603, 'Table name occupied.', 1;

IF OBJECT_ID(N'dbo.NoteAmendments',N'U') IS NULL
EXEC(N'CREATE TABLE [NoteAmendments] (
        [Id] uniqueidentifier NOT NULL,
        [AgencyId] int NOT NULL,
        [NoteId] int NOT NULL,
        [AuthorId] int NOT NULL,
        [OriginalNoteRevision] int NOT NULL,
        [OriginalSnapshotJson] nvarchar(max) NOT NULL,
        [BaseApprovedVersionId] bigint NULL,
        [Revision] int NOT NULL,
        [Status] int NOT NULL,
        [CurrentVersionId] bigint NOT NULL,
        [SubmittedVersionId] bigint NULL,
        [ApprovedVersionId] bigint NULL,
        [ChangesFinancialFacts] bit NOT NULL,
        CONSTRAINT [PK_NoteAmendments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NoteAmendments_Notes_NoteId] FOREIGN KEY ([NoteId]) REFERENCES [Notes] ([Id]) ON DELETE NO ACTION
    );');

IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.NoteAmendments'))<>13
 THROW 53603, 'Unexpected columns in NoteAmendments.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND c.name=N'Id' AND t.name=N'uniqueidentifier'
 AND c.max_length=16 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendments.Id.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND c.name=N'AgencyId' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendments.AgencyId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND c.name=N'NoteId' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendments.NoteId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND c.name=N'AuthorId' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendments.AuthorId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND c.name=N'OriginalNoteRevision' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendments.OriginalNoteRevision.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND c.name=N'OriginalSnapshotJson' AND t.name=N'nvarchar'
 AND c.max_length=-1 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendments.OriginalSnapshotJson.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND c.name=N'BaseApprovedVersionId' AND t.name=N'bigint'
 AND c.max_length=8 AND c.scale=0 AND c.is_nullable=1
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendments.BaseApprovedVersionId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND c.name=N'Revision' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendments.Revision.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND c.name=N'Status' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendments.Status.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND c.name=N'CurrentVersionId' AND t.name=N'bigint'
 AND c.max_length=8 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendments.CurrentVersionId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND c.name=N'SubmittedVersionId' AND t.name=N'bigint'
 AND c.max_length=8 AND c.scale=0 AND c.is_nullable=1
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendments.SubmittedVersionId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND c.name=N'ApprovedVersionId' AND t.name=N'bigint'
 AND c.max_length=8 AND c.scale=0 AND c.is_nullable=1
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendments.ApprovedVersionId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND c.name=N'ChangesFinancialFacts' AND t.name=N'bit'
 AND c.max_length=1 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendments.ChangesFinancialFacts.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.NoteAmendments')
 AND i.name=N'PK_NoteAmendments' AND i.is_primary_key=1 AND i.is_unique=1 AND i.type=1 AND i.is_disabled=0
 AND (SELECT COUNT(*) FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id)=1
 AND EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND c.key_ordinal=1 AND c.is_descending_key=0 AND COL_NAME(c.object_id,c.column_id)=N'Id'))
 THROW 53603, 'Incompatible primary key PK_NoteAmendments.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c
 ON c.constraint_object_id=f.object_id WHERE f.parent_object_id=OBJECT_ID(N'dbo.NoteAmendments')
 AND f.name=N'FK_NoteAmendments_Notes_NoteId' AND f.referenced_object_id=OBJECT_ID(N'dbo.Notes')
 AND f.delete_referential_action=0 AND f.update_referential_action=0
 AND f.is_disabled=0 AND f.is_not_trusted=0
 AND COL_NAME(f.parent_object_id,c.parent_column_id)=N'NoteId'
 AND COL_NAME(f.referenced_object_id,c.referenced_column_id)=N'Id'
 AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1)
 THROW 53603, 'Incompatible foreign key FK_NoteAmendments_Notes_NoteId.', 1;

IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.NoteAmendments'))<>1
 THROW 53603, 'Unexpected table relationships.', 1;

IF OBJECT_ID(N'dbo.NoteAmendmentVersions') IS NOT NULL AND OBJECT_ID(N'dbo.NoteAmendmentVersions',N'U') IS NULL
 THROW 53603, 'Table name occupied.', 1;

IF OBJECT_ID(N'dbo.NoteAmendmentVersions',N'U') IS NULL
EXEC(N'CREATE TABLE [NoteAmendmentVersions] (
        [Id] bigint NOT NULL IDENTITY,
        [AmendmentId] uniqueidentifier NOT NULL,
        [Number] int NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [ContentJson] nvarchar(max) NOT NULL,
        [FinancialContentJson] nvarchar(1000) NOT NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [RecordedById] int NOT NULL,
        [RecordedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_NoteAmendmentVersions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NoteAmendmentVersions_NoteAmendments_AmendmentId] FOREIGN KEY ([AmendmentId]) REFERENCES [NoteAmendments] ([Id]) ON DELETE NO ACTION
    );');

IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions'))<>9
 THROW 53603, 'Unexpected columns in NoteAmendmentVersions.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions') AND c.name=N'Id' AND t.name=N'bigint'
 AND c.max_length=8 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=1 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentVersions.Id.', 1;

IF IDENT_SEED(N'dbo.NoteAmendmentVersions')<>1 OR IDENT_INCR(N'dbo.NoteAmendmentVersions')<>1
 THROW 53603, 'Unexpected identity seed.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions') AND c.name=N'AmendmentId' AND t.name=N'uniqueidentifier'
 AND c.max_length=16 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentVersions.AmendmentId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions') AND c.name=N'Number' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentVersions.Number.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions') AND c.name=N'Kind' AND t.name=N'nvarchar'
 AND c.max_length=40 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentVersions.Kind.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions') AND c.name=N'ContentJson' AND t.name=N'nvarchar'
 AND c.max_length=-1 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentVersions.ContentJson.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions') AND c.name=N'FinancialContentJson' AND t.name=N'nvarchar'
 AND c.max_length=2000 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentVersions.FinancialContentJson.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions') AND c.name=N'Reason' AND t.name=N'nvarchar'
 AND c.max_length=2000 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentVersions.Reason.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions') AND c.name=N'RecordedById' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentVersions.RecordedById.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions') AND c.name=N'RecordedAtUtc' AND t.name=N'datetime2'
 AND c.max_length=8 AND c.scale=7 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentVersions.RecordedAtUtc.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions')
 AND i.name=N'PK_NoteAmendmentVersions' AND i.is_primary_key=1 AND i.is_unique=1 AND i.type=1 AND i.is_disabled=0
 AND (SELECT COUNT(*) FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id)=1
 AND EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND c.key_ordinal=1 AND c.is_descending_key=0 AND COL_NAME(c.object_id,c.column_id)=N'Id'))
 THROW 53603, 'Incompatible primary key PK_NoteAmendmentVersions.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c
 ON c.constraint_object_id=f.object_id WHERE f.parent_object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions')
 AND f.name=N'FK_NoteAmendmentVersions_NoteAmendments_AmendmentId' AND f.referenced_object_id=OBJECT_ID(N'dbo.NoteAmendments')
 AND f.delete_referential_action=0 AND f.update_referential_action=0
 AND f.is_disabled=0 AND f.is_not_trusted=0
 AND COL_NAME(f.parent_object_id,c.parent_column_id)=N'AmendmentId'
 AND COL_NAME(f.referenced_object_id,c.referenced_column_id)=N'Id'
 AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1)
 THROW 53603, 'Incompatible foreign key FK_NoteAmendmentVersions_NoteAmendments_AmendmentId.', 1;

IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions'))<>1
 THROW 53603, 'Unexpected table relationships.', 1;

IF OBJECT_ID(N'dbo.NoteAmendmentEvents') IS NOT NULL AND OBJECT_ID(N'dbo.NoteAmendmentEvents',N'U') IS NULL
 THROW 53603, 'Table name occupied.', 1;

IF OBJECT_ID(N'dbo.NoteAmendmentEvents',N'U') IS NULL
EXEC(N'CREATE TABLE [NoteAmendmentEvents] (
        [Id] bigint NOT NULL IDENTITY,
        [AmendmentId] uniqueidentifier NOT NULL,
        [AgencyId] int NOT NULL,
        [ActorId] int NOT NULL,
        [OperationId] uniqueidentifier NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        [Action] int NOT NULL,
        [VersionId] bigint NOT NULL,
        [ResponseJson] nvarchar(max) NOT NULL,
        [ReviewReason] nvarchar(1000) NULL,
        [RecordedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_NoteAmendmentEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NoteAmendmentEvents_NoteAmendmentVersions_VersionId] FOREIGN KEY ([VersionId]) REFERENCES [NoteAmendmentVersions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_NoteAmendmentEvents_NoteAmendments_AmendmentId] FOREIGN KEY ([AmendmentId]) REFERENCES [NoteAmendments] ([Id]) ON DELETE NO ACTION
    );');

IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents'))<>11
 THROW 53603, 'Unexpected columns in NoteAmendmentEvents.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND c.name=N'Id' AND t.name=N'bigint'
 AND c.max_length=8 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=1 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentEvents.Id.', 1;

IF IDENT_SEED(N'dbo.NoteAmendmentEvents')<>1 OR IDENT_INCR(N'dbo.NoteAmendmentEvents')<>1
 THROW 53603, 'Unexpected identity seed.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND c.name=N'AmendmentId' AND t.name=N'uniqueidentifier'
 AND c.max_length=16 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentEvents.AmendmentId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND c.name=N'AgencyId' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentEvents.AgencyId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND c.name=N'ActorId' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentEvents.ActorId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND c.name=N'OperationId' AND t.name=N'uniqueidentifier'
 AND c.max_length=16 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentEvents.OperationId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND c.name=N'RequestHash' AND t.name=N'nvarchar'
 AND c.max_length=128 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentEvents.RequestHash.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND c.name=N'Action' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentEvents.Action.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND c.name=N'VersionId' AND t.name=N'bigint'
 AND c.max_length=8 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentEvents.VersionId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND c.name=N'ResponseJson' AND t.name=N'nvarchar'
 AND c.max_length=-1 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentEvents.ResponseJson.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND c.name=N'ReviewReason' AND t.name=N'nvarchar'
 AND c.max_length=2000 AND c.scale=0 AND c.is_nullable=1
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentEvents.ReviewReason.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND c.name=N'RecordedAtUtc' AND t.name=N'datetime2'
 AND c.max_length=8 AND c.scale=7 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentEvents.RecordedAtUtc.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents')
 AND i.name=N'PK_NoteAmendmentEvents' AND i.is_primary_key=1 AND i.is_unique=1 AND i.type=1 AND i.is_disabled=0
 AND (SELECT COUNT(*) FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id)=1
 AND EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND c.key_ordinal=1 AND c.is_descending_key=0 AND COL_NAME(c.object_id,c.column_id)=N'Id'))
 THROW 53603, 'Incompatible primary key PK_NoteAmendmentEvents.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c
 ON c.constraint_object_id=f.object_id WHERE f.parent_object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents')
 AND f.name=N'FK_NoteAmendmentEvents_NoteAmendmentVersions_VersionId' AND f.referenced_object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions')
 AND f.delete_referential_action=0 AND f.update_referential_action=0
 AND f.is_disabled=0 AND f.is_not_trusted=0
 AND COL_NAME(f.parent_object_id,c.parent_column_id)=N'VersionId'
 AND COL_NAME(f.referenced_object_id,c.referenced_column_id)=N'Id'
 AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1)
 THROW 53603, 'Incompatible foreign key FK_NoteAmendmentEvents_NoteAmendmentVersions_VersionId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c
 ON c.constraint_object_id=f.object_id WHERE f.parent_object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents')
 AND f.name=N'FK_NoteAmendmentEvents_NoteAmendments_AmendmentId' AND f.referenced_object_id=OBJECT_ID(N'dbo.NoteAmendments')
 AND f.delete_referential_action=0 AND f.update_referential_action=0
 AND f.is_disabled=0 AND f.is_not_trusted=0
 AND COL_NAME(f.parent_object_id,c.parent_column_id)=N'AmendmentId'
 AND COL_NAME(f.referenced_object_id,c.referenced_column_id)=N'Id'
 AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1)
 THROW 53603, 'Incompatible foreign key FK_NoteAmendmentEvents_NoteAmendments_AmendmentId.', 1;

IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents'))<>2
 THROW 53603, 'Unexpected table relationships.', 1;

IF OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') IS NOT NULL AND OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews',N'U') IS NULL
 THROW 53603, 'Table name occupied.', 1;

IF OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews',N'U') IS NULL
EXEC(N'CREATE TABLE [NoteAmendmentFinancialReviews] (
        [Id] bigint NOT NULL IDENTITY,
        [AgencyId] int NOT NULL,
        [NoteId] int NOT NULL,
        [ApprovedVersionId] bigint NOT NULL,
        [ReviewedById] int NOT NULL,
        [ReviewedAtUtc] datetime2 NOT NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [OperationId] uniqueidentifier NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        CONSTRAINT [PK_NoteAmendmentFinancialReviews] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NoteAmendmentFinancialReviews_NoteAmendmentVersions_ApprovedVersionId] FOREIGN KEY ([ApprovedVersionId]) REFERENCES [NoteAmendmentVersions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_NoteAmendmentFinancialReviews_Notes_NoteId] FOREIGN KEY ([NoteId]) REFERENCES [Notes] ([Id]) ON DELETE NO ACTION
    );');

IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews'))<>9
 THROW 53603, 'Unexpected columns in NoteAmendmentFinancialReviews.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND c.name=N'Id' AND t.name=N'bigint'
 AND c.max_length=8 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=1 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentFinancialReviews.Id.', 1;

IF IDENT_SEED(N'dbo.NoteAmendmentFinancialReviews')<>1 OR IDENT_INCR(N'dbo.NoteAmendmentFinancialReviews')<>1
 THROW 53603, 'Unexpected identity seed.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND c.name=N'AgencyId' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentFinancialReviews.AgencyId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND c.name=N'NoteId' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentFinancialReviews.NoteId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND c.name=N'ApprovedVersionId' AND t.name=N'bigint'
 AND c.max_length=8 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentFinancialReviews.ApprovedVersionId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND c.name=N'ReviewedById' AND t.name=N'int'
 AND c.max_length=4 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentFinancialReviews.ReviewedById.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND c.name=N'ReviewedAtUtc' AND t.name=N'datetime2'
 AND c.max_length=8 AND c.scale=7 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentFinancialReviews.ReviewedAtUtc.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND c.name=N'Reason' AND t.name=N'nvarchar'
 AND c.max_length=2000 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentFinancialReviews.Reason.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND c.name=N'OperationId' AND t.name=N'uniqueidentifier'
 AND c.max_length=16 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentFinancialReviews.OperationId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND c.name=N'RequestHash' AND t.name=N'nvarchar'
 AND c.max_length=128 AND c.scale=0 AND c.is_nullable=0
 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0)
 THROW 53603, 'Incompatible column NoteAmendmentFinancialReviews.RequestHash.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews')
 AND i.name=N'PK_NoteAmendmentFinancialReviews' AND i.is_primary_key=1 AND i.is_unique=1 AND i.type=1 AND i.is_disabled=0
 AND (SELECT COUNT(*) FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id)=1
 AND EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND c.key_ordinal=1 AND c.is_descending_key=0 AND COL_NAME(c.object_id,c.column_id)=N'Id'))
 THROW 53603, 'Incompatible primary key PK_NoteAmendmentFinancialReviews.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c
 ON c.constraint_object_id=f.object_id WHERE f.parent_object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews')
 AND f.name=N'FK_NoteAmendmentFinancialReviews_NoteAmendmentVersions_ApprovedVersionId' AND f.referenced_object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions')
 AND f.delete_referential_action=0 AND f.update_referential_action=0
 AND f.is_disabled=0 AND f.is_not_trusted=0
 AND COL_NAME(f.parent_object_id,c.parent_column_id)=N'ApprovedVersionId'
 AND COL_NAME(f.referenced_object_id,c.referenced_column_id)=N'Id'
 AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1)
 THROW 53603, 'Incompatible foreign key FK_NoteAmendmentFinancialReviews_NoteAmendmentVersions_ApprovedVersionId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c
 ON c.constraint_object_id=f.object_id WHERE f.parent_object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews')
 AND f.name=N'FK_NoteAmendmentFinancialReviews_Notes_NoteId' AND f.referenced_object_id=OBJECT_ID(N'dbo.Notes')
 AND f.delete_referential_action=0 AND f.update_referential_action=0
 AND f.is_disabled=0 AND f.is_not_trusted=0
 AND COL_NAME(f.parent_object_id,c.parent_column_id)=N'NoteId'
 AND COL_NAME(f.referenced_object_id,c.referenced_column_id)=N'Id'
 AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1)
 THROW 53603, 'Incompatible foreign key FK_NoteAmendmentFinancialReviews_Notes_NoteId.', 1;

IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews'))<>2
 THROW 53603, 'Unexpected table relationships.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ClaimLines') AND name=N'IX_ClaimLines_AmendedNoteVersionId')
EXEC(N'CREATE INDEX [IX_ClaimLines_AmendedNoteVersionId] ON [ClaimLines] ([AmendedNoteVersionId]);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.ClaimLines') AND i.name=N'IX_ClaimLines_AmendedNoteVersionId'
 AND i.is_unique=0 AND i.type=2 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(c.object_id,c.column_id)),N',') WITHIN GROUP(ORDER BY c.key_ordinal)
 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=N'AmendedNoteVersionId'
 AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND (c.is_included_column=1 OR c.is_descending_key=1)))
 THROW 53603, 'Incompatible index IX_ClaimLines_AmendedNoteVersionId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ClaimCorrections') AND name=N'IX_ClaimCorrections_AmendedNoteVersionId')
EXEC(N'CREATE INDEX [IX_ClaimCorrections_AmendedNoteVersionId] ON [ClaimCorrections] ([AmendedNoteVersionId]);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.ClaimCorrections') AND i.name=N'IX_ClaimCorrections_AmendedNoteVersionId'
 AND i.is_unique=0 AND i.type=2 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(c.object_id,c.column_id)),N',') WITHIN GROUP(ORDER BY c.key_ordinal)
 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=N'AmendedNoteVersionId'
 AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND (c.is_included_column=1 OR c.is_descending_key=1)))
 THROW 53603, 'Incompatible index IX_ClaimCorrections_AmendedNoteVersionId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND name=N'IX_NoteAmendmentEvents_AgencyId_ActorId_OperationId')
EXEC(N'CREATE UNIQUE INDEX [IX_NoteAmendmentEvents_AgencyId_ActorId_OperationId] ON [NoteAmendmentEvents] ([AgencyId], [ActorId], [OperationId]);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND i.name=N'IX_NoteAmendmentEvents_AgencyId_ActorId_OperationId'
 AND i.is_unique=1 AND i.type=2 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(c.object_id,c.column_id)),N',') WITHIN GROUP(ORDER BY c.key_ordinal)
 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=N'AgencyId,ActorId,OperationId'
 AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND (c.is_included_column=1 OR c.is_descending_key=1)))
 THROW 53603, 'Incompatible index IX_NoteAmendmentEvents_AgencyId_ActorId_OperationId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND name=N'IX_NoteAmendmentEvents_AmendmentId')
EXEC(N'CREATE INDEX [IX_NoteAmendmentEvents_AmendmentId] ON [NoteAmendmentEvents] ([AmendmentId]);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND i.name=N'IX_NoteAmendmentEvents_AmendmentId'
 AND i.is_unique=0 AND i.type=2 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(c.object_id,c.column_id)),N',') WITHIN GROUP(ORDER BY c.key_ordinal)
 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=N'AmendmentId'
 AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND (c.is_included_column=1 OR c.is_descending_key=1)))
 THROW 53603, 'Incompatible index IX_NoteAmendmentEvents_AmendmentId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND name=N'IX_NoteAmendmentEvents_VersionId')
EXEC(N'CREATE INDEX [IX_NoteAmendmentEvents_VersionId] ON [NoteAmendmentEvents] ([VersionId]);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.NoteAmendmentEvents') AND i.name=N'IX_NoteAmendmentEvents_VersionId'
 AND i.is_unique=0 AND i.type=2 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(c.object_id,c.column_id)),N',') WITHIN GROUP(ORDER BY c.key_ordinal)
 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=N'VersionId'
 AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND (c.is_included_column=1 OR c.is_descending_key=1)))
 THROW 53603, 'Incompatible index IX_NoteAmendmentEvents_VersionId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND name=N'IX_NoteAmendmentFinancialReviews_AgencyId_ReviewedById_OperationId')
EXEC(N'CREATE UNIQUE INDEX [IX_NoteAmendmentFinancialReviews_AgencyId_ReviewedById_OperationId] ON [NoteAmendmentFinancialReviews] ([AgencyId], [ReviewedById], [OperationId]);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND i.name=N'IX_NoteAmendmentFinancialReviews_AgencyId_ReviewedById_OperationId'
 AND i.is_unique=1 AND i.type=2 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(c.object_id,c.column_id)),N',') WITHIN GROUP(ORDER BY c.key_ordinal)
 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=N'AgencyId,ReviewedById,OperationId'
 AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND (c.is_included_column=1 OR c.is_descending_key=1)))
 THROW 53603, 'Incompatible index IX_NoteAmendmentFinancialReviews_AgencyId_ReviewedById_OperationId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND name=N'IX_NoteAmendmentFinancialReviews_ApprovedVersionId')
EXEC(N'CREATE UNIQUE INDEX [IX_NoteAmendmentFinancialReviews_ApprovedVersionId] ON [NoteAmendmentFinancialReviews] ([ApprovedVersionId]);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND i.name=N'IX_NoteAmendmentFinancialReviews_ApprovedVersionId'
 AND i.is_unique=1 AND i.type=2 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(c.object_id,c.column_id)),N',') WITHIN GROUP(ORDER BY c.key_ordinal)
 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=N'ApprovedVersionId'
 AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND (c.is_included_column=1 OR c.is_descending_key=1)))
 THROW 53603, 'Incompatible index IX_NoteAmendmentFinancialReviews_ApprovedVersionId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND name=N'IX_NoteAmendmentFinancialReviews_NoteId')
EXEC(N'CREATE INDEX [IX_NoteAmendmentFinancialReviews_NoteId] ON [NoteAmendmentFinancialReviews] ([NoteId]);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.NoteAmendmentFinancialReviews') AND i.name=N'IX_NoteAmendmentFinancialReviews_NoteId'
 AND i.is_unique=0 AND i.type=2 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(c.object_id,c.column_id)),N',') WITHIN GROUP(ORDER BY c.key_ordinal)
 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=N'NoteId'
 AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND (c.is_included_column=1 OR c.is_descending_key=1)))
 THROW 53603, 'Incompatible index IX_NoteAmendmentFinancialReviews_NoteId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NoteAmendments') AND name=N'IX_NoteAmendments_AgencyId_NoteId')
EXEC(N'CREATE INDEX [IX_NoteAmendments_AgencyId_NoteId] ON [NoteAmendments] ([AgencyId], [NoteId]);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND i.name=N'IX_NoteAmendments_AgencyId_NoteId'
 AND i.is_unique=0 AND i.type=2 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(c.object_id,c.column_id)),N',') WITHIN GROUP(ORDER BY c.key_ordinal)
 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=N'AgencyId,NoteId'
 AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND (c.is_included_column=1 OR c.is_descending_key=1)))
 THROW 53603, 'Incompatible index IX_NoteAmendments_AgencyId_NoteId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NoteAmendments') AND name=N'IX_NoteAmendments_NoteId')
EXEC(N'CREATE UNIQUE INDEX [IX_NoteAmendments_NoteId] ON [NoteAmendments] ([NoteId]) WHERE [Status] IN (0, 1, 2)');

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.NoteAmendments') AND i.name=N'IX_NoteAmendments_NoteId'
 AND i.is_unique=1 AND i.type=2 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=1 AND LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(i.filter_definition,' ',''),'[',''),']',''),'(',''),')','')) IN (N'statusin0,1,2',N'status=0orstatus=1orstatus=2',N'status=2orstatus=1orstatus=0')
 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(c.object_id,c.column_id)),N',') WITHIN GROUP(ORDER BY c.key_ordinal)
 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=N'NoteId'
 AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND (c.is_included_column=1 OR c.is_descending_key=1)))
 THROW 53603, 'Incompatible index IX_NoteAmendments_NoteId.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions') AND name=N'IX_NoteAmendmentVersions_AmendmentId_Number')
EXEC(N'CREATE UNIQUE INDEX [IX_NoteAmendmentVersions_AmendmentId_Number] ON [NoteAmendmentVersions] ([AmendmentId], [Number]);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions') AND i.name=N'IX_NoteAmendmentVersions_AmendmentId_Number'
 AND i.is_unique=1 AND i.type=2 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(c.object_id,c.column_id)),N',') WITHIN GROUP(ORDER BY c.key_ordinal)
 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=N'AmendmentId,Number'
 AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND (c.is_included_column=1 OR c.is_descending_key=1)))
 THROW 53603, 'Incompatible index IX_NoteAmendmentVersions_AmendmentId_Number.', 1;

IF OBJECT_ID(N'dbo.FK_ClaimCorrections_NoteAmendmentVersions_AmendedNoteVersionId',N'F') IS NULL
EXEC(N'ALTER TABLE [ClaimCorrections] ADD CONSTRAINT [FK_ClaimCorrections_NoteAmendmentVersions_AmendedNoteVersionId] FOREIGN KEY ([AmendedNoteVersionId]) REFERENCES [NoteAmendmentVersions] ([Id]) ON DELETE NO ACTION;');

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c
 ON c.constraint_object_id=f.object_id WHERE f.parent_object_id=OBJECT_ID(N'dbo.ClaimCorrections')
 AND f.name=N'FK_ClaimCorrections_NoteAmendmentVersions_AmendedNoteVersionId' AND f.referenced_object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions')
 AND f.delete_referential_action=0 AND f.update_referential_action=0
 AND f.is_disabled=0 AND f.is_not_trusted=0
 AND COL_NAME(f.parent_object_id,c.parent_column_id)=N'AmendedNoteVersionId'
 AND COL_NAME(f.referenced_object_id,c.referenced_column_id)=N'Id'
 AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1)
 THROW 53603, 'Incompatible foreign key FK_ClaimCorrections_NoteAmendmentVersions_AmendedNoteVersionId.', 1;

IF OBJECT_ID(N'dbo.FK_ClaimLines_NoteAmendmentVersions_AmendedNoteVersionId',N'F') IS NULL
EXEC(N'ALTER TABLE [ClaimLines] ADD CONSTRAINT [FK_ClaimLines_NoteAmendmentVersions_AmendedNoteVersionId] FOREIGN KEY ([AmendedNoteVersionId]) REFERENCES [NoteAmendmentVersions] ([Id]) ON DELETE NO ACTION;');

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c
 ON c.constraint_object_id=f.object_id WHERE f.parent_object_id=OBJECT_ID(N'dbo.ClaimLines')
 AND f.name=N'FK_ClaimLines_NoteAmendmentVersions_AmendedNoteVersionId' AND f.referenced_object_id=OBJECT_ID(N'dbo.NoteAmendmentVersions')
 AND f.delete_referential_action=0 AND f.update_referential_action=0
 AND f.is_disabled=0 AND f.is_not_trusted=0
 AND COL_NAME(f.parent_object_id,c.parent_column_id)=N'AmendedNoteVersionId'
 AND COL_NAME(f.referenced_object_id,c.referenced_column_id)=N'Id'
 AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1)
 THROW 53603, 'Incompatible foreign key FK_ClaimLines_NoteAmendmentVersions_AmendedNoteVersionId.', 1;

IF @applied=0
INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004120026_AddNoteAmendments', N'10.0.5');

IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>125
 THROW 53603, 'Migration count verification failed.', 1;
