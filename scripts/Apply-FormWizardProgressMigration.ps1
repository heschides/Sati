<#
.SYNOPSIS
    Applies only the approved FormWizardProgress migration to identity-checked SatiDemo.
.DESCRIPTION
    Run -WhatIfOnly, then without switches, then again to verify idempotency.
    Existing objects must match the reviewed shape. No Production target or firewall action.
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
    THROW 53200, 'Refusing a non-Demo database.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
    WHERE MigrationId=N'20260928140919_AddAnnualPcpAndUnbilledNotes')
    THROW 53201, 'Predecessor migration missing.', 1;
BEGIN TRANSACTION;
DECLARE @lockResult int;
EXEC @lockResult=sys.sp_getapplock @Resource=N'SatiDemo.FullReset',
    @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=60000;
IF @lockResult < 0 THROW 53202, 'Demo is busy; migration did not begin.', 1;
DECLARE @applied bit = CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
    WHERE MigrationId=N'20260929185110_AddFormWizardProgress') THEN 1 ELSE 0 END;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 121 + CONVERT(int,@applied)
    THROW 53203, 'Unexpected migration boundary.', 1;
IF @applied=1 AND OBJECT_ID(N'dbo.FormWizardProgress',N'U') IS NULL
    THROW 53204, 'Migration recorded but table missing.', 1;
DECLARE @created bit=0;
IF OBJECT_ID(N'dbo.FormWizardProgress',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FormWizardProgress (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FormWizardProgress PRIMARY KEY,
        PersonId int NOT NULL, AgencyId int NOT NULL, AuthorUserId int NOT NULL,
        FormKey nvarchar(60) NOT NULL, Revision int NOT NULL, StepIndex int NOT NULL,
        UpdatedAtUtc datetime2 NOT NULL, Ciphertext varbinary(max) NOT NULL,
        Nonce varbinary(max) NOT NULL, Tag varbinary(max) NOT NULL,
        WrappedKey varbinary(max) NOT NULL, KeyId nvarchar(300) NOT NULL,
        CONSTRAINT FK_FormWizardProgress_People_PersonId FOREIGN KEY(PersonId)
            REFERENCES dbo.People(Id) ON DELETE CASCADE
    );
    SET @created=1;
END;
DECLARE @table int=OBJECT_ID(N'dbo.FormWizardProgress');
DECLARE @expected TABLE (name sysname, type sysname, length smallint, scale tinyint, identityFlag bit);
INSERT @expected VALUES
    (N'Id',N'int',4,0,1), (N'PersonId',N'int',4,0,0),
    (N'AgencyId',N'int',4,0,0), (N'AuthorUserId',N'int',4,0,0),
    (N'FormKey',N'nvarchar',120,0,0), (N'Revision',N'int',4,0,0),
    (N'StepIndex',N'int',4,0,0), (N'UpdatedAtUtc',N'datetime2',8,7,0),
    (N'Ciphertext',N'varbinary',-1,0,0), (N'Nonce',N'varbinary',-1,0,0),
    (N'Tag',N'varbinary',-1,0,0), (N'WrappedKey',N'varbinary',-1,0,0),
    (N'KeyId',N'nvarchar',600,0,0);
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=@table)<>13 OR EXISTS (
    SELECT 1 FROM @expected e LEFT JOIN sys.columns c ON c.object_id=@table AND c.name=e.name
    LEFT JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.column_id IS NULL OR t.name<>e.type OR c.max_length<>e.length OR c.scale<>e.scale
      OR c.is_nullable<>0 OR c.is_identity<>e.identityFlag OR c.is_computed<>0
      OR c.default_object_id<>0)
    THROW 53205, 'Existing wizard table column shape differs from the reviewed migration.', 1;
IF IDENT_SEED(N'dbo.FormWizardProgress')<>1 OR IDENT_INCR(N'dbo.FormWizardProgress')<>1
    THROW 53206, 'Unexpected identity definition.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    WHERE i.object_id=@table AND i.is_primary_key=1 AND ic.key_ordinal=1
      AND COL_NAME(@table,ic.column_id)=N'Id'
      AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id=@table AND x.index_id=i.index_id)=1)
    THROW 53207, 'Unexpected primary key.', 1;
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=@table)<>1 OR NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id=f.object_id
    WHERE f.parent_object_id=@table AND f.referenced_object_id=OBJECT_ID(N'dbo.People')
      AND f.delete_referential_action=1 AND f.update_referential_action=0
      AND f.is_disabled=0 AND f.is_not_trusted=0
      AND COL_NAME(@table,c.parent_column_id)=N'PersonId'
      AND COL_NAME(f.referenced_object_id,c.referenced_column_id)=N'Id'
      AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1)
    THROW 53208, 'Unexpected People foreign key.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=@table AND name=N'IX_FormWizardProgress_AgencyId_UpdatedAtUtc')
    CREATE INDEX IX_FormWizardProgress_AgencyId_UpdatedAtUtc ON dbo.FormWizardProgress(AgencyId,UpdatedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=@table AND name=N'IX_FormWizardProgress_PersonId_AuthorUserId_FormKey')
    CREATE UNIQUE INDEX IX_FormWizardProgress_PersonId_AuthorUserId_FormKey ON dbo.FormWizardProgress(PersonId,AuthorUserId,FormKey);
IF EXISTS (
    SELECT 1 FROM (VALUES
        (N'IX_FormWizardProgress_AgencyId_UpdatedAtUtc',0,N'AgencyId,UpdatedAtUtc'),
        (N'IX_FormWizardProgress_PersonId_AuthorUserId_FormKey',1,N'PersonId,AuthorUserId,FormKey')) e(name,uniqueFlag,keys)
    LEFT JOIN sys.indexes i ON i.object_id=@table AND i.name=e.name
    WHERE i.index_id IS NULL OR i.is_unique<>e.uniqueFlag OR i.is_disabled<>0 OR i.has_filter<>0 OR i.type<>2
      OR (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(@table,c.column_id)),N',') WITHIN GROUP(ORDER BY c.key_ordinal)
          FROM sys.index_columns c WHERE c.object_id=@table AND c.index_id=i.index_id AND c.key_ordinal>0)<>e.keys
      OR EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=@table AND c.index_id=i.index_id
                 AND (c.is_included_column=1 OR c.is_descending_key=1)))
    THROW 53209, 'Unexpected wizard index definition.', 1;
IF @applied=0
    INSERT dbo.__EFMigrationsHistory(MigrationId,ProductVersion)
    VALUES(N'20260929185110_AddFormWizardProgress',N'10.0.5');
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>122
    THROW 53210, 'Migration count verification failed.', 1;
IF @rollback=1 ROLLBACK TRANSACTION; ELSE COMMIT TRANSACTION;
SELECT CASE WHEN @rollback=1 THEN N'ROLLBACK_REHEARSAL_PASSED'
            WHEN @applied=1 THEN N'IDEMPOTENCY_VERIFIED'
            ELSE N'FORM_WIZARD_PROGRESS_APPLIED' END AS Result,
    @created AS TableCreatedInTransaction,
    (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS PersistedMigrationCount;
'@
    $table = [System.Data.DataTable]::new()
    $reader = $command.ExecuteReader()
    try { $table.Load($reader) } finally { $reader.Dispose() }
    $table | Format-Table -AutoSize
}
finally { $connection.Dispose() }
