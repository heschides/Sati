BEGIN TRANSACTION;
ALTER TABLE [Notes] ADD [IsAnnualPlan] bit NOT NULL DEFAULT CAST(0 AS bit);

ALTER TABLE [Notes] ADD [IsUnbilled] bit NOT NULL DEFAULT CAST(0 AS bit);

UPDATE [Notes] SET [IsAnnualPlan] = 1 WHERE [FormType] = 4 AND [FormId] IS NOT NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260928140919_AddAnnualPcpAndUnbilledNotes', N'10.0.5');

COMMIT;
GO
