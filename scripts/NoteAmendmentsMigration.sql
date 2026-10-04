BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    ALTER TABLE [ClaimLines] ADD [AmendedNoteVersionId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    ALTER TABLE [ClaimCorrections] ADD [AmendedNoteVersionId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    ALTER TABLE [ClaimCorrections] ADD [CorrectedChargeAmount] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    ALTER TABLE [ClaimCorrections] ADD [CorrectedDateOfService] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    ALTER TABLE [ClaimCorrections] ADD [CorrectedUnits] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE TABLE [NoteAmendments] (
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
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE TABLE [NoteAmendmentVersions] (
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
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE TABLE [NoteAmendmentEvents] (
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
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE TABLE [NoteAmendmentFinancialReviews] (
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
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE INDEX [IX_ClaimLines_AmendedNoteVersionId] ON [ClaimLines] ([AmendedNoteVersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE INDEX [IX_ClaimCorrections_AmendedNoteVersionId] ON [ClaimCorrections] ([AmendedNoteVersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NoteAmendmentEvents_AgencyId_ActorId_OperationId] ON [NoteAmendmentEvents] ([AgencyId], [ActorId], [OperationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE INDEX [IX_NoteAmendmentEvents_AmendmentId] ON [NoteAmendmentEvents] ([AmendmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE INDEX [IX_NoteAmendmentEvents_VersionId] ON [NoteAmendmentEvents] ([VersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NoteAmendmentFinancialReviews_AgencyId_ReviewedById_OperationId] ON [NoteAmendmentFinancialReviews] ([AgencyId], [ReviewedById], [OperationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NoteAmendmentFinancialReviews_ApprovedVersionId] ON [NoteAmendmentFinancialReviews] ([ApprovedVersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE INDEX [IX_NoteAmendmentFinancialReviews_NoteId] ON [NoteAmendmentFinancialReviews] ([NoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE INDEX [IX_NoteAmendments_AgencyId_NoteId] ON [NoteAmendments] ([AgencyId], [NoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_NoteAmendments_NoteId] ON [NoteAmendments] ([NoteId]) WHERE [Status] IN (0, 1, 2)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NoteAmendmentVersions_AmendmentId_Number] ON [NoteAmendmentVersions] ([AmendmentId], [Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    ALTER TABLE [ClaimCorrections] ADD CONSTRAINT [FK_ClaimCorrections_NoteAmendmentVersions_AmendedNoteVersionId] FOREIGN KEY ([AmendedNoteVersionId]) REFERENCES [NoteAmendmentVersions] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    ALTER TABLE [ClaimLines] ADD CONSTRAINT [FK_ClaimLines_NoteAmendmentVersions_AmendedNoteVersionId] FOREIGN KEY ([AmendedNoteVersionId]) REFERENCES [NoteAmendmentVersions] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004120026_AddNoteAmendments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004120026_AddNoteAmendments', N'10.0.5');
END;

COMMIT;
GO

