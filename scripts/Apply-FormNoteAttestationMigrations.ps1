<#
.SYNOPSIS
    Applies the two exact-form-note migrations to identity-marked SatiDemo.

.DESCRIPTION
    Operator-controlled, transactional, and rerunnable. This script never changes a
    firewall rule. Run -WhatIfOnly first, then run normally, then rerun to verify
    idempotency. Existing objects are checked for their expected shape; a mismatch
    stops the migration instead of being treated as success. No client data is read
    or backfilled. Legacy notes deliberately keep a null FormId.

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    ./scripts/Apply-FormNoteAttestationMigrations.ps1 -AccessToken $token -WhatIfOnly
    ./scripts/Apply-FormNoteAttestationMigrations.ps1 -AccessToken $token
    ./scripts/Apply-FormNoteAttestationMigrations.ps1 -AccessToken $token
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AccessToken,

    [string]$SqlServer = 'sati-demo-satilogica-central.database.windows.net',

    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
$connectionString = "Server=$SqlServer;Database=SatiDemo;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;"
$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$connection.AccessToken = $AccessToken
$connection.Open()

try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 180
    $command.Parameters.AddWithValue('@rollBackOnly', [bool]$WhatIfOnly) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

IF DB_NAME() <> N'SatiDemo'
    THROW 52400, 'The connected database is not SatiDemo.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL OR
   NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 52401, 'The database is not identity-marked Demo.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 52402, 'EF migration history is missing.', 1;
IF OBJECT_ID(N'dbo.Notes', N'U') IS NULL OR OBJECT_ID(N'dbo.Forms', N'U') IS NULL OR
   OBJECT_ID(N'dbo.Agencies', N'U') IS NULL OR OBJECT_ID(N'dbo.People', N'U') IS NULL OR
   OBJECT_ID(N'dbo.ClaimLines', N'U') IS NULL
    THROW 52403, 'A prerequisite table is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
               WHERE MigrationId = N'20260919212417_AddEftDepositsAndClaimCorrections')
    THROW 52415, 'The preceding release migration is not recorded.', 1;

DECLARE @firstMigration nvarchar(150) = N'20260921235404_LinkNotesToExactFormObligations';
DECLARE @secondMigration nvarchar(150) = N'20260921235644_AddFormAttestationChangeReviewFlags';
DECLARE @firstHistory bit = CASE WHEN EXISTS
    (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @firstMigration) THEN 1 ELSE 0 END;
DECLARE @secondHistory bit = CASE WHEN EXISTS
    (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @secondMigration) THEN 1 ELSE 0 END;

-- History ahead of the schema is corruption, not a cue to repair silently.
IF @firstHistory = 1 AND
   (COL_LENGTH(N'dbo.Notes', N'FormId') IS NULL OR
    COL_LENGTH(N'dbo.Notes', N'FormDateCorrectionReason') IS NULL OR
    NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Notes')
                AND name = N'IX_Notes_FormId') OR
    NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.Notes')
                AND name = N'FK_Notes_Forms_FormId'))
    THROW 52404, 'First migration is recorded but its schema is incomplete.', 1;
IF @secondHistory = 1 AND OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags', N'U') IS NULL
    THROW 52405, 'Second migration is recorded but its table is missing.', 1;
IF @secondHistory = 1 AND @firstHistory = 0
    THROW 52406, 'Migration history has the second migration without the first.', 1;

DECLARE @formIdAdded bit = 0;
DECLARE @reasonAdded bit = 0;
DECLARE @noteIndexAdded bit = 0;
DECLARE @noteFkAdded bit = 0;
DECLARE @flagsTableAdded bit = 0;
DECLARE @firstHistoryAdded bit = 0;
DECLARE @secondHistoryAdded bit = 0;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.Notes', N'FormDateCorrectionReason') IS NULL
BEGIN
    ALTER TABLE dbo.Notes ADD FormDateCorrectionReason nvarchar(1000) NULL;
    SET @reasonAdded = 1;
END;
IF COL_LENGTH(N'dbo.Notes', N'FormId') IS NULL
BEGIN
    ALTER TABLE dbo.Notes ADD FormId int NULL;
    SET @formIdAdded = 1;
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Notes')
               AND name = N'IX_Notes_FormId')
BEGIN
    CREATE INDEX IX_Notes_FormId ON dbo.Notes(FormId);
    SET @noteIndexAdded = 1;
END;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.Notes')
               AND name = N'FK_Notes_Forms_FormId')
