-- Reviewed additive migration 128; caller owns the transaction and reset exclusion lock.
SET XACT_ABORT ON;
IF @@TRANCOUNT=0 THROW 53810, 'Caller must own the migration transaction.', 1;
IF DB_NAME()<>@expectedDatabase OR
 (@expectedDatabase<>N'SatiDemo' AND @expectedDatabase NOT LIKE N'SatiSyntheticPipeline[_]%') OR
 NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id=1 AND EnvironmentName=N'Demo')
 THROW 53810, 'Refusing an environment outside marked Demo or private synthetic rehearsal.', 1;
DECLARE @migration nvarchar(150)=N'20261007111016_AddAssessmentReviewCycles';
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 53811, 'Migration history missing.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@migration)
BEGIN
 IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>127 OR
    (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory)<>N'20261007004626_AddRecordsGovernance'
  THROW 53811, 'Expected exactly the reviewed 127-migration predecessor.', 1;
 IF OBJECT_ID(N'dbo.AssessmentSubmissions',N'U') IS NOT NULL OR OBJECT_ID(N'dbo.AssessmentReviewEvents',N'U') IS NOT NULL
  THROW 53812, 'Partial or untracked assessment schema requires investigation.', 1;
CREATE TABLE [AssessmentSubmissions] (
    [Id] int NOT NULL IDENTITY,
    [AgencyId] int NOT NULL,
    [AssessmentId] int NOT NULL,
    [PersonId] int NOT NULL,
    [AuthorUserId] int NOT NULL,
    [AssessmentVersion] int NOT NULL,
    [CycleNumber] int NOT NULL,
    [DocumentRevision] int NOT NULL,
    [FormId] int NOT NULL,
    [TargetEffectiveDate] date NOT NULL,
    [DueDate] date NOT NULL,
    [RulesVersion] int NOT NULL,
    [ContentSha256] nvarchar(64) NOT NULL,
    [DocumentJson] nvarchar(max) NOT NULL,
    [ConsumerName] nvarchar(300) NOT NULL,
    [SubmittedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_AssessmentSubmissions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AssessmentSubmissions_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AssessmentSubmissions_ComprehensiveAssessments_AssessmentId] FOREIGN KEY ([AssessmentId]) REFERENCES [ComprehensiveAssessments] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AssessmentSubmissions_Forms_FormId] FOREIGN KEY ([FormId]) REFERENCES [Forms] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AssessmentSubmissions_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AssessmentSubmissions_Users_AuthorUserId] FOREIGN KEY ([AuthorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [AssessmentReviewEvents] (
    [Id] bigint NOT NULL IDENTITY,
    [AgencyId] int NOT NULL,
    [AssessmentId] int NOT NULL,
    [SubmissionId] int NOT NULL,
    [Action] nvarchar(20) NOT NULL,
    [Location] nvarchar(100) NOT NULL,
    [Text] nvarchar(4000) NOT NULL,
    [Blocking] bit NOT NULL,
    [FlagId] bigint NULL,
    [ActorUserId] int NOT NULL,
    [RecordedAtUtc] datetime2 NOT NULL,
    [AssessmentRevision] int NOT NULL,
    [ArtifactId] int NULL,
    [CompletedOn] date NULL,
    CONSTRAINT [PK_AssessmentReviewEvents] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AssessmentReviewEvents_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AssessmentReviewEvents_AssessmentReviewEvents_FlagId] FOREIGN KEY ([FlagId]) REFERENCES [AssessmentReviewEvents] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AssessmentReviewEvents_AssessmentSubmissions_SubmissionId] FOREIGN KEY ([SubmissionId]) REFERENCES [AssessmentSubmissions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AssessmentReviewEvents_ComprehensiveAssessments_AssessmentId] FOREIGN KEY ([AssessmentId]) REFERENCES [ComprehensiveAssessments] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AssessmentReviewEvents_DocumentArtifacts_ArtifactId] FOREIGN KEY ([ArtifactId]) REFERENCES [DocumentArtifacts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AssessmentReviewEvents_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_AssessmentReviewEvents_ActorUserId] ON [AssessmentReviewEvents] ([ActorUserId]);

CREATE INDEX [IX_AssessmentReviewEvents_AgencyId] ON [AssessmentReviewEvents] ([AgencyId]);

CREATE INDEX [IX_AssessmentReviewEvents_ArtifactId] ON [AssessmentReviewEvents] ([ArtifactId]);

