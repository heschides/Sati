BEGIN TRANSACTION;
ALTER TABLE [Settings] ADD [IsInternalElectronicSignatureEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);

CREATE TABLE [ExternalSignatureEvidence] (
    [Id] int NOT NULL IDENTITY,
    [ClientRequestId] uniqueidentifier NOT NULL,
    [AgencyId] int NOT NULL,
    [PersonId] int NOT NULL,
    [DocumentArtifactId] int NOT NULL,
    [ReleaseObligationId] bigint NOT NULL,
    [Method] nvarchar(40) NOT NULL,
    [SignedOn] date NOT NULL,
    [SignerName] nvarchar(120) NOT NULL,
    [SignerCapacity] nvarchar(40) NOT NULL,
    [AttestedByUserId] int NOT NULL,
    [AttestedAtUtc] datetime2 NOT NULL,
    [AttestationText] nvarchar(1000) NOT NULL,
    [BlobPath] nvarchar(400) NOT NULL,
    [ContentSha256] char(64) NOT NULL,
    [ByteCount] bigint NOT NULL,
    [VerificationNote] nvarchar(1000) NULL,
    CONSTRAINT [PK_ExternalSignatureEvidence] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ExternalSignatureEvidence_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ExternalSignatureEvidence_DocumentArtifacts_DocumentArtifactId] FOREIGN KEY ([DocumentArtifactId]) REFERENCES [DocumentArtifacts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ExternalSignatureEvidence_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ExternalSignatureEvidence_ReleaseObligations_ReleaseObligationId] FOREIGN KEY ([ReleaseObligationId]) REFERENCES [ReleaseObligations] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ExternalSignatureEvidence_Users_AttestedByUserId] FOREIGN KEY ([AttestedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);

CREATE UNIQUE INDEX [IX_ExternalSignatureEvidence_AgencyId_ClientRequestId] ON [ExternalSignatureEvidence] ([AgencyId], [ClientRequestId]);

CREATE INDEX [IX_ExternalSignatureEvidence_AttestedByUserId] ON [ExternalSignatureEvidence] ([AttestedByUserId]);

CREATE UNIQUE INDEX [IX_ExternalSignatureEvidence_DocumentArtifactId] ON [ExternalSignatureEvidence] ([DocumentArtifactId]);

CREATE INDEX [IX_ExternalSignatureEvidence_PersonId] ON [ExternalSignatureEvidence] ([PersonId]);

CREATE INDEX [IX_ExternalSignatureEvidence_ReleaseObligationId] ON [ExternalSignatureEvidence] ([ReleaseObligationId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260927152031_AddExternalSignatureEvidence', N'10.0.5');

COMMIT;
GO