BEGIN
    ALTER TABLE dbo.Notes WITH CHECK ADD CONSTRAINT FK_Notes_Forms_FormId
        FOREIGN KEY (FormId) REFERENCES dbo.Forms(Id) ON DELETE NO ACTION;
    SET @noteFkAdded = 1;
END;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
               WHERE c.object_id = OBJECT_ID(N'dbo.Notes') AND c.name = N'FormId'
                 AND t.name = N'int' AND c.is_nullable = 1)
    THROW 52407, 'Notes.FormId has the wrong definition.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
               WHERE c.object_id = OBJECT_ID(N'dbo.Notes') AND c.name = N'FormDateCorrectionReason'
                 AND t.name = N'nvarchar' AND c.max_length = 2000 AND c.is_nullable = 1)
    THROW 52408, 'Notes.FormDateCorrectionReason has the wrong definition.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic
        ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE i.object_id = OBJECT_ID(N'dbo.Notes') AND i.name = N'IX_Notes_FormId'
      AND i.is_unique = 0 AND i.has_filter = 0 AND i.is_disabled = 0
      AND ic.key_ordinal = 1 AND c.name = N'FormId'
      AND (SELECT COUNT(*) FROM sys.index_columns allKeys
           WHERE allKeys.object_id = i.object_id AND allKeys.index_id = i.index_id
             AND allKeys.key_ordinal > 0) = 1)
    THROW 52409, 'IX_Notes_FormId has the wrong definition.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
    JOIN sys.columns parentColumn ON parentColumn.object_id = fkc.parent_object_id
        AND parentColumn.column_id = fkc.parent_column_id
    JOIN sys.columns targetColumn ON targetColumn.object_id = fkc.referenced_object_id
        AND targetColumn.column_id = fkc.referenced_column_id
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Notes')
      AND fk.referenced_object_id = OBJECT_ID(N'dbo.Forms')
      AND fk.name = N'FK_Notes_Forms_FormId' AND fk.delete_referential_action = 0
      AND fk.is_disabled = 0 AND fk.is_not_trusted = 0
      AND parentColumn.name = N'FormId' AND targetColumn.name = N'Id'
      AND (SELECT COUNT(*) FROM sys.foreign_key_columns columnsInFk
           WHERE columnsInFk.constraint_object_id = fk.object_id) = 1)
    THROW 52410, 'FK_Notes_Forms_FormId has the wrong definition.', 1;

IF @firstHistory = 0
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion) VALUES (@firstMigration, N'10.0.5');
    SET @firstHistoryAdded = 1;
END;

IF OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FormAttestationChangeReviewFlags
    (
        Id bigint IDENTITY(1,1) NOT NULL,
        FlagId uniqueidentifier NOT NULL,
        AgencyId int NOT NULL,
        PersonId int NOT NULL,
        NoteId int NOT NULL,
        FormId int NOT NULL,
        ClaimLineId int NULL,
        NoteActivityDate date NULL,
        DueDate date NOT NULL,
        PreviousCompletedOn date NULL,
        RevisedCompletedOn date NULL,
        Reason nvarchar(max) NOT NULL,
        RequiresSupervisorAttention bit NOT NULL,
        RequiresBillingAttention bit NOT NULL,
        BillingHoldReasons int NOT NULL,
        CreatedAtUtc datetime2 NOT NULL,
        CONSTRAINT PK_FormAttestationChangeReviewFlags PRIMARY KEY (Id),
        CONSTRAINT FK_FormAttestationChangeReviewFlags_Agencies_AgencyId FOREIGN KEY (AgencyId)
            REFERENCES dbo.Agencies(Id),
        CONSTRAINT FK_FormAttestationChangeReviewFlags_ClaimLines_ClaimLineId FOREIGN KEY (ClaimLineId)
            REFERENCES dbo.ClaimLines(Id),
        CONSTRAINT FK_FormAttestationChangeReviewFlags_Forms_FormId FOREIGN KEY (FormId)
            REFERENCES dbo.Forms(Id),
        CONSTRAINT FK_FormAttestationChangeReviewFlags_Notes_NoteId FOREIGN KEY (NoteId)
            REFERENCES dbo.Notes(Id),
        CONSTRAINT FK_FormAttestationChangeReviewFlags_People_PersonId FOREIGN KEY (PersonId)
            REFERENCES dbo.People(Id)
    );
    CREATE INDEX IX_FormAttestationChangeReviewFlags_AgencyId_CreatedAtUtc
        ON dbo.FormAttestationChangeReviewFlags(AgencyId, CreatedAtUtc);
    CREATE INDEX IX_FormAttestationChangeReviewFlags_AgencyId_RequiresBillingAttention_CreatedAtUtc
        ON dbo.FormAttestationChangeReviewFlags(AgencyId, RequiresBillingAttention, CreatedAtUtc);
    CREATE INDEX IX_FormAttestationChangeReviewFlags_AgencyId_RequiresSupervisorAttention_CreatedAtUtc
        ON dbo.FormAttestationChangeReviewFlags(AgencyId, RequiresSupervisorAttention, CreatedAtUtc);
    CREATE INDEX IX_FormAttestationChangeReviewFlags_ClaimLineId
        ON dbo.FormAttestationChangeReviewFlags(ClaimLineId);
    CREATE UNIQUE INDEX IX_FormAttestationChangeReviewFlags_FlagId
        ON dbo.FormAttestationChangeReviewFlags(FlagId);
    CREATE INDEX IX_FormAttestationChangeReviewFlags_FormId
        ON dbo.FormAttestationChangeReviewFlags(FormId);
    CREATE INDEX IX_FormAttestationChangeReviewFlags_NoteId
        ON dbo.FormAttestationChangeReviewFlags(NoteId);
    CREATE INDEX IX_FormAttestationChangeReviewFlags_PersonId
        ON dbo.FormAttestationChangeReviewFlags(PersonId);
    SET @flagsTableAdded = 1;
