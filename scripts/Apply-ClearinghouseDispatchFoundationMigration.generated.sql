BEGIN TRANSACTION;
DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ClearinghouseResponseReceipts]') AND [c].[name] = N'ActorUserId');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [ClearinghouseResponseReceipts] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [ClearinghouseResponseReceipts] ALTER COLUMN [ActorUserId] int NULL;

ALTER TABLE [ClearinghouseResponseReceipts] ADD [AccountId] uniqueidentifier NULL;

ALTER TABLE [ClearinghouseResponseReceipts] ADD [ConnectorKind] int NULL;

ALTER TABLE [ClearinghouseResponseReceipts] ADD [ConnectorVersion] nvarchar(40) NULL;

ALTER TABLE [ClearinghouseResponseReceipts] ADD [ContentType] nvarchar(80) NULL;

ALTER TABLE [ClearinghouseResponseReceipts] ADD [ExternalArtifactId] nvarchar(128) NULL;

ALTER TABLE [ClearinghouseResponseReceipts] ADD [FeedKind] int NULL;

ALTER TABLE [ClearinghouseResponseReceipts] ADD [Source] int NOT NULL DEFAULT 0;

ALTER TABLE [EdiGenerations] ADD CONSTRAINT [AK_EdiGenerations_AgencyId_Id] UNIQUE ([AgencyId], [Id]);

ALTER TABLE [ClearinghouseResponseReceipts] ADD CONSTRAINT [AK_ClearinghouseResponseReceipts_AgencyId_Id] UNIQUE ([AgencyId], [Id]);

