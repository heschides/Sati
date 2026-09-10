<#
.SYNOPSIS
    Applies and verifies the CheckRequests migration to the hosted Demo database.

.DESCRIPTION
    This runner is deliberately narrower than `dotnet ef database update`. It refuses any
    database except identity-marked SatiDemo, inspects the real schema as well as migration
    history, and is transactional and rerunnable. Use -WhatIfOnly first to execute every check
    and roll the transaction back, then run normally, then run normally once more to prove
    idempotency.

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    ./scripts/Apply-CheckRequestMigration.ps1 -AccessToken $token -WhatIfOnly
    ./scripts/Apply-CheckRequestMigration.ps1 -AccessToken $token
    ./scripts/Apply-CheckRequestMigration.ps1 -AccessToken $token
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AccessToken,

    [string]$SqlServer = 'sati-demo-satilogica-central.database.windows.net',

    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
$migrationId = '20260909153255_AddCheckRequests'
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

IF DB_NAME() <> N'SatiDemo'
    THROW 52000, 'The connected database is not SatiDemo.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 52001, 'dbo.SatiDatabaseIdentity is missing.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 52002, 'The database identity marker is not Demo.', 1;
IF OBJECT_ID(N'dbo.People', N'U') IS NULL
    THROW 52003, 'dbo.People is missing; this is not the expected Sati schema.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 52004, 'dbo.__EFMigrationsHistory is missing.', 1;
IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId)
   AND OBJECT_ID(N'dbo.CheckRequests', N'U') IS NULL
    THROW 52005, 'Migration history says CheckRequests is applied, but the table is missing.', 1;

DECLARE @tableCreated bit = 0;
DECLARE @historyWritten bit = 0;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.CheckRequests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CheckRequests
    (
        Id int IDENTITY(1,1) NOT NULL,
        Revision int NOT NULL,
        PersonId int NOT NULL,
        ConsumerName nvarchar(200) NOT NULL,
        AgencyName nvarchar(200) NOT NULL,
        CaseManagerName nvarchar(200) NOT NULL,
        SupervisorName nvarchar(200) NOT NULL,
        RequestDate date NULL,
        PayableTo nvarchar(200) NULL,
        MailingAddress nvarchar(500) NULL,
        Amount decimal(18,2) NOT NULL,
        NeededByDate date NULL,
        Reason nvarchar(1000) NULL,
        CreatedAtUtc datetime2 NOT NULL,
        PublishedAtUtc datetime2 NULL,
        PublishedByUserId int NULL,
        PublishedByName nvarchar(200) NULL,
        CONSTRAINT PK_CheckRequests PRIMARY KEY (Id),
        CONSTRAINT FK_CheckRequests_People_PersonId FOREIGN KEY (PersonId)
            REFERENCES dbo.People(Id) ON DELETE NO ACTION
    );
    CREATE INDEX IX_CheckRequests_PersonId_RequestDate
        ON dbo.CheckRequests(PersonId, RequestDate);
    SET @tableCreated = 1;
END;

-- Validate every column's persistence semantics, not merely its name.
IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
               WHERE c.object_id=OBJECT_ID(N'dbo.CheckRequests') AND c.name=N'Id'
                 AND t.name=N'int' AND c.is_nullable=0 AND c.is_identity=1)
    THROW 52010, 'CheckRequests.Id has an unexpected definition.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
               WHERE c.object_id=OBJECT_ID(N'dbo.CheckRequests') AND c.name=N'Revision'
                 AND t.name=N'int' AND c.is_nullable=0)
    THROW 52011, 'CheckRequests.Revision has an unexpected definition.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
               WHERE c.object_id=OBJECT_ID(N'dbo.CheckRequests') AND c.name=N'PersonId'
                 AND t.name=N'int' AND c.is_nullable=0)
    THROW 52012, 'CheckRequests.PersonId has an unexpected definition.', 1;
IF EXISTS (
    SELECT expected.Name
    FROM (VALUES
        (N'ConsumerName', 400, CONVERT(bit, 0)),
        (N'AgencyName', 400, CONVERT(bit, 0)),
        (N'CaseManagerName', 400, CONVERT(bit, 0)),
        (N'SupervisorName', 400, CONVERT(bit, 0)),
        (N'PayableTo', 400, CONVERT(bit, 1)),
        (N'MailingAddress', 1000, CONVERT(bit, 1)),
        (N'Reason', 2000, CONVERT(bit, 1)),
        (N'PublishedByName', 400, CONVERT(bit, 1))
    ) expected(Name, MaxLength, IsNullable)
    WHERE NOT EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
        WHERE c.object_id=OBJECT_ID(N'dbo.CheckRequests') AND c.name=expected.Name
          AND t.name=N'nvarchar' AND c.max_length=expected.MaxLength
          AND c.is_nullable=expected.IsNullable))
    THROW 52013, 'A CheckRequests text column has an unexpected definition.', 1;
