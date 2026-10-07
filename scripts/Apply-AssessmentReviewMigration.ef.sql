BEGIN TRANSACTION;
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

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261007111016_AddAssessmentReviewCycles', N'10.0.5');

COMMIT;
GO
