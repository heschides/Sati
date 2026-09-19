<#
.SYNOPSIS
    Applies and verifies the bank-deposit and claim-correction migration in hosted Demo.

.DESCRIPTION
    This runner is deliberately narrower than dotnet ef database update. It refuses any
    database except identity-marked SatiDemo, guards every statement on the real schema,
    verifies that anything already present has the expected definition rather than merely
    the expected name, and is transactional and rerunnable. Use -WhatIfOnly first, then run
    normally twice: the second run must report every Created flag false.

    Migration 20260919212417_AddEftDepositsAndClaimCorrections is additive. It creates four
    tables — EftDepositRecords, ClaimAcknowledgementOutcomes, ClaimCorrections and
    ClaimCorrectionSubmissions — and adds two nullable-or-defaulted columns,
    RemittanceClaimOutcomes.PayerClaimControlNumber and EdiGenerations.IsCorrection. It
    alters no existing row and reads no consumer data.

    The filtered unique index on EftDepositRecords.SupersedesRecordId is what stops two
    people correcting the same recorded deposit at once, so it is verified as unique and
    filtered rather than merely present.

    The Demo SQL firewall does not admit workstations. Add the temporary exact-IP rule
    before running this and remove it immediately afterwards.

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    ./scripts/Apply-EftDepositsAndClaimCorrectionsMigration.ps1 -AccessToken $token -WhatIfOnly
    ./scripts/Apply-EftDepositsAndClaimCorrectionsMigration.ps1 -AccessToken $token
    ./scripts/Apply-EftDepositsAndClaimCorrectionsMigration.ps1 -AccessToken $token
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AccessToken,

    [string]$SqlServer = 'sati-demo-satilogica-central.database.windows.net',

    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
$migrationId = '20260919212417_AddEftDepositsAndClaimCorrections'
$connectionString = "Server=$SqlServer;Database=SatiDemo;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;"
$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$connection.AccessToken = $AccessToken
$connection.Open()

try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 180
    $command.Parameters.AddWithValue('@migrationId', $migrationId) | Out-Null
    $command.Parameters.AddWithValue('@rollBackOnly', [bool]$WhatIfOnly) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Stated rather than inherited: the filtered unique index below cannot be created unless
-- both are ON, and a client that connects with either OFF would fail halfway through.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

IF DB_NAME() <> N'SatiDemo'
    THROW 52300, 'The connected database is not SatiDemo.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 52301, 'dbo.SatiDatabaseIdentity is missing.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 52302, 'The database identity marker is not Demo.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 52303, 'dbo.__EFMigrationsHistory is missing.', 1;

-- Every table this migration points a foreign key at must already exist, or the schema
-- is not the one this migration was generated against.
IF OBJECT_ID(N'dbo.Agencies', N'U') IS NULL OR OBJECT_ID(N'dbo.Users', N'U') IS NULL
   OR OBJECT_ID(N'dbo.BillingPeriods', N'U') IS NULL OR OBJECT_ID(N'dbo.ClaimLines', N'U') IS NULL
   OR OBJECT_ID(N'dbo.EdiGenerations', N'U') IS NULL OR OBJECT_ID(N'dbo.RemittanceDeposits', N'U') IS NULL
   OR OBJECT_ID(N'dbo.RemittanceClaimOutcomes', N'U') IS NULL
   OR OBJECT_ID(N'dbo.ClearinghouseResponseReceipts', N'U') IS NULL
    THROW 52304, 'A table this migration depends on is missing; this is not the expected Sati schema.', 1;
IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId)
   AND OBJECT_ID(N'dbo.EftDepositRecords', N'U') IS NULL
    THROW 52305, 'Migration history says this migration is applied, but EftDepositRecords is missing.', 1;

DECLARE @payerColumnAdded bit = 0;
DECLARE @correctionColumnAdded bit = 0;
DECLARE @depositTableCreated bit = 0;
DECLARE @acknowledgementTableCreated bit = 0;
DECLARE @correctionTableCreated bit = 0;
DECLARE @submissionTableCreated bit = 0;
DECLARE @historyWritten bit = 0;
BEGIN TRANSACTION;

-- 1. The two added columns. Both are safe on existing rows: one is nullable, the other
--    defaults to 0, which is what every file generated before corrections existed was.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.RemittanceClaimOutcomes') AND name = N'PayerClaimControlNumber')
BEGIN
    ALTER TABLE dbo.RemittanceClaimOutcomes ADD PayerClaimControlNumber nvarchar(50) NULL;
    SET @payerColumnAdded = 1;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.EdiGenerations') AND name = N'IsCorrection')
