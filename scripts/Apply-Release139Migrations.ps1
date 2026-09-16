<#
.SYNOPSIS
    Applies and verifies the Representative Payee workflow, weekly check-request automation,
    and case-note goal-progress migrations.

.DESCRIPTION
    This runner is deliberately narrower than dotnet ef database update. It refuses a database
    or environment identity mismatch, verifies the actual schema instead of trusting migration
    history alone, runs all changes in one transaction, and is rerunnable. Use -InspectOnly,
    then -WhatIfOnly, then run normally twice to prove application and idempotency.

    Local SatiProduction receives a full backup before the real schema change when it contains
    records. Azure SatiDemo relies on configured point-in-time recovery and requires an Entra
    access token plus the separately operator-managed temporary firewall rule.

.EXAMPLE
    ./scripts/Apply-Release139Migrations.ps1 -DatabaseName SatiProduction -InspectOnly
    ./scripts/Apply-Release139Migrations.ps1 -DatabaseName SatiProduction -WhatIfOnly
    ./scripts/Apply-Release139Migrations.ps1 -DatabaseName SatiProduction

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    ./scripts/Apply-Release139Migrations.ps1 -DatabaseName SatiDemo `
        -SqlServer sati-demo-satilogica-central.database.windows.net -AccessToken $token -WhatIfOnly
#>
[CmdletBinding()]
param(
    [ValidateSet('SatiDemo', 'SatiProduction')]
    [Parameter(Mandatory)]
    [string]$DatabaseName,

    [string]$SqlServer = '(localdb)\MSSQLLocalDB',

    [string]$AccessToken,

    [switch]$InspectOnly,

    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
if ($InspectOnly -and $WhatIfOnly) {
    throw 'Choose either -InspectOnly or -WhatIfOnly, not both.'
}

$expectedEnvironment = if ($DatabaseName -ceq 'SatiDemo') { 'Demo' } else { 'Production' }
$payeeMigration = '20260914015314_AddRepresentativePayeeWorkflow'
$automationMigration = '20260914023645_AddWeeklyCheckRequestAutomation'
$goalMigration = '20260914030703_AddGoalProgressToCaseNotes'
$usesAccessToken = -not [string]::IsNullOrWhiteSpace($AccessToken)

if ($DatabaseName -ceq 'SatiDemo' -and -not $usesAccessToken) {
    throw 'SatiDemo requires an Entra access token; integrated workstation credentials are not permitted.'
}
if ($DatabaseName -ceq 'SatiProduction' -and $usesAccessToken) {
    throw 'The local SatiProduction runner does not accept an Azure access token.'
}

$connectionString = if ($usesAccessToken) {
    "Server=$SqlServer;Database=$DatabaseName;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;"
}
else {
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=30;"
}

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
if ($usesAccessToken) {
    $connection.AccessToken = $AccessToken
}
$connection.Open()

$backupPath = $null
try {
    $preflight = $connection.CreateCommand()
    $preflight.CommandTimeout = 180
    $preflight.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
    $preflight.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
    $preflight.Parameters.AddWithValue('@payeeMigration', $payeeMigration) | Out-Null
    $preflight.Parameters.AddWithValue('@automationMigration', $automationMigration) | Out-Null
    $preflight.Parameters.AddWithValue('@goalMigration', $goalMigration) | Out-Null
    $preflight.CommandText = @'
SET NOCOUNT ON;

IF DB_NAME() COLLATE Latin1_General_100_BIN2 <> @expectedDatabase COLLATE Latin1_General_100_BIN2
    THROW 52300, 'The connected database is not the exact database requested.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 52301, 'dbo.SatiDatabaseIdentity is missing.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1
      AND EnvironmentName COLLATE Latin1_General_100_BIN2 = @expectedEnvironment COLLATE Latin1_General_100_BIN2)
    THROW 52302, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 52303, 'dbo.__EFMigrationsHistory is missing.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL OR OBJECT_ID(N'dbo.People', N'U') IS NULL
   OR OBJECT_ID(N'dbo.CheckRequests', N'U') IS NULL OR OBJECT_ID(N'dbo.Notes', N'U') IS NULL
    THROW 52304, 'A required existing Sati table is missing.', 1;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    (SELECT COUNT_BIG(*) FROM dbo.Users) AS UserCount,
    (SELECT COUNT_BIG(*) FROM dbo.People) AS PersonCount,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@payeeMigration) THEN 1 ELSE 0 END AS bit) AS PayeeHistory,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@automationMigration) THEN 1 ELSE 0 END AS bit) AS AutomationHistory,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@goalMigration) THEN 1 ELSE 0 END AS bit) AS GoalHistory,
    CAST(CASE WHEN OBJECT_ID(N'dbo.CheckRequestWorkflowEvents', N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS WorkflowTable,
    CAST(CASE WHEN OBJECT_ID(N'dbo.RepresentativePayeeLedgerEntries', N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS LedgerTable,
    CAST(CASE WHEN OBJECT_ID(N'dbo.CheckRequestTemplates', N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS TemplateTable,
    CAST(CASE WHEN COL_LENGTH(N'dbo.CheckRequests', N'TemplateId') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS TemplateColumn,
    CAST(CASE WHEN COL_LENGTH(N'dbo.CheckRequests', N'ScheduledForDate') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS ScheduledColumn,
    CAST(CASE WHEN COL_LENGTH(N'dbo.Notes', N'GoalProgress') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS GoalColumn;
'@

    $preflightReader = $preflight.ExecuteReader()
    if (-not $preflightReader.Read()) {
        throw 'The migration preflight row was not returned.'
    }
    $databaseIdentity = $preflightReader.GetString(0)
    $environmentIdentity = $preflightReader.GetString(1)
    $userCount = $preflightReader.GetInt64(2)
    $personCount = $preflightReader.GetInt64(3)
    $migrationCountBefore = $preflightReader.GetInt64(4)
    $payeeHistory = $preflightReader.GetBoolean(5)
    $automationHistory = $preflightReader.GetBoolean(6)
    $goalHistory = $preflightReader.GetBoolean(7)
    $workflowTable = $preflightReader.GetBoolean(8)
    $ledgerTable = $preflightReader.GetBoolean(9)
    $templateTable = $preflightReader.GetBoolean(10)
    $templateColumn = $preflightReader.GetBoolean(11)
    $scheduledColumn = $preflightReader.GetBoolean(12)
    $goalColumn = $preflightReader.GetBoolean(13)
    $preflightReader.Close()

    if ($InspectOnly) {
        [pscustomobject][ordered]@{
            DatabaseName = $databaseIdentity
            EnvironmentName = $environmentIdentity
            UserCount = $userCount
            PersonCount = $personCount
            MigrationCount = $migrationCountBefore
            PayeeHistory = $payeeHistory
            AutomationHistory = $automationHistory
            GoalHistory = $goalHistory
            WorkflowTable = $workflowTable
            LedgerTable = $ledgerTable
            TemplateTable = $templateTable
            TemplateColumn = $templateColumn
            ScheduledColumn = $scheduledColumn
            GoalColumn = $goalColumn
        }
        return
    }

    $hasPendingHistory = -not ($payeeHistory -and $automationHistory -and $goalHistory)
    if (-not $WhatIfOnly -and -not $usesAccessToken -and $hasPendingHistory -and
        ($userCount -gt 0 -or $personCount -gt 0)) {
        $backupDirectory = Join-Path `
            ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) `
            'Sati\schema-backups'
        [System.IO.Directory]::CreateDirectory($backupDirectory) | Out-Null
        $stamp = Get-Date -Format 'yyyy-MM-dd-HHmmss'
        $backupPath = Join-Path $backupDirectory "$DatabaseName-$stamp.bak"
        $escapedBackupPath = $backupPath.Replace("'", "''", [StringComparison]::Ordinal)

        $backup = $connection.CreateCommand()
        $backup.CommandTimeout = 600
        $backup.CommandText = "BACKUP DATABASE [$DatabaseName] TO DISK = '$escapedBackupPath' WITH INIT, SKIP, NOFORMAT, CHECKSUM;"
        $backup.ExecuteNonQuery() | Out-Null
    }

    $command = $connection.CreateCommand()
    $command.CommandTimeout = 300
    $command.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
    $command.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
    $command.Parameters.AddWithValue('@payeeMigration', $payeeMigration) | Out-Null
    $command.Parameters.AddWithValue('@automationMigration', $automationMigration) | Out-Null
    $command.Parameters.AddWithValue('@goalMigration', $goalMigration) | Out-Null
    $command.Parameters.AddWithValue('@rollBackOnly', [bool]$WhatIfOnly) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() COLLATE Latin1_General_100_BIN2 <> @expectedDatabase COLLATE Latin1_General_100_BIN2
    THROW 52300, 'The connected database is not the exact database requested.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 52301, 'dbo.SatiDatabaseIdentity is missing.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id=1
      AND EnvironmentName COLLATE Latin1_General_100_BIN2 = @expectedEnvironment COLLATE Latin1_General_100_BIN2)
    THROW 52302, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 52303, 'dbo.__EFMigrationsHistory is missing.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL OR OBJECT_ID(N'dbo.People', N'U') IS NULL
   OR OBJECT_ID(N'dbo.CheckRequests', N'U') IS NULL OR OBJECT_ID(N'dbo.Notes', N'U') IS NULL
    THROW 52304, 'A required existing Sati table is missing.', 1;

DECLARE @payeeHistoryBefore bit = CASE WHEN EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@payeeMigration) THEN 1 ELSE 0 END;
DECLARE @automationHistoryBefore bit = CASE WHEN EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@automationMigration) THEN 1 ELSE 0 END;
DECLARE @goalHistoryBefore bit = CASE WHEN EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@goalMigration) THEN 1 ELSE 0 END;

IF @payeeHistoryBefore=1 AND
   (OBJECT_ID(N'dbo.CheckRequestWorkflowEvents', N'U') IS NULL
    OR OBJECT_ID(N'dbo.RepresentativePayeeLedgerEntries', N'U') IS NULL)
    THROW 52305, 'Representative Payee migration history exists but its tables are missing.', 1;
IF @automationHistoryBefore=1 AND
   (OBJECT_ID(N'dbo.CheckRequestTemplates', N'U') IS NULL
    OR COL_LENGTH(N'dbo.CheckRequests', N'TemplateId') IS NULL
    OR COL_LENGTH(N'dbo.CheckRequests', N'ScheduledForDate') IS NULL)
    THROW 52306, 'Check-request automation migration history exists but its schema is missing.', 1;
IF @goalHistoryBefore=1 AND COL_LENGTH(N'dbo.Notes', N'GoalProgress') IS NULL
    THROW 52307, 'Goal-progress migration history exists but Notes.GoalProgress is missing.', 1;

DECLARE @tablesCreated int = 0;
DECLARE @columnsAdded int = 0;
DECLARE @indexesCreated int = 0;
DECLARE @foreignKeysCreated int = 0;
DECLARE @administratorsUpgraded int = 0;
DECLARE @historyRowsWritten int = 0;

BEGIN TRANSACTION;

-- 20260914015314_AddRepresentativePayeeWorkflow
IF @payeeHistoryBefore=0 AND OBJECT_ID(N'dbo.CheckRequestWorkflowEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CheckRequestWorkflowEvents
    (
        Id bigint IDENTITY(1,1) NOT NULL,
        CheckRequestId int NOT NULL,
        [Checkpoint] nvarchar(20) NOT NULL,
        Action nvarchar(30) NOT NULL,
        OccurredAtUtc datetime2 NOT NULL,
        ActorUserId int NOT NULL,
        ActorName nvarchar(200) NOT NULL,
        Note nvarchar(1000) NULL,
        CONSTRAINT PK_CheckRequestWorkflowEvents PRIMARY KEY (Id),
        CONSTRAINT FK_CheckRequestWorkflowEvents_CheckRequests_CheckRequestId
            FOREIGN KEY (CheckRequestId) REFERENCES dbo.CheckRequests(Id) ON DELETE CASCADE
    );
    SET @tablesCreated += 1;
END;

IF @payeeHistoryBefore=0 AND OBJECT_ID(N'dbo.RepresentativePayeeLedgerEntries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepresentativePayeeLedgerEntries
    (
        Id bigint IDENTITY(1,1) NOT NULL,
        PersonId int NOT NULL,
        CheckRequestId int NULL,
        EntryDate date NOT NULL,
        Kind nvarchar(20) NOT NULL,
        Amount decimal(18,2) NOT NULL,
        Description nvarchar(500) NOT NULL,
        RecordedAtUtc datetime2 NOT NULL,
        RecordedByUserId int NOT NULL,
        RecordedByName nvarchar(200) NOT NULL,
        CONSTRAINT PK_RepresentativePayeeLedgerEntries PRIMARY KEY (Id),
        CONSTRAINT FK_RepresentativePayeeLedgerEntries_CheckRequests_CheckRequestId
            FOREIGN KEY (CheckRequestId) REFERENCES dbo.CheckRequests(Id) ON DELETE NO ACTION,
        CONSTRAINT FK_RepresentativePayeeLedgerEntries_People_PersonId
            FOREIGN KEY (PersonId) REFERENCES dbo.People(Id) ON DELETE NO ACTION
    );
    SET @tablesCreated += 1;
END;

IF OBJECT_ID(N'dbo.CheckRequestWorkflowEvents', N'U') IS NULL
    THROW 52310, 'CheckRequestWorkflowEvents was not created.', 1;
IF EXISTS (
    SELECT expected.Name
    FROM (VALUES
        (N'Id', N'bigint', CONVERT(smallint,8), CONVERT(bit,0), CONVERT(bit,1), CONVERT(tinyint,19), CONVERT(tinyint,0)),
        (N'CheckRequestId', N'int', CONVERT(smallint,4), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,10), CONVERT(tinyint,0)),
        (N'Checkpoint', N'nvarchar', CONVERT(smallint,40), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'Action', N'nvarchar', CONVERT(smallint,60), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'OccurredAtUtc', N'datetime2', CONVERT(smallint,8), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'ActorUserId', N'int', CONVERT(smallint,4), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,10), CONVERT(tinyint,0)),
        (N'ActorName', N'nvarchar', CONVERT(smallint,400), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'Note', N'nvarchar', CONVERT(smallint,2000), CONVERT(bit,1), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0))
    ) expected(Name, TypeName, MaxLength, IsNullable, IsIdentity, PrecisionValue, ScaleValue)
    WHERE NOT EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
        WHERE c.object_id=OBJECT_ID(N'dbo.CheckRequestWorkflowEvents') AND c.name=expected.Name
          AND t.name=expected.TypeName AND c.max_length=expected.MaxLength
          AND c.is_nullable=expected.IsNullable AND c.is_identity=expected.IsIdentity
          AND (expected.PrecisionValue=0 OR c.precision=expected.PrecisionValue)
          AND (expected.ScaleValue=0 OR c.scale=expected.ScaleValue)))
    THROW 52311, 'CheckRequestWorkflowEvents has an unexpected column definition.', 1;

IF OBJECT_ID(N'dbo.RepresentativePayeeLedgerEntries', N'U') IS NULL
    THROW 52312, 'RepresentativePayeeLedgerEntries was not created.', 1;
IF EXISTS (
    SELECT expected.Name
    FROM (VALUES
        (N'Id', N'bigint', CONVERT(smallint,8), CONVERT(bit,0), CONVERT(bit,1), CONVERT(tinyint,19), CONVERT(tinyint,0)),
        (N'PersonId', N'int', CONVERT(smallint,4), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,10), CONVERT(tinyint,0)),
        (N'CheckRequestId', N'int', CONVERT(smallint,4), CONVERT(bit,1), CONVERT(bit,0), CONVERT(tinyint,10), CONVERT(tinyint,0)),
        (N'EntryDate', N'date', CONVERT(smallint,3), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'Kind', N'nvarchar', CONVERT(smallint,40), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'Amount', N'decimal', CONVERT(smallint,9), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,18), CONVERT(tinyint,2)),
        (N'Description', N'nvarchar', CONVERT(smallint,1000), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'RecordedAtUtc', N'datetime2', CONVERT(smallint,8), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'RecordedByUserId', N'int', CONVERT(smallint,4), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,10), CONVERT(tinyint,0)),
        (N'RecordedByName', N'nvarchar', CONVERT(smallint,400), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0))
    ) expected(Name, TypeName, MaxLength, IsNullable, IsIdentity, PrecisionValue, ScaleValue)
    WHERE NOT EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
        WHERE c.object_id=OBJECT_ID(N'dbo.RepresentativePayeeLedgerEntries') AND c.name=expected.Name
          AND t.name=expected.TypeName AND c.max_length=expected.MaxLength
          AND c.is_nullable=expected.IsNullable AND c.is_identity=expected.IsIdentity
          AND (expected.PrecisionValue=0 OR c.precision=expected.PrecisionValue)
          AND (expected.ScaleValue=0 OR c.scale=expected.ScaleValue)))
    THROW 52313, 'RepresentativePayeeLedgerEntries has an unexpected column definition.', 1;

IF @payeeHistoryBefore=0 AND NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CheckRequestWorkflowEvents')
      AND name=N'IX_CheckRequestWorkflowEvents_CheckRequestId_Checkpoint')
BEGIN
    CREATE UNIQUE INDEX IX_CheckRequestWorkflowEvents_CheckRequestId_Checkpoint
        ON dbo.CheckRequestWorkflowEvents(CheckRequestId, [Checkpoint]);
    SET @indexesCreated += 1;
END;
IF @payeeHistoryBefore=0 AND NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.RepresentativePayeeLedgerEntries')
      AND name=N'IX_RepresentativePayeeLedgerEntries_CheckRequestId')
BEGIN
    CREATE UNIQUE INDEX IX_RepresentativePayeeLedgerEntries_CheckRequestId
        ON dbo.RepresentativePayeeLedgerEntries(CheckRequestId) WHERE CheckRequestId IS NOT NULL;
    SET @indexesCreated += 1;
END;
IF @payeeHistoryBefore=0 AND NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.RepresentativePayeeLedgerEntries')
      AND name=N'IX_RepresentativePayeeLedgerEntries_PersonId_EntryDate_Id')
BEGIN
    CREATE INDEX IX_RepresentativePayeeLedgerEntries_PersonId_EntryDate_Id
        ON dbo.RepresentativePayeeLedgerEntries(PersonId, EntryDate, Id);
    SET @indexesCreated += 1;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CheckRequestWorkflowEvents')
    AND name=N'IX_CheckRequestWorkflowEvents_CheckRequestId_Checkpoint' AND is_unique=1)
    THROW 52314, 'The workflow checkpoint unique index is missing or not unique.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.RepresentativePayeeLedgerEntries')
    AND name=N'IX_RepresentativePayeeLedgerEntries_CheckRequestId' AND is_unique=1 AND has_filter=1)
    THROW 52315, 'The released-check ledger unique filtered index is missing or invalid.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.RepresentativePayeeLedgerEntries')
    AND name=N'IX_RepresentativePayeeLedgerEntries_PersonId_EntryDate_Id' AND is_unique=0)
    THROW 52316, 'The consumer ledger ordering index is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.CheckRequestWorkflowEvents')
    AND name=N'FK_CheckRequestWorkflowEvents_CheckRequests_CheckRequestId' AND delete_referential_action=1)
    THROW 52317, 'The workflow-event CheckRequest cascade foreign key is missing or invalid.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.RepresentativePayeeLedgerEntries')
    AND name=N'FK_RepresentativePayeeLedgerEntries_CheckRequests_CheckRequestId' AND delete_referential_action=0)
    THROW 52318, 'The ledger CheckRequest restrictive foreign key is missing or invalid.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.RepresentativePayeeLedgerEntries')
    AND name=N'FK_RepresentativePayeeLedgerEntries_People_PersonId' AND delete_referential_action=0)
    THROW 52319, 'The ledger Person restrictive foreign key is missing or invalid.', 1;

IF @payeeHistoryBefore=0
BEGIN
    UPDATE dbo.Users
    SET Permissions = Permissions | 32
    WHERE (Permissions & 4)=4 AND (Permissions & 32)=0;
    SET @administratorsUpgraded = @@ROWCOUNT;
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
        VALUES (@payeeMigration, N'10.0.5');
    SET @historyRowsWritten += 1;
END;

-- 20260914023645_AddWeeklyCheckRequestAutomation
IF @automationHistoryBefore=0 AND COL_LENGTH(N'dbo.CheckRequests', N'ScheduledForDate') IS NULL
BEGIN
    ALTER TABLE dbo.CheckRequests ADD ScheduledForDate date NULL;
    SET @columnsAdded += 1;
END;
IF @automationHistoryBefore=0 AND COL_LENGTH(N'dbo.CheckRequests', N'TemplateId') IS NULL
BEGIN
    ALTER TABLE dbo.CheckRequests ADD TemplateId int NULL;
    SET @columnsAdded += 1;
END;
IF @automationHistoryBefore=0 AND OBJECT_ID(N'dbo.CheckRequestTemplates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CheckRequestTemplates
    (
        Id int IDENTITY(1,1) NOT NULL,
        PersonId int NOT NULL,
        Revision int NOT NULL,
        IsEnabled bit NOT NULL,
        GenerateOn nvarchar(10) NOT NULL,
        NeededByDaysAfterRequest int NOT NULL,
        PayableTo nvarchar(200) NOT NULL,
        MailingAddress nvarchar(500) NOT NULL,
        Amount decimal(18,2) NOT NULL,
        Reason nvarchar(1000) NOT NULL,
        EffectiveFrom date NOT NULL,
        CreatedAtUtc datetime2 NOT NULL,
        UpdatedAtUtc datetime2 NOT NULL,
        CONSTRAINT PK_CheckRequestTemplates PRIMARY KEY (Id),
        CONSTRAINT FK_CheckRequestTemplates_People_PersonId
            FOREIGN KEY (PersonId) REFERENCES dbo.People(Id) ON DELETE NO ACTION
    );
    SET @tablesCreated += 1;
END;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'dbo.CheckRequests') AND c.name=N'ScheduledForDate'
      AND t.name=N'date' AND c.is_nullable=1)
    THROW 52320, 'CheckRequests.ScheduledForDate has an unexpected definition.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'dbo.CheckRequests') AND c.name=N'TemplateId'
      AND t.name=N'int' AND c.is_nullable=1)
    THROW 52321, 'CheckRequests.TemplateId has an unexpected definition.', 1;
IF OBJECT_ID(N'dbo.CheckRequestTemplates', N'U') IS NULL
    THROW 52322, 'CheckRequestTemplates was not created.', 1;
IF EXISTS (
    SELECT expected.Name
    FROM (VALUES
        (N'Id', N'int', CONVERT(smallint,4), CONVERT(bit,0), CONVERT(bit,1), CONVERT(tinyint,10), CONVERT(tinyint,0)),
        (N'PersonId', N'int', CONVERT(smallint,4), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,10), CONVERT(tinyint,0)),
        (N'Revision', N'int', CONVERT(smallint,4), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,10), CONVERT(tinyint,0)),
        (N'IsEnabled', N'bit', CONVERT(smallint,1), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,1), CONVERT(tinyint,0)),
        (N'GenerateOn', N'nvarchar', CONVERT(smallint,20), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'NeededByDaysAfterRequest', N'int', CONVERT(smallint,4), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,10), CONVERT(tinyint,0)),
        (N'PayableTo', N'nvarchar', CONVERT(smallint,400), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'MailingAddress', N'nvarchar', CONVERT(smallint,1000), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'Amount', N'decimal', CONVERT(smallint,9), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,18), CONVERT(tinyint,2)),
        (N'Reason', N'nvarchar', CONVERT(smallint,2000), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'EffectiveFrom', N'date', CONVERT(smallint,3), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'CreatedAtUtc', N'datetime2', CONVERT(smallint,8), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0)),
        (N'UpdatedAtUtc', N'datetime2', CONVERT(smallint,8), CONVERT(bit,0), CONVERT(bit,0), CONVERT(tinyint,0), CONVERT(tinyint,0))
    ) expected(Name, TypeName, MaxLength, IsNullable, IsIdentity, PrecisionValue, ScaleValue)
    WHERE NOT EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
        WHERE c.object_id=OBJECT_ID(N'dbo.CheckRequestTemplates') AND c.name=expected.Name
          AND t.name=expected.TypeName AND c.max_length=expected.MaxLength
          AND c.is_nullable=expected.IsNullable AND c.is_identity=expected.IsIdentity
          AND (expected.PrecisionValue=0 OR c.precision=expected.PrecisionValue)
          AND (expected.ScaleValue=0 OR c.scale=expected.ScaleValue)))
    THROW 52323, 'CheckRequestTemplates has an unexpected column definition.', 1;

IF @automationHistoryBefore=0 AND NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CheckRequests')
      AND name=N'IX_CheckRequests_TemplateId_ScheduledForDate')
BEGIN
    EXEC(N'CREATE UNIQUE INDEX IX_CheckRequests_TemplateId_ScheduledForDate
        ON dbo.CheckRequests(TemplateId, ScheduledForDate)
        WHERE TemplateId IS NOT NULL AND ScheduledForDate IS NOT NULL;');
    SET @indexesCreated += 1;
END;
IF @automationHistoryBefore=0 AND NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CheckRequestTemplates')
      AND name=N'IX_CheckRequestTemplates_PersonId')
BEGIN
    CREATE UNIQUE INDEX IX_CheckRequestTemplates_PersonId ON dbo.CheckRequestTemplates(PersonId);
    SET @indexesCreated += 1;
END;
IF @automationHistoryBefore=0 AND NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.CheckRequests')
      AND name=N'FK_CheckRequests_CheckRequestTemplates_TemplateId')
BEGIN
    EXEC(N'ALTER TABLE dbo.CheckRequests
        ADD CONSTRAINT FK_CheckRequests_CheckRequestTemplates_TemplateId
        FOREIGN KEY (TemplateId) REFERENCES dbo.CheckRequestTemplates(Id) ON DELETE NO ACTION;');
    SET @foreignKeysCreated += 1;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CheckRequests')
    AND name=N'IX_CheckRequests_TemplateId_ScheduledForDate' AND is_unique=1 AND has_filter=1)
    THROW 52324, 'The scheduled template occurrence unique filtered index is missing or invalid.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CheckRequestTemplates')
    AND name=N'IX_CheckRequestTemplates_PersonId' AND is_unique=1)
    THROW 52325, 'The one-template-per-person unique index is missing or invalid.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.CheckRequestTemplates')
    AND name=N'FK_CheckRequestTemplates_People_PersonId' AND delete_referential_action=0)
    THROW 52326, 'The template Person restrictive foreign key is missing or invalid.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.CheckRequests')
    AND name=N'FK_CheckRequests_CheckRequestTemplates_TemplateId' AND delete_referential_action=0)
    THROW 52327, 'The CheckRequest template restrictive foreign key is missing or invalid.', 1;

IF @automationHistoryBefore=0
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
        VALUES (@automationMigration, N'10.0.5');
    SET @historyRowsWritten += 1;
END;

-- 20260914030703_AddGoalProgressToCaseNotes
IF @goalHistoryBefore=0 AND COL_LENGTH(N'dbo.Notes', N'GoalProgress') IS NULL
BEGIN
    ALTER TABLE dbo.Notes ADD GoalProgress int NULL;
    SET @columnsAdded += 1;
END;
IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'dbo.Notes') AND c.name=N'GoalProgress'
      AND t.name=N'int' AND c.is_nullable=1)
    THROW 52328, 'Notes.GoalProgress has an unexpected definition.', 1;
IF @goalHistoryBefore=0
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
        VALUES (@goalMigration, N'10.0.5');
    SET @historyRowsWritten += 1;
END;

IF @rollBackOnly=1
    ROLLBACK TRANSACTION;
ELSE
    COMMIT TRANSACTION;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id=1) AS EnvironmentName,
    @tablesCreated AS TablesCreated,
    @columnsAdded AS ColumnsAdded,
    @indexesCreated AS IndexesCreated,
    @foreignKeysCreated AS ForeignKeysCreated,
    @administratorsUpgraded AS AdministratorsUpgraded,
    @historyRowsWritten AS HistoryRowsWritten,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount,
    @rollBackOnly AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName = $reader.GetString(0)
        EnvironmentName = $reader.GetString(1)
        TablesCreated = $reader.GetInt32(2)
        ColumnsAdded = $reader.GetInt32(3)
        IndexesCreated = $reader.GetInt32(4)
        ForeignKeysCreated = $reader.GetInt32(5)
        AdministratorsUpgraded = $reader.GetInt32(6)
        HistoryRowsWritten = $reader.GetInt32(7)
        MigrationCount = $reader.GetInt64(8)
        RolledBack = $reader.GetBoolean(9)
        BackupPath = $backupPath
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
