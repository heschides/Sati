BEGIN TRANSACTION;
DROP INDEX [IX_DocumentArtifacts_OneLivePerCycle] ON [DocumentArtifacts];
GO

ALTER TABLE [Providers] ADD [Email] nvarchar(254) NULL;
GO

DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ExternalSignatureEvidence]') AND [c].[name] = N'ReleaseObligationId');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [ExternalSignatureEvidence] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [ExternalSignatureEvidence] ALTER COLUMN [ReleaseObligationId] bigint NULL;
GO

ALTER TABLE [DocumentArtifacts] ADD [OneOffRecipientJson] nvarchar(4000) NULL;
GO

ALTER TABLE [DocumentArtifacts] ADD [OneOffReleaseId] uniqueidentifier NULL;
GO

ALTER TABLE [DocumentArtifacts] ADD [PromotedProviderId] int NULL;
GO

CREATE UNIQUE INDEX [IX_DocumentArtifacts_OneLivePerCycle] ON [DocumentArtifacts] ([PersonId], [Kind], [CycleStart]) WHERE [ReleaseObligationId] IS NULL AND [OneOffReleaseId] IS NULL AND [SupersededByArtifactId] IS NULL;
GO

CREATE UNIQUE INDEX [IX_DocumentArtifacts_OneLivePerOneOffRelease] ON [DocumentArtifacts] ([OneOffReleaseId], [Kind]) WHERE [OneOffReleaseId] IS NOT NULL AND [SupersededByArtifactId] IS NULL;
GO

CREATE INDEX [IX_DocumentArtifacts_PromotedProviderId] ON [DocumentArtifacts] ([PromotedProviderId]);
GO

ALTER TABLE [DocumentArtifacts] ADD CONSTRAINT [FK_DocumentArtifacts_Providers_PromotedProviderId] FOREIGN KEY ([PromotedProviderId]) REFERENCES [Providers] ([Id]) ON DELETE SET NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260927232039_SupportDurableOneOffReleases', N'10.0.5');

COMMIT;
GO