BEGIN
    ALTER TABLE dbo.EdiGenerations ADD IsCorrection bit NOT NULL CONSTRAINT DF_EdiGenerations_IsCorrection DEFAULT 0;
    SET @correctionColumnAdded = 1;
END;

-- 2. Recorded bank deposits.
IF OBJECT_ID(N'dbo.EftDepositRecords', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EftDepositRecords
    (
        Id bigint IDENTITY(1,1) NOT NULL,
        AgencyId int NOT NULL,
        RemittanceDepositId bigint NOT NULL,
        Amount decimal(18,2) NOT NULL,
        DepositDate date NOT NULL,
        BankTraceNumber nvarchar(80) NULL,
        Note nvarchar(500) NULL,
        SupersedesRecordId bigint NULL,
        RecordedByUserId int NOT NULL,
        RecordedAtUtc datetime2 NOT NULL,
        IsSynthetic bit NOT NULL,
        CONSTRAINT PK_EftDepositRecords PRIMARY KEY (Id),
        CONSTRAINT FK_EftDepositRecords_Agencies_AgencyId FOREIGN KEY (AgencyId)
            REFERENCES dbo.Agencies (Id),
        CONSTRAINT FK_EftDepositRecords_EftDepositRecords_SupersedesRecordId FOREIGN KEY (SupersedesRecordId)
            REFERENCES dbo.EftDepositRecords (Id),
        CONSTRAINT FK_EftDepositRecords_RemittanceDeposits_RemittanceDepositId FOREIGN KEY (RemittanceDepositId)
            REFERENCES dbo.RemittanceDeposits (Id),
        CONSTRAINT FK_EftDepositRecords_Users_RecordedByUserId FOREIGN KEY (RecordedByUserId)
            REFERENCES dbo.Users (Id)
    );
    CREATE INDEX IX_EftDepositRecords_AgencyId_RemittanceDepositId
        ON dbo.EftDepositRecords (AgencyId, RemittanceDepositId);
    CREATE INDEX IX_EftDepositRecords_RecordedByUserId
        ON dbo.EftDepositRecords (RecordedByUserId);
    CREATE INDEX IX_EftDepositRecords_RemittanceDepositId
        ON dbo.EftDepositRecords (RemittanceDepositId);
    CREATE UNIQUE INDEX IX_EftDepositRecords_SupersedesRecordId
        ON dbo.EftDepositRecords (SupersedesRecordId) WHERE SupersedesRecordId IS NOT NULL;
    SET @depositTableCreated = 1;
END;

-- 3. Per-claim 277CA verdicts.
IF OBJECT_ID(N'dbo.ClaimAcknowledgementOutcomes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClaimAcknowledgementOutcomes
    (
        Id bigint IDENTITY(1,1) NOT NULL,
        AgencyId int NOT NULL,
        BillingPeriodId int NOT NULL,
        EdiGenerationId bigint NOT NULL,
        ResponseId uniqueidentifier NOT NULL,
        ClaimReference nvarchar(80) NOT NULL,
        Disposition int NOT NULL,
        CategoryCode nvarchar(10) NOT NULL,
        StatusCode nvarchar(10) NOT NULL,
        ReceivedAtUtc datetime2 NOT NULL,
        IsSynthetic bit NOT NULL,
        CONSTRAINT PK_ClaimAcknowledgementOutcomes PRIMARY KEY (Id),
        CONSTRAINT FK_ClaimAcknowledgementOutcomes_Agencies_AgencyId FOREIGN KEY (AgencyId)
            REFERENCES dbo.Agencies (Id),
        CONSTRAINT FK_ClaimAcknowledgementOutcomes_BillingPeriods_BillingPeriodId FOREIGN KEY (BillingPeriodId)
            REFERENCES dbo.BillingPeriods (Id),
        CONSTRAINT FK_ClaimAcknowledgementOutcomes_ClearinghouseResponseReceipts_ResponseId FOREIGN KEY (ResponseId)
            REFERENCES dbo.ClearinghouseResponseReceipts (Id),
        CONSTRAINT FK_ClaimAcknowledgementOutcomes_EdiGenerations_EdiGenerationId FOREIGN KEY (EdiGenerationId)
            REFERENCES dbo.EdiGenerations (Id)
    );
    CREATE INDEX IX_ClaimAcknowledgementOutcomes_AgencyId_EdiGenerationId_ClaimReference
        ON dbo.ClaimAcknowledgementOutcomes (AgencyId, EdiGenerationId, ClaimReference);
    CREATE INDEX IX_ClaimAcknowledgementOutcomes_BillingPeriodId
        ON dbo.ClaimAcknowledgementOutcomes (BillingPeriodId);
    CREATE INDEX IX_ClaimAcknowledgementOutcomes_EdiGenerationId
        ON dbo.ClaimAcknowledgementOutcomes (EdiGenerationId);
    CREATE INDEX IX_ClaimAcknowledgementOutcomes_ResponseId
        ON dbo.ClaimAcknowledgementOutcomes (ResponseId);
    SET @acknowledgementTableCreated = 1;
END;

-- 4. Claim corrections and the files that carried them.
IF OBJECT_ID(N'dbo.ClaimCorrections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClaimCorrections
    (
        Id bigint IDENTITY(1,1) NOT NULL,
        AgencyId int NOT NULL,
        BillingPeriodId int NOT NULL,
        ClaimLineId int NOT NULL,
        NoteId int NOT NULL,
        [Action] int NOT NULL,
        PayerClaimControlNumber nvarchar(50) NULL,
        ClaimSnapshotJson nvarchar(max) NOT NULL,
        ClientMaineCareId nvarchar(80) NOT NULL,
        RenderingProviderNpi nvarchar(10) NOT NULL,
        DiagnosisCode nvarchar(20) NOT NULL,
        PlaceOfService int NOT NULL,
        Reason nvarchar(500) NOT NULL,
        CorrectsEdiGenerationId bigint NULL,
        RequestedByUserId int NOT NULL,
        RequestedAtUtc datetime2 NOT NULL,
        CONSTRAINT PK_ClaimCorrections PRIMARY KEY (Id),
        CONSTRAINT FK_ClaimCorrections_Agencies_AgencyId FOREIGN KEY (AgencyId)
            REFERENCES dbo.Agencies (Id),
        CONSTRAINT FK_ClaimCorrections_BillingPeriods_BillingPeriodId FOREIGN KEY (BillingPeriodId)
            REFERENCES dbo.BillingPeriods (Id),
        CONSTRAINT FK_ClaimCorrections_ClaimLines_ClaimLineId FOREIGN KEY (ClaimLineId)
            REFERENCES dbo.ClaimLines (Id),
        CONSTRAINT FK_ClaimCorrections_EdiGenerations_CorrectsEdiGenerationId FOREIGN KEY (CorrectsEdiGenerationId)
            REFERENCES dbo.EdiGenerations (Id),
        CONSTRAINT FK_ClaimCorrections_Users_RequestedByUserId FOREIGN KEY (RequestedByUserId)
            REFERENCES dbo.Users (Id)
    );
    CREATE INDEX IX_ClaimCorrections_AgencyId_BillingPeriodId
        ON dbo.ClaimCorrections (AgencyId, BillingPeriodId);
    CREATE INDEX IX_ClaimCorrections_BillingPeriodId ON dbo.ClaimCorrections (BillingPeriodId);
    CREATE INDEX IX_ClaimCorrections_ClaimLineId ON dbo.ClaimCorrections (ClaimLineId);
    CREATE INDEX IX_ClaimCorrections_CorrectsEdiGenerationId ON dbo.ClaimCorrections (CorrectsEdiGenerationId);
    CREATE INDEX IX_ClaimCorrections_RequestedByUserId ON dbo.ClaimCorrections (RequestedByUserId);
    SET @correctionTableCreated = 1;
END;

IF OBJECT_ID(N'dbo.ClaimCorrectionSubmissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClaimCorrectionSubmissions
    (
        ClaimCorrectionId bigint NOT NULL,
        EdiGenerationId bigint NOT NULL,
        CONSTRAINT PK_ClaimCorrectionSubmissions PRIMARY KEY (ClaimCorrectionId, EdiGenerationId),
        CONSTRAINT FK_ClaimCorrectionSubmissions_ClaimCorrections_ClaimCorrectionId FOREIGN KEY (ClaimCorrectionId)
            REFERENCES dbo.ClaimCorrections (Id),
        CONSTRAINT FK_ClaimCorrectionSubmissions_EdiGenerations_EdiGenerationId FOREIGN KEY (EdiGenerationId)
            REFERENCES dbo.EdiGenerations (Id)
    );
    CREATE INDEX IX_ClaimCorrectionSubmissions_EdiGenerationId
        ON dbo.ClaimCorrectionSubmissions (EdiGenerationId);
    SET @submissionTableCreated = 1;
END;

-- Present is not enough. A table or column of the right name and the wrong shape would let
-- the API write rows the desktop cannot read, or lose a guard the workflow depends on.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.RemittanceClaimOutcomes')
      AND c.name = N'PayerClaimControlNumber' AND t.name = N'nvarchar'
      AND c.max_length = 100 AND c.is_nullable = 1)
    THROW 52306, 'RemittanceClaimOutcomes.PayerClaimControlNumber has an unexpected definition.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.EdiGenerations')
      AND c.name = N'IsCorrection' AND t.name = N'bit' AND c.is_nullable = 0)
    THROW 52307, 'EdiGenerations.IsCorrection has an unexpected definition.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.EftDepositRecords')
      AND c.name = N'Amount' AND t.name = N'decimal' AND c.precision = 18 AND c.scale = 2
      AND c.is_nullable = 0)
    THROW 52308, 'EftDepositRecords.Amount has an unexpected definition.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.EftDepositRecords')
      AND c.name = N'DepositDate' AND t.name = N'date' AND c.is_nullable = 0)
    THROW 52309, 'EftDepositRecords.DepositDate has an unexpected definition.', 1;