END;

-- Verify every table column, primary/foreign key, and index. Existing tables
-- with a matching name but a different shape must never acquire a history row.
DECLARE @expectedColumns table (Name sysname, TypeName sysname, MaxLength smallint,
                                 IsNullable bit, IsIdentity bit);
INSERT @expectedColumns VALUES
    (N'Id', N'bigint', 8, 0, 1),
    (N'FlagId', N'uniqueidentifier', 16, 0, 0),
    (N'AgencyId', N'int', 4, 0, 0),
    (N'PersonId', N'int', 4, 0, 0),
    (N'NoteId', N'int', 4, 0, 0),
    (N'FormId', N'int', 4, 0, 0),
    (N'ClaimLineId', N'int', 4, 1, 0),
    (N'NoteActivityDate', N'date', 3, 1, 0),
    (N'DueDate', N'date', 3, 0, 0),
    (N'PreviousCompletedOn', N'date', 3, 1, 0),
    (N'RevisedCompletedOn', N'date', 3, 1, 0),
    (N'Reason', N'nvarchar', -1, 0, 0),
    (N'RequiresSupervisorAttention', N'bit', 1, 0, 0),
    (N'RequiresBillingAttention', N'bit', 1, 0, 0),
    (N'BillingHoldReasons', N'int', 4, 0, 0),
    (N'CreatedAtUtc', N'datetime2', 8, 0, 0);
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags'))
   <> (SELECT COUNT(*) FROM @expectedColumns) OR
   EXISTS (
       SELECT 1 FROM @expectedColumns expected
       LEFT JOIN sys.columns actual ON actual.object_id = OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags')
           AND actual.name = expected.Name
       LEFT JOIN sys.types actualType ON actualType.user_type_id = actual.user_type_id
       WHERE actual.column_id IS NULL OR actualType.name <> expected.TypeName OR
             actual.max_length <> expected.MaxLength OR actual.is_nullable <> expected.IsNullable OR
             actual.is_identity <> expected.IsIdentity)
    THROW 52411, 'FormAttestationChangeReviewFlags columns differ from the EF migration.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags')
               AND name = N'PK_FormAttestationChangeReviewFlags' AND type = N'PK')
    THROW 52412, 'Review flags primary key is missing.', 1;

DECLARE @expectedFks table (Name sysname, ColumnName sysname, TargetTable sysname);
INSERT @expectedFks VALUES
    (N'FK_FormAttestationChangeReviewFlags_Agencies_AgencyId', N'AgencyId', N'Agencies'),
    (N'FK_FormAttestationChangeReviewFlags_ClaimLines_ClaimLineId', N'ClaimLineId', N'ClaimLines'),
    (N'FK_FormAttestationChangeReviewFlags_Forms_FormId', N'FormId', N'Forms'),
    (N'FK_FormAttestationChangeReviewFlags_Notes_NoteId', N'NoteId', N'Notes'),
    (N'FK_FormAttestationChangeReviewFlags_People_PersonId', N'PersonId', N'People');
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags'))
   <> (SELECT COUNT(*) FROM @expectedFks) OR
   EXISTS (
       SELECT 1 FROM @expectedFks expected
       LEFT JOIN sys.foreign_keys fk ON fk.parent_object_id = OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags')
           AND fk.name = expected.Name
       LEFT JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
       LEFT JOIN sys.columns parentColumn ON parentColumn.object_id = fkc.parent_object_id
           AND parentColumn.column_id = fkc.parent_column_id
       LEFT JOIN sys.columns targetColumn ON targetColumn.object_id = fkc.referenced_object_id
           AND targetColumn.column_id = fkc.referenced_column_id
       WHERE fk.object_id IS NULL OR fk.referenced_object_id <> OBJECT_ID(N'dbo.' + expected.TargetTable)
          OR fk.delete_referential_action <> 0 OR fk.is_disabled <> 0 OR fk.is_not_trusted <> 0
          OR parentColumn.name <> expected.ColumnName OR targetColumn.name <> N'Id'
          OR (SELECT COUNT(*) FROM sys.foreign_key_columns columnsInFk
              WHERE columnsInFk.constraint_object_id = fk.object_id) <> 1)
    THROW 52413, 'Review flags foreign keys differ from the EF migration.', 1;

