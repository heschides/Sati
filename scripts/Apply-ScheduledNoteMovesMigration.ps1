<#
.SYNOPSIS
    Applies only the approved ScheduledNoteMoves migration to identity-checked SatiDemo.
.DESCRIPTION
    Run -WhatIfOnly, then without switches, then again to verify idempotency.
    Existing objects must match the reviewed shape. This script never changes a firewall rule.
#>
[CmdletBinding()]
param([switch]$WhatIfOnly)
$ErrorActionPreference = 'Stop'
$token = az account get-access-token --resource 'https://database.windows.net/' --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) { throw 'Azure SQL token unavailable.' }
$connection = [System.Data.SqlClient.SqlConnection]::new(
    'Server=sati-demo-satilogica-central.database.windows.net;Database=SatiDemo;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;')
$connection.AccessToken = $token.Trim()
$token = $null
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 180
    [void]$command.Parameters.AddWithValue('@rollback', [bool]$WhatIfOnly)
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'SatiDemo' OR NOT EXISTS
    (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id=1 AND EnvironmentName=N'Demo')
    THROW 53500, 'Refusing a non-Demo database.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
    WHERE MigrationId=N'20260930142103_AddConsumerSchedule')
    THROW 53501, 'Predecessor migration missing.', 1;
BEGIN TRANSACTION;
DECLARE @lockResult int;
EXEC @lockResult=sys.sp_getapplock @Resource=N'SatiDemo.FullReset',
    @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=60000;
IF @lockResult < 0 THROW 53502, 'Demo is busy; migration did not begin.', 1;
DECLARE @applied bit = CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
    WHERE MigrationId=N'20261002221023_AddScheduledNoteMoves') THEN 1 ELSE 0 END;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 123 + CONVERT(int,@applied)
    THROW 53503, 'Unexpected migration boundary.', 1;
IF OBJECT_ID(N'dbo.ScheduledNoteMoves') IS NOT NULL AND OBJECT_ID(N'dbo.ScheduledNoteMoves',N'U') IS NULL
    THROW 53504, 'ScheduledNoteMoves name is occupied by a different object.', 1;
IF @applied=1 AND OBJECT_ID(N'dbo.ScheduledNoteMoves',N'U') IS NULL
    THROW 53505, 'Migration recorded but scheduled-note move table missing.', 1;
DECLARE @created bit=0;
IF OBJECT_ID(N'dbo.ScheduledNoteMoves',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ScheduledNoteMoves (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ScheduledNoteMoves PRIMARY KEY,
        NoteId int NOT NULL,
        PersonId int NOT NULL,
        AgencyId int NOT NULL,
        UserId int NOT NULL,
        FromDate date NOT NULL,
        ToDate date NOT NULL,
        ScheduledMinutes int NULL,
        ScheduledUnits int NULL,
        NoteRevision int NOT NULL,
        MovedAtUtc datetime2 NOT NULL,
        CONSTRAINT FK_ScheduledNoteMoves_Notes_NoteId FOREIGN KEY(NoteId)
            REFERENCES dbo.Notes(Id) ON DELETE CASCADE
    );
    SET @created=1;
END;
DECLARE @table int=OBJECT_ID(N'dbo.ScheduledNoteMoves',N'U');
DECLARE @expected TABLE (name sysname, type sysname, length smallint, scale tinyint,
                         nullable bit, identityFlag bit);
INSERT @expected VALUES
    (N'Id',N'bigint',8,0,0,1),
    (N'NoteId',N'int',4,0,0,0),
    (N'PersonId',N'int',4,0,0,0),
    (N'AgencyId',N'int',4,0,0,0),
    (N'UserId',N'int',4,0,0,0),
    (N'FromDate',N'date',3,0,0,0),
    (N'ToDate',N'date',3,0,0,0),
    (N'ScheduledMinutes',N'int',4,0,1,0),
    (N'ScheduledUnits',N'int',4,0,1,0),
    (N'NoteRevision',N'int',4,0,0,0),
    (N'MovedAtUtc',N'datetime2',8,7,0,0);
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=@table)<>11 OR EXISTS (
    SELECT 1 FROM @expected e LEFT JOIN sys.columns c ON c.object_id=@table AND c.name=e.name
    LEFT JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.column_id IS NULL OR t.name<>e.type OR c.max_length<>e.length OR c.scale<>e.scale
      OR c.is_nullable<>e.nullable OR c.is_identity<>e.identityFlag OR c.is_computed<>0
      OR c.default_object_id<>0)
    THROW 53506, 'Scheduled-note move column shape differs from the reviewed migration.', 1;