-- The one entry-per-correction guard: without uniqueness, two people correcting the same
-- recorded deposit at once would both succeed and the later figure would win silently.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.EftDepositRecords')
      AND name = N'IX_EftDepositRecords_SupersedesRecordId'
      AND is_unique = 1 AND has_filter = 1)
    THROW 52310, 'IX_EftDepositRecords_SupersedesRecordId is missing, not unique, or not filtered.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.ClaimCorrectionSubmissions')
      AND type = 'PK' AND name = N'PK_ClaimCorrectionSubmissions')
    THROW 52311, 'ClaimCorrectionSubmissions is missing its composite primary key.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.ClaimCorrections') AND name = N'ClaimSnapshotJson')
    THROW 52312, 'ClaimCorrections.ClaimSnapshotJson is missing.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.ClaimAcknowledgementOutcomes') AND name = N'Disposition')
    THROW 52313, 'ClaimAcknowledgementOutcomes.Disposition is missing.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId)
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (@migrationId, N'10.0.5');
    SET @historyWritten = 1;
END;

DECLARE @migrationCount bigint = (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory);
DECLARE @depositRowCount bigint = (SELECT COUNT_BIG(*) FROM dbo.EftDepositRecords);
DECLARE @correctionRowCount bigint = (SELECT COUNT_BIG(*) FROM dbo.ClaimCorrections);
IF @rollBackOnly = 1
    ROLLBACK TRANSACTION;