DECLARE @expectedIndexes table (Name sysname, Keys nvarchar(200), IsUnique bit);
INSERT @expectedIndexes VALUES
    (N'IX_FormAttestationChangeReviewFlags_AgencyId_CreatedAtUtc', N'AgencyId,CreatedAtUtc', 0),
    (N'IX_FormAttestationChangeReviewFlags_AgencyId_RequiresBillingAttention_CreatedAtUtc', N'AgencyId,RequiresBillingAttention,CreatedAtUtc', 0),
    (N'IX_FormAttestationChangeReviewFlags_AgencyId_RequiresSupervisorAttention_CreatedAtUtc', N'AgencyId,RequiresSupervisorAttention,CreatedAtUtc', 0),
    (N'IX_FormAttestationChangeReviewFlags_ClaimLineId', N'ClaimLineId', 0),
    (N'IX_FormAttestationChangeReviewFlags_FlagId', N'FlagId', 1),
    (N'IX_FormAttestationChangeReviewFlags_FormId', N'FormId', 0),
    (N'IX_FormAttestationChangeReviewFlags_NoteId', N'NoteId', 0),
    (N'IX_FormAttestationChangeReviewFlags_PersonId', N'PersonId', 0);
IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags')
      AND is_primary_key = 0 AND is_hypothetical = 0) <> (SELECT COUNT(*) FROM @expectedIndexes) OR
   EXISTS (
       SELECT 1 FROM @expectedIndexes expected
       LEFT JOIN sys.indexes i ON i.object_id = OBJECT_ID(N'dbo.FormAttestationChangeReviewFlags')
           AND i.name = expected.Name
       WHERE i.index_id IS NULL OR i.is_unique <> expected.IsUnique OR i.is_disabled <> 0 OR i.has_filter <> 0 OR
         (SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
          FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id
              AND c.column_id = ic.column_id
          WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) <> expected.Keys)
    THROW 52414, 'Review flags indexes differ from the EF migration.', 1;

IF @secondHistory = 0
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion) VALUES (@secondMigration, N'10.0.5');
    SET @secondHistoryAdded = 1;
END;

DECLARE @migrationCount bigint = (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory);
DECLARE @flagRowCount bigint = (SELECT COUNT_BIG(*) FROM dbo.FormAttestationChangeReviewFlags);
IF @rollBackOnly = 1 ROLLBACK TRANSACTION ELSE COMMIT TRANSACTION;

SELECT DB_NAME() AS DatabaseName,
       (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
       @formIdAdded AS FormIdAdded, @reasonAdded AS ReasonAdded,
       @noteIndexAdded AS NoteIndexAdded, @noteFkAdded AS NoteForeignKeyAdded,
       @flagsTableAdded AS ReviewFlagsTableAdded,
       @firstHistoryAdded AS FirstHistoryAdded, @secondHistoryAdded AS SecondHistoryAdded,
       @migrationCount AS MigrationCount, @flagRowCount AS ReviewFlagRowCount,
       @rollBackOnly AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The form-note migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName          = $reader.GetString(0)
        EnvironmentName       = $reader.GetString(1)
        FormIdAdded           = $reader.GetBoolean(2)
        ReasonAdded           = $reader.GetBoolean(3)
        NoteIndexAdded        = $reader.GetBoolean(4)
        NoteForeignKeyAdded   = $reader.GetBoolean(5)
        ReviewFlagsTableAdded = $reader.GetBoolean(6)
        FirstHistoryAdded     = $reader.GetBoolean(7)
        SecondHistoryAdded    = $reader.GetBoolean(8)
        MigrationCount        = $reader.GetInt64(9)
        ReviewFlagRowCount    = $reader.GetInt64(10)
        RolledBack            = $reader.GetBoolean(11)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