CREATE UNIQUE INDEX [IX_AssessmentReviewEvents_AssessmentId_AssessmentRevision] ON [AssessmentReviewEvents] ([AssessmentId], [AssessmentRevision]);

CREATE INDEX [IX_AssessmentReviewEvents_FlagId] ON [AssessmentReviewEvents] ([FlagId]);

CREATE INDEX [IX_AssessmentReviewEvents_SubmissionId] ON [AssessmentReviewEvents] ([SubmissionId]);

CREATE INDEX [IX_AssessmentSubmissions_AgencyId_SubmittedAtUtc] ON [AssessmentSubmissions] ([AgencyId], [SubmittedAtUtc]);

CREATE UNIQUE INDEX [IX_AssessmentSubmissions_AssessmentId_CycleNumber] ON [AssessmentSubmissions] ([AssessmentId], [CycleNumber]);

CREATE INDEX [IX_AssessmentSubmissions_AuthorUserId] ON [AssessmentSubmissions] ([AuthorUserId]);

CREATE INDEX [IX_AssessmentSubmissions_FormId] ON [AssessmentSubmissions] ([FormId]);

CREATE INDEX [IX_AssessmentSubmissions_PersonId] ON [AssessmentSubmissions] ([PersonId]);
END;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AssessmentSubmissions'))<>16 OR EXISTS (SELECT 1 FROM (VALUES
(N'Id',N'int',4,0,1),
(N'AgencyId',N'int',4,0,0),
(N'AssessmentId',N'int',4,0,0),
(N'PersonId',N'int',4,0,0),
(N'AuthorUserId',N'int',4,0,0),
(N'AssessmentVersion',N'int',4,0,0),
(N'CycleNumber',N'int',4,0,0),
(N'DocumentRevision',N'int',4,0,0),
(N'FormId',N'int',4,0,0),
(N'TargetEffectiveDate',N'date',3,0,0),
(N'DueDate',N'date',3,0,0),
(N'RulesVersion',N'int',4,0,0),
(N'ContentSha256',N'nvarchar',128,0,0),
(N'DocumentJson',N'nvarchar',-1,0,0),
(N'ConsumerName',N'nvarchar',600,0,0),
(N'SubmittedAtUtc',N'datetime2',8,0,0)
) expected(columnName,typeName,length,nullableColumn,identityColumn) WHERE NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.AssessmentSubmissions') AND c.name=expected.columnName AND t.name=expected.typeName AND c.max_length=expected.length AND c.is_nullable=expected.nullableColumn AND c.is_identity=expected.identityColumn AND c.is_computed=0 AND (expected.typeName<>N'datetime2' OR c.scale=7))) THROW 53812, 'Assessment columns differ from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_AssessmentSubmissions_Agencies_AgencyId' AND fk.parent_object_id=OBJECT_ID(N'dbo.AssessmentSubmissions') AND c.name=N'AgencyId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.Agencies') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53812, 'Assessment ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_AssessmentSubmissions_ComprehensiveAssessments_AssessmentId' AND fk.parent_object_id=OBJECT_ID(N'dbo.AssessmentSubmissions') AND c.name=N'AssessmentId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.ComprehensiveAssessments') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53812, 'Assessment ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_AssessmentSubmissions_Forms_FormId' AND fk.parent_object_id=OBJECT_ID(N'dbo.AssessmentSubmissions') AND c.name=N'FormId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.Forms') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53812, 'Assessment ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_AssessmentSubmissions_People_PersonId' AND fk.parent_object_id=OBJECT_ID(N'dbo.AssessmentSubmissions') AND c.name=N'PersonId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.People') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53812, 'Assessment ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_AssessmentSubmissions_Users_AuthorUserId' AND fk.parent_object_id=OBJECT_ID(N'dbo.AssessmentSubmissions') AND c.name=N'AuthorUserId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.Users') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53812, 'Assessment ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'dbo.AssessmentSubmissions') AND i.is_primary_key=1 AND i.is_unique=1 AND i.is_disabled=0 AND c.name=N'Id' AND ic.key_ordinal=1) THROW 53812, 'Assessment primary key differs from reviewed model.', 1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents'))<>14 OR EXISTS (SELECT 1 FROM (VALUES
(N'Id',N'bigint',8,0,1),
(N'AgencyId',N'int',4,0,0),
(N'AssessmentId',N'int',4,0,0),
(N'SubmissionId',N'int',4,0,0),
(N'Action',N'nvarchar',40,0,0),
(N'Location',N'nvarchar',200,0,0),
(N'Text',N'nvarchar',8000,0,0),
(N'Blocking',N'bit',1,0,0),
(N'FlagId',N'bigint',8,1,0),
(N'ActorUserId',N'int',4,0,0),
(N'RecordedAtUtc',N'datetime2',8,0,0),
(N'AssessmentRevision',N'int',4,0,0),
(N'ArtifactId',N'int',4,1,0),
(N'CompletedOn',N'date',3,1,0)
) expected(columnName,typeName,length,nullableColumn,identityColumn) WHERE NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND c.name=expected.columnName AND t.name=expected.typeName AND c.max_length=expected.length AND c.is_nullable=expected.nullableColumn AND c.is_identity=expected.identityColumn AND c.is_computed=0 AND (expected.typeName<>N'datetime2' OR c.scale=7))) THROW 53812, 'Assessment columns differ from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_AssessmentReviewEvents_Agencies_AgencyId' AND fk.parent_object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND c.name=N'AgencyId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.Agencies') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53812, 'Assessment ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_AssessmentReviewEvents_AssessmentReviewEvents_FlagId' AND fk.parent_object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND c.name=N'FlagId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53812, 'Assessment ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_AssessmentReviewEvents_AssessmentSubmissions_SubmissionId' AND fk.parent_object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND c.name=N'SubmissionId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.AssessmentSubmissions') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53812, 'Assessment ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_AssessmentReviewEvents_ComprehensiveAssessments_AssessmentId' AND fk.parent_object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND c.name=N'AssessmentId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.ComprehensiveAssessments') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53812, 'Assessment ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_AssessmentReviewEvents_DocumentArtifacts_ArtifactId' AND fk.parent_object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND c.name=N'ArtifactId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.DocumentArtifacts') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53812, 'Assessment ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE fk.name=N'FK_AssessmentReviewEvents_Users_ActorUserId' AND fk.parent_object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND c.name=N'ActorUserId' AND fc.referenced_object_id=OBJECT_ID(N'dbo.Users') AND rc.name=N'Id' AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0) THROW 53812, 'Assessment ownership constraint differs from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND i.is_primary_key=1 AND i.is_unique=1 AND i.is_disabled=0 AND c.name=N'Id' AND ic.key_ordinal=1) THROW 53812, 'Assessment primary key differs from reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND i.name=N'IX_AssessmentReviewEvents_ActorUserId' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'ActorUserId') THROW 53812, 'Assessment index differs from reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND i.name=N'IX_AssessmentReviewEvents_AgencyId' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'AgencyId') THROW 53812, 'Assessment index differs from reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND i.name=N'IX_AssessmentReviewEvents_ArtifactId' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'ArtifactId') THROW 53812, 'Assessment index differs from reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND i.name=N'IX_AssessmentReviewEvents_AssessmentId_AssessmentRevision' AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'AssessmentId,AssessmentRevision') THROW 53812, 'Assessment index differs from reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND i.name=N'IX_AssessmentReviewEvents_FlagId' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'FlagId') THROW 53812, 'Assessment index differs from reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.AssessmentReviewEvents') AND i.name=N'IX_AssessmentReviewEvents_SubmissionId' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'SubmissionId') THROW 53812, 'Assessment index differs from reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.AssessmentSubmissions') AND i.name=N'IX_AssessmentSubmissions_AgencyId_SubmittedAtUtc' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'AgencyId,SubmittedAtUtc') THROW 53812, 'Assessment index differs from reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.AssessmentSubmissions') AND i.name=N'IX_AssessmentSubmissions_AssessmentId_CycleNumber' AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'AssessmentId,CycleNumber') THROW 53812, 'Assessment index differs from reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.AssessmentSubmissions') AND i.name=N'IX_AssessmentSubmissions_AuthorUserId' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'AuthorUserId') THROW 53812, 'Assessment index differs from reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.AssessmentSubmissions') AND i.name=N'IX_AssessmentSubmissions_FormId' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'FormId') THROW 53812, 'Assessment index differs from reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.AssessmentSubmissions') AND i.name=N'IX_AssessmentSubmissions_PersonId' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'PersonId') THROW 53812, 'Assessment index differs from reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@migration) INSERT dbo.__EFMigrationsHistory(MigrationId,ProductVersion) VALUES(@migration,N'10.0.5');
