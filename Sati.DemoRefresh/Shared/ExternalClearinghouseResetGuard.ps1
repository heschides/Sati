# The same read-only SQL runs under the exclusive SatiDemo.FullReset lock before
# baseline replacement, inside the stored restore procedure, and in the Function
# immediately before calling that procedure. A missing required table is unsafe:
# an older deployed procedure must not be allowed to bypass this guard.
function Get-DemoExternalClearinghouseResetGuardSql {
    param([switch]$RequireBaseline)

    $requireBaselineFlag = if ($RequireBaseline) { 1 } else { 0 }
    return @"
DECLARE @externalSchema sysname=N'dbo';
DECLARE @externalPrefix nvarchar(260);
DECLARE @externalSql nvarchar(max);
DECLARE @externalStateFound bit=0;
DECLARE @requireBaseline bit=$requireBaselineFlag;
WHILE @externalSchema IS NOT NULL
BEGIN
    SET @externalPrefix=QUOTENAME(@externalSchema)+N'.';
    IF @externalSchema=N'dbo' OR @requireBaseline=1
    BEGIN
        IF OBJECT_ID(@externalPrefix+N'[ClearinghouseAccounts]', N'U') IS NULL OR
           OBJECT_ID(@externalPrefix+N'[ClearinghouseDispatches]', N'U') IS NULL OR
           OBJECT_ID(@externalPrefix+N'[ClearinghouseDispatchAttempts]', N'U') IS NULL OR
           OBJECT_ID(@externalPrefix+N'[ClearinghouseResponseReceipts]', N'U') IS NULL OR
           OBJECT_ID(@externalPrefix+N'[ClearinghouseResponseMatches]', N'U') IS NULL OR
           OBJECT_ID(@externalPrefix+N'[ClearinghouseFeedCheckpoints]', N'U') IS NULL OR
           OBJECT_ID(@externalPrefix+N'[AuditEvents]', N'U') IS NULL
            THROW 51012, 'DemoResetBlockedByExternalClearinghouseState', 1;
    END

    IF OBJECT_ID(@externalPrefix+N'[ClearinghouseAccounts]', N'U') IS NOT NULL
    BEGIN
        SET @externalSql=N'IF EXISTS (SELECT 1 FROM '+@externalPrefix+
            N'[ClearinghouseAccounts] a WHERE a.ConnectorKind=2 AND LEN(LTRIM(RTRIM(a.SecretReference)))>0) SET @Found=1;';
        EXEC sys.sp_executesql @externalSql,N'@Found bit OUTPUT',@Found=@externalStateFound OUTPUT;

        IF OBJECT_ID(@externalPrefix+N'[ClearinghouseDispatches]', N'U') IS NOT NULL
        BEGIN
            SET @externalSql=N'IF EXISTS (SELECT 1 FROM '+@externalPrefix+
                N'[ClearinghouseDispatches] d JOIN '+@externalPrefix+
                N'[ClearinghouseAccounts] a ON a.Id=d.AccountId WHERE a.ConnectorKind=2 AND '+
                N'(d.State NOT IN (1,6) OR d.ExternalFileId IS NOT NULL)) SET @Found=1;';
            EXEC sys.sp_executesql @externalSql,N'@Found bit OUTPUT',@Found=@externalStateFound OUTPUT;

            IF OBJECT_ID(@externalPrefix+N'[ClearinghouseDispatchAttempts]', N'U') IS NOT NULL
            BEGIN
                SET @externalSql=N'IF EXISTS (SELECT 1 FROM '+@externalPrefix+
                    N'[ClearinghouseDispatchAttempts] attempt JOIN '+@externalPrefix+
                    N'[ClearinghouseDispatches] d ON d.Id=attempt.DispatchId JOIN '+@externalPrefix+
                    N'[ClearinghouseAccounts] a ON a.Id=d.AccountId WHERE a.ConnectorKind=2) SET @Found=1;';
                EXEC sys.sp_executesql @externalSql,N'@Found bit OUTPUT',@Found=@externalStateFound OUTPUT;
            END
        END

        IF OBJECT_ID(@externalPrefix+N'[ClearinghouseFeedCheckpoints]', N'U') IS NOT NULL
        BEGIN
            SET @externalSql=N'IF EXISTS (SELECT 1 FROM '+@externalPrefix+
                N'[ClearinghouseFeedCheckpoints] feedCheckpoint JOIN '+@externalPrefix+
                N'[ClearinghouseAccounts] a ON a.Id=feedCheckpoint.AccountId WHERE a.ConnectorKind=2 AND '+
                N'(LEN(LTRIM(RTRIM(feedCheckpoint.[Cursor])))>0 OR feedCheckpoint.LastReceiptId IS NOT NULL)) SET @Found=1;';
            EXEC sys.sp_executesql @externalSql,N'@Found bit OUTPUT',@Found=@externalStateFound OUTPUT;
        END
    END

    IF OBJECT_ID(@externalPrefix+N'[ClearinghouseResponseReceipts]', N'U') IS NOT NULL
    BEGIN
        SET @externalSql=N'IF EXISTS (SELECT 1 FROM '+@externalPrefix+
            N'[ClearinghouseResponseReceipts] receipt WHERE receipt.Source=2 OR receipt.ConnectorKind=2) SET @Found=1;';
        EXEC sys.sp_executesql @externalSql,N'@Found bit OUTPUT',@Found=@externalStateFound OUTPUT;
        IF OBJECT_ID(@externalPrefix+N'[ClearinghouseAccounts]', N'U') IS NOT NULL
        BEGIN
            SET @externalSql=N'IF EXISTS (SELECT 1 FROM '+@externalPrefix+
                N'[ClearinghouseResponseReceipts] receipt JOIN '+@externalPrefix+
                N'[ClearinghouseAccounts] a ON a.Id=receipt.AccountId WHERE a.ConnectorKind=2) SET @Found=1;';
            EXEC sys.sp_executesql @externalSql,N'@Found bit OUTPUT',@Found=@externalStateFound OUTPUT;
        END
    END

    -- An onboarding audit remains evidence even if its account row was removed.
    IF OBJECT_ID(@externalPrefix+N'[AuditEvents]', N'U') IS NOT NULL
    BEGIN
        SET @externalSql=N'IF EXISTS (SELECT 1 FROM '+@externalPrefix+
            N'[AuditEvents] WHERE Action=@OnboardAction) SET @Found=1;';
        EXEC sys.sp_executesql @externalSql,
            N'@Found bit OUTPUT,@OnboardAction nvarchar(100)',
            @Found=@externalStateFound OUTPUT,
            @OnboardAction=N'billing-clearinghouse.claimmd-test-account-onboarded';
    END
    SET @externalSchema=CASE WHEN @externalSchema=N'dbo' THEN N'demo_baseline' ELSE NULL END;
END
IF @externalStateFound=1 THROW 51012, 'DemoResetBlockedByExternalClearinghouseState', 1;
"@
}