ELSE
    COMMIT TRANSACTION;

SELECT DB_NAME() AS DatabaseName,
       (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
       @payerColumnAdded AS PayerClaimColumnAdded,
       @correctionColumnAdded AS IsCorrectionColumnAdded,
       @depositTableCreated AS EftDepositRecordsCreated,
       @acknowledgementTableCreated AS ClaimAcknowledgementOutcomesCreated,
       @correctionTableCreated AS ClaimCorrectionsCreated,
       @submissionTableCreated AS ClaimCorrectionSubmissionsCreated,
       @historyWritten AS HistoryWritten,
       @migrationCount AS MigrationCount,
       @depositRowCount AS DepositEntryRowCount,
       @correctionRowCount AS CorrectionRowCount,
       @rollBackOnly AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The deposit and claim-correction migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName                       = $reader.GetString(0)
        EnvironmentName                    = $reader.GetString(1)
        PayerClaimColumnAdded              = $reader.GetBoolean(2)
        IsCorrectionColumnAdded            = $reader.GetBoolean(3)
        EftDepositRecordsCreated           = $reader.GetBoolean(4)
        ClaimAcknowledgementOutcomesCreated = $reader.GetBoolean(5)
        ClaimCorrectionsCreated            = $reader.GetBoolean(6)
        ClaimCorrectionSubmissionsCreated  = $reader.GetBoolean(7)
        HistoryWritten                     = $reader.GetBoolean(8)
        MigrationCount                     = $reader.GetInt64(9)
        DepositEntryRowCount               = $reader.GetInt64(10)
        CorrectionRowCount                 = $reader.GetInt64(11)
        RolledBack                         = $reader.GetBoolean(12)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