IF IDENT_SEED(N'dbo.ScheduledNoteMoves')<>1 OR IDENT_INCR(N'dbo.ScheduledNoteMoves')<>1
    THROW 53507, 'Unexpected identity definition.', 1;
IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id=@table AND is_primary_key=1)<>1 OR NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    WHERE i.object_id=@table AND i.name=N'PK_ScheduledNoteMoves'
      AND i.is_primary_key=1 AND i.is_unique=1 AND i.type=1
      AND ic.key_ordinal=1 AND ic.is_descending_key=0
      AND COL_NAME(@table,ic.column_id)=N'Id'
      AND (SELECT COUNT(*) FROM sys.index_columns x
           WHERE x.object_id=@table AND x.index_id=i.index_id)=1)
    THROW 53508, 'Unexpected scheduled-note move primary key.', 1;
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=@table)<>1 OR NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id=f.object_id
    WHERE f.parent_object_id=@table AND f.name=N'FK_ScheduledNoteMoves_Notes_NoteId'
      AND f.referenced_object_id=OBJECT_ID(N'dbo.Notes',N'U')
      AND f.delete_referential_action=1 AND f.update_referential_action=0
      AND f.is_disabled=0 AND f.is_not_trusted=0
      AND COL_NAME(@table,c.parent_column_id)=N'NoteId'
      AND COL_NAME(f.referenced_object_id,c.referenced_column_id)=N'Id'
      AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1)
    THROW 53509, 'Unexpected Notes foreign key.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=@table AND name=N'IX_ScheduledNoteMoves_NoteId_NoteRevision')
    CREATE INDEX IX_ScheduledNoteMoves_NoteId_NoteRevision
        ON dbo.ScheduledNoteMoves(NoteId,NoteRevision);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=@table AND name=N'IX_ScheduledNoteMoves_UserId_FromDate')
    CREATE INDEX IX_ScheduledNoteMoves_UserId_FromDate
        ON dbo.ScheduledNoteMoves(UserId,FromDate);
IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id=@table AND is_primary_key=0 AND index_id>0)<>2
   OR NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.object_id=@table AND i.name=N'IX_ScheduledNoteMoves_NoteId_NoteRevision'
      AND i.is_unique=0 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0 AND i.type=2
      AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(@table,c.column_id)),N',')
          WITHIN GROUP(ORDER BY c.key_ordinal)
          FROM sys.index_columns c WHERE c.object_id=@table AND c.index_id=i.index_id
            AND c.key_ordinal>0)=N'NoteId,NoteRevision'
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=@table AND c.index_id=i.index_id
                      AND (c.is_included_column=1 OR c.is_descending_key=1)))
   OR NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.object_id=@table AND i.name=N'IX_ScheduledNoteMoves_UserId_FromDate'
      AND i.is_unique=0 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0 AND i.type=2
      AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(@table,c.column_id)),N',')
          WITHIN GROUP(ORDER BY c.key_ordinal)
          FROM sys.index_columns c WHERE c.object_id=@table AND c.index_id=i.index_id
            AND c.key_ordinal>0)=N'UserId,FromDate'
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=@table AND c.index_id=i.index_id
                      AND (c.is_included_column=1 OR c.is_descending_key=1)))
    THROW 53510, 'Unexpected scheduled-note move index definition.', 1;
IF @applied=0
    INSERT dbo.__EFMigrationsHistory(MigrationId,ProductVersion)
    VALUES(N'20261002221023_AddScheduledNoteMoves',N'10.0.5');
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>124
    THROW 53511, 'Migration count verification failed.', 1;
IF @rollback=1 ROLLBACK TRANSACTION; ELSE COMMIT TRANSACTION;
SELECT CASE WHEN @rollback=1 THEN N'ROLLBACK_REHEARSAL_PASSED'
            WHEN @applied=1 THEN N'IDEMPOTENCY_VERIFIED'
            ELSE N'SCHEDULED_NOTE_MOVES_APPLIED' END AS Result,
    @created AS TableCreatedInTransaction,
    (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS PersistedMigrationCount;
'@
    $table = [System.Data.DataTable]::new()
    $reader = $command.ExecuteReader()
    try { $table.Load($reader) } finally { $reader.Dispose() }
    $table | Format-Table -AutoSize
}
finally { $connection.Dispose() }