CREATE TABLE [ClearinghouseAccounts] (
    [Id] uniqueidentifier NOT NULL,
    [AgencyId] int NOT NULL,
    [ConnectorKind] int NOT NULL,
    [IsTest] bit NOT NULL,
    [ExternalAccountNumber] nvarchar(80) NOT NULL,
    [ClaimNamespace] nvarchar(8) NULL,
    [SecretReference] nvarchar(500) NULL,
    [TradingPartnerProfileVersion] int NOT NULL,
    [IsEnabled] bit NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NOT NULL,
    [Revision] bigint NOT NULL,
    CONSTRAINT [PK_ClearinghouseAccounts] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_ClearinghouseAccounts_AgencyId_Id] UNIQUE ([AgencyId], [Id]),
    CONSTRAINT [CK_ClearinghouseAccounts_ClaimMdProfile] CHECK ([ConnectorKind] <> 2 OR ([ClaimNamespace] IS NOT NULL AND [TradingPartnerProfileVersion] > 0)),
    CONSTRAINT [FK_ClearinghouseAccounts_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ClearinghouseDispatches] (
    [Id] uniqueidentifier NOT NULL,
    [AgencyId] int NOT NULL,
    [AccountId] uniqueidentifier NOT NULL,
    [EdiGenerationId] bigint NOT NULL,
    [RequestingUserId] int NOT NULL,
    [RequestedAtUtc] datetime2 NOT NULL,
    [State] int NOT NULL,
    [TradingPartnerProfileVersion] int NOT NULL,
    [ExternalFileId] nvarchar(128) NULL,
    [AcceptedClaimCount] int NULL,
    [RejectedClaimCount] int NULL,
    [SafeErrorCode] nvarchar(80) NULL,
    [Revision] bigint NOT NULL,
    CONSTRAINT [PK_ClearinghouseDispatches] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ClearinghouseDispatches_ClearinghouseAccounts_AgencyId_AccountId] FOREIGN KEY ([AgencyId], [AccountId]) REFERENCES [ClearinghouseAccounts] ([AgencyId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ClearinghouseDispatches_EdiGenerations_AgencyId_EdiGenerationId] FOREIGN KEY ([AgencyId], [EdiGenerationId]) REFERENCES [EdiGenerations] ([AgencyId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ClearinghouseDispatches_Users_RequestingUserId] FOREIGN KEY ([RequestingUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ClearinghouseFeedCheckpoints] (
    [Id] uniqueidentifier NOT NULL,
    [AgencyId] int NOT NULL,
    [AccountId] uniqueidentifier NOT NULL,
    [FeedKind] int NOT NULL,
    [Cursor] nvarchar(256) NULL,
    [LastReceiptId] uniqueidentifier NULL,
    [UpdatedAtUtc] datetime2 NOT NULL,
    [Revision] bigint NOT NULL,
    CONSTRAINT [PK_ClearinghouseFeedCheckpoints] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ClearinghouseFeedCheckpoints_ClearinghouseAccounts_AgencyId_AccountId] FOREIGN KEY ([AgencyId], [AccountId]) REFERENCES [ClearinghouseAccounts] ([AgencyId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ClearinghouseFeedCheckpoints_ClearinghouseResponseReceipts_AgencyId_LastReceiptId] FOREIGN KEY ([AgencyId], [LastReceiptId]) REFERENCES [ClearinghouseResponseReceipts] ([AgencyId], [Id]) ON DELETE NO ACTION
);

CREATE TABLE [ClearinghouseDispatchAttempts] (
    [Id] uniqueidentifier NOT NULL,
    [DispatchId] uniqueidentifier NOT NULL,
    [AttemptNumber] int NOT NULL,
    [StartedAtUtc] datetime2 NOT NULL,
    [CompletedAtUtc] datetime2 NOT NULL,
    [ContentSha256] nvarchar(64) NOT NULL,
    [FileName] nvarchar(260) NOT NULL,
    [Outcome] int NOT NULL,
    [VendorCode] nvarchar(40) NULL,
    [CorrelationId] nvarchar(80) NULL,
    [ResponseSha256] nvarchar(64) NULL,
    [ResponseCiphertext] varbinary(max) NULL,
    [ResponseNonce] varbinary(12) NULL,
    [ResponseTag] varbinary(16) NULL,
    [ResponseWrappedDataKey] varbinary(max) NULL,
    [ResponseKeyId] nvarchar(500) NULL,
    CONSTRAINT [PK_ClearinghouseDispatchAttempts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ClearinghouseDispatchAttempts_ClearinghouseDispatches_DispatchId] FOREIGN KEY ([DispatchId]) REFERENCES [ClearinghouseDispatches] ([Id]) ON DELETE NO ACTION
);

CREATE UNIQUE INDEX [IX_ClearinghouseResponseReceipts_AccountId_FeedKind_ExternalArtifactId] ON [ClearinghouseResponseReceipts] ([AccountId], [FeedKind], [ExternalArtifactId]) WHERE [AccountId] IS NOT NULL AND [FeedKind] IS NOT NULL AND [ExternalArtifactId] IS NOT NULL;

CREATE INDEX [IX_ClearinghouseResponseReceipts_AgencyId_AccountId] ON [ClearinghouseResponseReceipts] ([AgencyId], [AccountId]);

CREATE UNIQUE INDEX [IX_ClearinghouseAccounts_AgencyId_ConnectorKind_IsTest] ON [ClearinghouseAccounts] ([AgencyId], [ConnectorKind], [IsTest]) WHERE [IsEnabled] = 1;

CREATE UNIQUE INDEX [IX_ClearinghouseAccounts_ClaimNamespace] ON [ClearinghouseAccounts] ([ClaimNamespace]) WHERE [ClaimNamespace] IS NOT NULL;

CREATE UNIQUE INDEX [IX_ClearinghouseDispatchAttempts_DispatchId_AttemptNumber] ON [ClearinghouseDispatchAttempts] ([DispatchId], [AttemptNumber]);

CREATE INDEX [IX_ClearinghouseDispatches_AgencyId_AccountId] ON [ClearinghouseDispatches] ([AgencyId], [AccountId]);

CREATE INDEX [IX_ClearinghouseDispatches_AgencyId_EdiGenerationId] ON [ClearinghouseDispatches] ([AgencyId], [EdiGenerationId]);

CREATE INDEX [IX_ClearinghouseDispatches_RequestingUserId] ON [ClearinghouseDispatches] ([RequestingUserId]);

CREATE INDEX [IX_ClearinghouseDispatches_AgencyId_State_RequestedAtUtc] ON [ClearinghouseDispatches] ([AgencyId], [State], [RequestedAtUtc]);

CREATE UNIQUE INDEX [IX_ClearinghouseDispatches_EdiGenerationId] ON [ClearinghouseDispatches] ([EdiGenerationId]);

CREATE UNIQUE INDEX [IX_ClearinghouseFeedCheckpoints_AccountId_FeedKind] ON [ClearinghouseFeedCheckpoints] ([AccountId], [FeedKind]);

CREATE INDEX [IX_ClearinghouseFeedCheckpoints_AgencyId_AccountId] ON [ClearinghouseFeedCheckpoints] ([AgencyId], [AccountId]);

CREATE INDEX [IX_ClearinghouseFeedCheckpoints_AgencyId_LastReceiptId] ON [ClearinghouseFeedCheckpoints] ([AgencyId], [LastReceiptId]);

ALTER TABLE [ClearinghouseResponseReceipts] ADD CONSTRAINT [FK_ClearinghouseResponseReceipts_ClearinghouseAccounts_AgencyId_AccountId] FOREIGN KEY ([AgencyId], [AccountId]) REFERENCES [ClearinghouseAccounts] ([AgencyId], [Id]) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260926183942_AddClearinghouseDispatchFoundation', N'10.0.5');

COMMIT;
GO