IF EXISTS (
    SELECT expected.Name
    FROM (VALUES
        (N'RequestDate', N'date', CONVERT(bit, 1)),
        (N'NeededByDate', N'date', CONVERT(bit, 1)),
        (N'CreatedAtUtc', N'datetime2', CONVERT(bit, 0)),
        (N'PublishedAtUtc', N'datetime2', CONVERT(bit, 1))
    ) expected(Name, TypeName, IsNullable)
    WHERE NOT EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
        WHERE c.object_id=OBJECT_ID(N'dbo.CheckRequests') AND c.name=expected.Name
          AND t.name=expected.TypeName AND c.is_nullable=expected.IsNullable))
    THROW 52014, 'A CheckRequests date column has an unexpected definition.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
               WHERE c.object_id=OBJECT_ID(N'dbo.CheckRequests') AND c.name=N'Amount'
                 AND t.name=N'decimal' AND c.precision=18 AND c.scale=2 AND c.is_nullable=0)
    THROW 52015, 'CheckRequests.Amount has an unexpected definition.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
               WHERE c.object_id=OBJECT_ID(N'dbo.CheckRequests') AND c.name=N'PublishedByUserId'
                 AND t.name=N'int' AND c.is_nullable=1)
    THROW 52016, 'CheckRequests.PublishedByUserId has an unexpected definition.', 1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CheckRequests')) <> 17
    THROW 52017, 'CheckRequests has an unexpected column count.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.key_constraints kc
    JOIN sys.index_columns ic ON ic.object_id=kc.parent_object_id AND ic.index_id=kc.unique_index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE kc.parent_object_id=OBJECT_ID(N'dbo.CheckRequests') AND kc.type=N'PK'
      AND c.name=N'Id' AND ic.key_ordinal=1)
    THROW 52018, 'CheckRequests does not have the expected primary key.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
    JOIN sys.columns parentColumn ON parentColumn.object_id=fkc.parent_object_id AND parentColumn.column_id=fkc.parent_column_id
    JOIN sys.columns referencedColumn ON referencedColumn.object_id=fkc.referenced_object_id AND referencedColumn.column_id=fkc.referenced_column_id
    WHERE fk.parent_object_id=OBJECT_ID(N'dbo.CheckRequests')
      AND fk.referenced_object_id=OBJECT_ID(N'dbo.People')
      AND parentColumn.name=N'PersonId' AND referencedColumn.name=N'Id'
      AND fk.delete_referential_action=0)
    THROW 52019, 'CheckRequests does not have the expected restrictive People foreign key.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.object_id=OBJECT_ID(N'dbo.CheckRequests')
      AND i.name=N'IX_CheckRequests_PersonId_RequestDate' AND i.is_unique=0)
   OR (SELECT COUNT(*) FROM sys.index_columns ic
       WHERE ic.object_id=OBJECT_ID(N'dbo.CheckRequests')
         AND ic.index_id=(SELECT index_id FROM sys.indexes
                          WHERE object_id=OBJECT_ID(N'dbo.CheckRequests')
                            AND name=N'IX_CheckRequests_PersonId_RequestDate')
         AND ic.key_ordinal > 0) <> 2
   OR NOT EXISTS (
       SELECT 1 FROM sys.indexes i
       JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
       JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
       WHERE i.object_id=OBJECT_ID(N'dbo.CheckRequests')
         AND i.name=N'IX_CheckRequests_PersonId_RequestDate'
         AND ((ic.key_ordinal=1 AND c.name=N'PersonId') OR (ic.key_ordinal=2 AND c.name=N'RequestDate'))
       GROUP BY i.object_id, i.index_id HAVING COUNT(*)=2)
    THROW 52020, 'CheckRequests does not have the expected PersonId/RequestDate index.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@migrationId)
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (@migrationId, N'10.0.5');
    SET @historyWritten = 1;
END;

DECLARE @migrationCount bigint = (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory);
IF @rollBackOnly = 1
    ROLLBACK TRANSACTION;
ELSE
    COMMIT TRANSACTION;

SELECT DB_NAME() AS DatabaseName,
       (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id=1) AS EnvironmentName,
       @tableCreated AS TableCreated,
       @historyWritten AS HistoryWritten,
       @migrationCount AS MigrationCount,
       @rollBackOnly AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The CheckRequests migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName   = $reader.GetString(0)
        EnvironmentName = $reader.GetString(1)
        TableCreated   = $reader.GetBoolean(2)
        HistoryWritten = $reader.GetBoolean(3)
        MigrationCount = $reader.GetInt64(4)
        RolledBack     = $reader.GetBoolean(5)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
