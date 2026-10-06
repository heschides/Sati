SET XACT_ABORT ON;
IF @@TRANCOUNT = 0 THROW 53700, 'Caller must own the migration transaction.', 1;
IF DB_NAME() <> @expectedDatabase OR
   (@expectedDatabase <> N'SatiDemo' AND @expectedDatabase NOT LIKE N'SatiSyntheticPipeline[_]%') OR
   NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id=1 AND EnvironmentName=N'Demo')
    THROW 53700, 'Refusing a database outside identity-checked Demo or a private synthetic rehearsal.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL
    THROW 53701, 'Migration history is missing.', 1;
DECLARE @migration nvarchar(150)=N'20261004204633_AddPayerBillingConfigurationVersions';
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@migration)
BEGIN
    IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>125 OR
       (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory)<>N'20261004120026_AddNoteAmendments'
        THROW 53701, 'Expected the reviewed 125-migration predecessor.', 1;
END
ELSE IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>126 OR
        (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory)<>@migration
    THROW 53701, 'Expected exactly the reviewed 126-migration outcome.', 1;

IF OBJECT_ID(N'dbo.PayerBillingConfigurationVersions',N'U') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@migration)
        THROW 53702, 'History reports a migration whose table is missing.', 1;
    CREATE TABLE dbo.PayerBillingConfigurationVersions
    (
        VersionId uniqueidentifier NOT NULL CONSTRAINT PK_PayerBillingConfigurationVersions PRIMARY KEY,
        AgencyId int NOT NULL,
        ProfileKey nvarchar(40) NOT NULL,
        Revision bigint NOT NULL,
        EffectiveOn date NOT NULL,
        ConfigurationJson nvarchar(max) NOT NULL,
        CreatedByUserId int NOT NULL,
        RecordedAtUtc datetime2 NOT NULL,
        CONSTRAINT FK_PayerBillingConfigurationVersions_Agencies_AgencyId FOREIGN KEY (AgencyId) REFERENCES dbo.Agencies(Id),
        CONSTRAINT FK_PayerBillingConfigurationVersions_Users_CreatedByUserId FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(Id)
    );
    CREATE UNIQUE INDEX IX_PayerBillingConfigurationVersions_AgencyId_ProfileKey_Revision
        ON dbo.PayerBillingConfigurationVersions(AgencyId,ProfileKey,Revision);
    CREATE UNIQUE INDEX IX_PayerBillingConfigurationVersions_AgencyId_ProfileKey_EffectiveOn
        ON dbo.PayerBillingConfigurationVersions(AgencyId,ProfileKey,EffectiveOn);
    CREATE INDEX IX_PayerBillingConfigurationVersions_CreatedByUserId
        ON dbo.PayerBillingConfigurationVersions(CreatedByUserId);
END;
DECLARE @table int=OBJECT_ID(N'dbo.PayerBillingConfigurationVersions');
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=@table)<>8 OR EXISTS
(
    SELECT * FROM (VALUES
     (N'VersionId',N'uniqueidentifier',16),(N'AgencyId',N'int',4),(N'ProfileKey',N'nvarchar',80),
     (N'Revision',N'bigint',8),(N'EffectiveOn',N'date',3),(N'ConfigurationJson',N'nvarchar',-1),
     (N'CreatedByUserId',N'int',4),(N'RecordedAtUtc',N'datetime2',8)) expected(name,type,length)
    WHERE NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
        WHERE c.object_id=@table AND c.name=expected.name AND t.name=expected.type AND c.max_length=expected.length
        AND c.is_nullable=0 AND c.is_identity=0 AND c.is_computed=0 AND (c.name<>N'RecordedAtUtc' OR c.scale=7))
)
    THROW 53702, 'Payer configuration columns differ from the reviewed model.', 1;
IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id=@table AND index_id>0)<>4 OR EXISTS
(
    SELECT * FROM (VALUES
      (N'PK_PayerBillingConfigurationVersions',1,1,N'VersionId'),
      (N'IX_PayerBillingConfigurationVersions_AgencyId_ProfileKey_Revision',1,0,N'AgencyId,ProfileKey,Revision'),
      (N'IX_PayerBillingConfigurationVersions_AgencyId_ProfileKey_EffectiveOn',1,0,N'AgencyId,ProfileKey,EffectiveOn'),
      (N'IX_PayerBillingConfigurationVersions_CreatedByUserId',0,0,N'CreatedByUserId')) expected(name,uniqueIndex,primaryIndex,columns)
    WHERE NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=@table AND i.name=expected.name
        AND i.is_unique=expected.uniqueIndex AND i.is_primary_key=expected.primaryIndex AND i.has_filter=0 AND i.is_disabled=0
        AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
            FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE ic.object_id=@table AND ic.index_id=i.index_id AND ic.key_ordinal>0)=expected.columns)
)
    THROW 53702, 'Payer configuration keys/indexes differ from the reviewed model.', 1;
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=@table)<>2 OR EXISTS
(
    SELECT * FROM (VALUES (N'AgencyId',N'Agencies'),(N'CreatedByUserId',N'Users')) expected(columnName,referencedTable)
    WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id
        JOIN sys.columns c ON c.object_id=@table AND c.column_id=fc.parent_column_id
        JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id
        WHERE fk.parent_object_id=@table AND c.name=expected.columnName AND rc.name=N'Id'
        AND fc.referenced_object_id=OBJECT_ID(N'dbo.'+expected.referencedTable)
        AND fk.delete_referential_action=0 AND fk.update_referential_action=0 AND fk.is_disabled=0 AND fk.is_not_trusted=0)
)
    THROW 53702, 'Payer configuration ownership relationships differ from the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=@migration)
    INSERT dbo.__EFMigrationsHistory(MigrationId,ProductVersion) VALUES(@migration,N'10.0.5');
