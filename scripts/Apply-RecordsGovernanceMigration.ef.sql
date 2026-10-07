BEGIN TRANSACTION;
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

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261007004626_AddRecordsGovernance', N'10.0.5');

COMMIT;
GO
