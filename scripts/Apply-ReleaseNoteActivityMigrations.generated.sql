BEGIN TRANSACTION;
ALTER TABLE [Notes] ADD [Activities] int NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260922154932_AddMultiActivityNotes', N'10.0.5');

COMMIT;
GO
BEGIN TRANSACTION;
ALTER TABLE [ReleaseObligationAttestations] ADD [EvidenceNoteId] int NULL;

ALTER TABLE [Notes] ADD [ReleaseObligationId] bigint NULL;

CREATE INDEX [IX_Notes_ReleaseObligationId] ON [Notes] ([ReleaseObligationId]);

ALTER TABLE [Notes] ADD CONSTRAINT [FK_Notes_ReleaseObligations_ReleaseObligationId] FOREIGN KEY ([ReleaseObligationId]) REFERENCES [ReleaseObligations] ([Id]) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260922161704_LinkReleaseNotesToExactObligations', N'10.0.5');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [ReleaseObligationAttestations] ADD [RevocationReason] nvarchar(max) NULL;

ALTER TABLE [ReleaseObligationAttestations] ADD [RevokedAtUtc] datetime2 NULL;

ALTER TABLE [ReleaseObligationAttestations] ADD [RevokedByUserId] int NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260922162222_TrackReleaseAttestationRevocation', N'10.0.5');

COMMIT;
GO

BEGIN TRANSACTION;
DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[FormAttestationChangeReviewFlags]') AND [c].[name] = N'FormId');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [FormAttestationChangeReviewFlags] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [FormAttestationChangeReviewFlags] ALTER COLUMN [FormId] int NULL;

ALTER TABLE [FormAttestationChangeReviewFlags] ADD [ReleaseObligationId] bigint NULL;

CREATE INDEX [IX_FormAttestationChangeReviewFlags_ReleaseObligationId] ON [FormAttestationChangeReviewFlags] ([ReleaseObligationId]);

ALTER TABLE [FormAttestationChangeReviewFlags] ADD CONSTRAINT [CK_FormAttestationChangeReviewFlags_OneSource] CHECK (([FormId] IS NOT NULL AND [ReleaseObligationId] IS NULL) OR ([FormId] IS NULL AND [ReleaseObligationId] IS NOT NULL));

ALTER TABLE [FormAttestationChangeReviewFlags] ADD CONSTRAINT [FK_FormAttestationChangeReviewFlags_ReleaseObligations_ReleaseObligationId] FOREIGN KEY ([ReleaseObligationId]) REFERENCES [ReleaseObligations] ([Id]) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260922191918_SupportReleaseAttestationReviewFlags', N'10.0.5');

COMMIT;
GO
