-- Source-only EF idempotent script: AddAssessmentReviewCycles -> AddClearinghousePreflightReadiness.
-- Generated with --synthetic-design; no database was connected or migrated.
-- Execute only after separate target authorization and the DATABASE_ENVIRONMENTS.md controlled
-- identity/schema/baseline preflight. Pause dispatch on every host; active old workers ignore
-- readiness. Retain storage/audits on operational rollback and reconcile unresolved work.
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009183720_AddClearinghousePreflightReadiness'
)
BEGIN
    CREATE TABLE [ClearinghouseDispatchReadiness] (
        [AgencyId] int NOT NULL,
        [AccountId] uniqueidentifier NOT NULL,
        [Disposition] int NOT NULL,
        [FailureCount] int NOT NULL,
        [RecoveryCycleId] uniqueidentifier NOT NULL,
        [NextEligibleAtUtc] datetime2 NULL,
        [LastFailureAtUtc] datetime2 NULL,
        [SafeFailureCode] nvarchar(40) NULL,
        [ValidatedAccountRevision] bigint NOT NULL,
        [Revision] bigint NOT NULL,
        CONSTRAINT [PK_ClearinghouseDispatchReadiness] PRIMARY KEY ([AgencyId], [AccountId]),
        CONSTRAINT [CK_ClearinghouseDispatchReadiness_State] CHECK ([AgencyId] > 0 AND [Revision] > 0 AND [ValidatedAccountRevision] >= 0 AND (([Disposition] = 1 AND [FailureCount] = 0 AND [RecoveryCycleId] = '00000000-0000-0000-0000-000000000000' AND [NextEligibleAtUtc] IS NULL AND [LastFailureAtUtc] IS NULL AND [SafeFailureCode] IS NULL) OR ([Disposition] = 2 AND [FailureCount] BETWEEN 1 AND 4 AND [RecoveryCycleId] <> '00000000-0000-0000-0000-000000000000' AND [NextEligibleAtUtc] IS NOT NULL AND [LastFailureAtUtc] IS NOT NULL AND [NextEligibleAtUtc] > [LastFailureAtUtc] AND [SafeFailureCode] IS NOT NULL AND [SafeFailureCode] = 'account_key_unavailable') OR ([Disposition] = 3 AND [FailureCount] = 5 AND [RecoveryCycleId] <> '00000000-0000-0000-0000-000000000000' AND [NextEligibleAtUtc] IS NULL AND [LastFailureAtUtc] IS NOT NULL AND [SafeFailureCode] IS NOT NULL AND [SafeFailureCode] = 'account_key_unavailable'))),
        CONSTRAINT [FK_ClearinghouseDispatchReadiness_ClearinghouseAccounts_AgencyId_AccountId] FOREIGN KEY ([AgencyId], [AccountId]) REFERENCES [ClearinghouseAccounts] ([AgencyId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009183720_AddClearinghousePreflightReadiness'
)
BEGIN
    CREATE INDEX [IX_ClearinghouseDispatches_State_RequestedAtUtc_Id] ON [ClearinghouseDispatches] ([State], [RequestedAtUtc], [Id]) INCLUDE ([AgencyId], [AccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009183720_AddClearinghousePreflightReadiness'
)
BEGIN
    CREATE INDEX [IX_ClearinghouseDispatchReadiness_Disposition_NextEligibleAtUtc] ON [ClearinghouseDispatchReadiness] ([Disposition], [NextEligibleAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009183720_AddClearinghousePreflightReadiness'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009183720_AddClearinghousePreflightReadiness', N'10.0.5');
END;

COMMIT;
GO
