using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Sati.Tests;

public sealed class ComplianceMigrationTests
{
    [Fact]
    public void AnnualIdentityIsStagedValidatedAndThenMadeRequired()
    {
        var operations = new Migrations.CorrectAnnualComplianceAndBillingPolicy().UpOperations;

        var add = Assert.Single(operations.OfType<AddColumnOperation>(), operation =>
            operation.Table == "Forms" && operation.Name == "TargetEffectiveDate");
        Assert.True(add.IsNullable);
        Assert.Null(add.DefaultValue);

        var makeRequired = Assert.Single(operations.OfType<AlterColumnOperation>(), operation =>
            operation.Table == "Forms" && operation.Name == "TargetEffectiveDate");
        Assert.False(makeRequired.IsNullable);

        var index = Assert.Single(operations.OfType<CreateIndexOperation>(), operation =>
            operation.Name == "IX_Forms_PersonId_Type_TargetEffectiveDate");
        Assert.True(index.IsUnique);
        Assert.Null(index.Filter);
        Assert.Equal(["PersonId", "Type", "TargetEffectiveDate"], index.Columns);

        var check = Assert.Single(operations.OfType<AddCheckConstraintOperation>(), operation =>
            operation.Name == "CK_Forms_TargetEffectiveDate_Valid");
        Assert.Contains("1900-01-01", check.Sql);
    }

    [Fact]
    public void BackfillCorrectsTheLegacyOneYearShiftAndPreservesClinicalEvidence()
    {
        var operations = new Migrations.CorrectAnnualComplianceAndBillingPolicy().UpOperations;
        var sql = string.Join("\n", operations.OfType<SqlOperation>().Select(operation => operation.Sql));

        Assert.Contains("every form must belong to a person with an EffectiveDate", sql);
        Assert.Contains("dbo.Forms contains an unknown Type", sql);
        Assert.Contains("Latin1_General_100_BIN2", sql);
        Assert.Contains("DATALENGTH(f.[Type]) = DATALENGTH(supported.[Type])", sql);
        Assert.Contains("greatest effective-date anniversary strictly", sql);
        Assert.DoesNotContain("first anniversary on", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHEN a.Anniversary >= a.DueDate", sql);
        Assert.Contains("a.AnniversaryNumber - 1", sql);
        Assert.DoesNotContain("a.AnniversaryNumber + 1", sql);
        Assert.Contains("documented post-backfill legacy calculator", sql);
        Assert.Contains("post-2026-06-29 deadline shape", sql);
        Assert.Contains("DATEADD(day, -60, nextCycle.NextAnniversary)", sql);
        Assert.Contains("DATEADD(day, -120, nextCycle.NextAnniversary)", sql);
        Assert.Contains("NOT BETWEEN 0 AND 364", sql);
        Assert.Contains("TargetEffectiveDate >= '9999-01-01'", sql);
        Assert.Contains("s.Q4RDaysBeforeAnniversary = 1", sql);
        Assert.Contains("q4.TargetEffectiveDate = annualForm.TargetEffectiveDate", sql);
        Assert.Contains("duplicate (PersonId, Type, TargetEffectiveDate)", sql);
        Assert.Contains("Legacy note compliance overrides cannot be converted", sql);
        Assert.Contains("longer than 4000 characters", sql);
        Assert.Contains("WHEN N'Q4R' THEN DATEADD(day, 360, f.TargetEffectiveDate)", sql);
        Assert.Contains("WHEN N'PCP' THEN DATEADD(day, -s.PcpDaysBeforeAnniversary, f.TargetEffectiveDate)", sql);
        Assert.Contains("SET DueDate = d.NewDueDate", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("form.target-effective-date-assigned", sql);
        Assert.Contains("form.deadline-corrected", sql);
        Assert.Contains("oldDueDate", sql);
        Assert.Contains("newDueDate", sql);
        Assert.DoesNotContain("SET CompletedDate", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET OpenedDate", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.FormAttestations", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(operations, operation => operation is UpdateDataOperation);
    }

    [Fact]
    public void OnlyRecognizedLegacySettingDefaultsAreCorrectedAndAuditedAsSystemWork()
    {
        var sql = string.Join("\n",
            new Migrations.CorrectAnnualComplianceAndBillingPolicy().UpOperations
                .OfType<SqlOperation>()
                .Select(operation => operation.Sql));

        Assert.Contains("settings.compliance-defaults-corrected", sql);
        Assert.Contains("ActorUserId", sql);
        Assert.Contains("s.BillingComplianceRequirements = 31", sql);
        Assert.Contains("ReclassificationOpenDaysBefore = 15", sql);
        Assert.Contains("SafetyPlanOpenDaysBefore = 60", sql);
        Assert.Contains("PrivacyPracticesOpenDaysBefore = 30", sql);
        Assert.Contains("ReleaseAgencyOpenDaysBefore = 30", sql);
        Assert.Contains("ReleaseDhhsOpenDaysBefore = 30", sql);
        Assert.Contains("ReleaseMedicalOpenDaysBefore = 30", sql);
        Assert.Contains("CompAssessmentDaysBeforeAnniversary IN (60, 120)", sql);
        Assert.Contains("THEN 7 ELSE BillingComplianceRequirements", sql);
        Assert.Contains("THEN 90 ELSE CompAssessmentDaysBeforeAnniversary", sql);
    }

    [Fact]
    public void MigrationContainsTheNewRetainedComplianceSchema()
    {
        var migration = new Migrations.CorrectAnnualComplianceAndBillingPolicy();
        var operations = migration.UpOperations;
        var tables = operations.OfType<CreateTableOperation>()
            .Select(operation => operation.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("BillingCompliancePolicyVersions", tables);
        Assert.Contains("BillingComplianceRecoveryDecisions", tables);
        Assert.Contains("BillingComplianceRecoveryObligations", tables);
        Assert.Contains("BillingComplianceRecoveryNotes", tables);
        Assert.Contains("ReleaseObligations", tables);
        Assert.Contains("ReleaseObligationAttestations", tables);
        Assert.Contains("ReleaseAuthorizationEvents", tables);
        Assert.Contains("SignatureComplianceProjections", tables);

        var assignmentKnownOn = Assert.Single(operations.OfType<AddColumnOperation>(), operation =>
            operation.Table == "PersonProviders" && operation.Name == "AssignmentKnownOn");
        Assert.True(assignmentKnownOn.IsNullable);

        var rollbackGuard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51001", rollbackGuard.Sql);
        Assert.Contains("the prior schema cannot represent", rollbackGuard.Sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IF EXISTS (SELECT 1 FROM dbo.Forms)", rollbackGuard.Sql);
        Assert.Contains("migration-CorrectAnnualComplianceAndBillingPolicy", rollbackGuard.Sql);

        var drops = migration.DownOperations.OfType<DropTableOperation>()
            .Select((operation, index) => (operation.Name, index))
            .ToDictionary(item => item.Name, item => item.index, StringComparer.Ordinal);
        Assert.True(drops["SignatureComplianceProjections"] <
                    drops["ReleaseObligationAttestations"]);
        Assert.True(drops["ReleaseObligationAttestations"] < drops["ReleaseObligations"]);
        Assert.True(drops["ReleaseAuthorizationEvents"] < drops["ReleaseObligations"]);
        Assert.True(drops["BillingComplianceRecoveryNotes"] <
                    drops["BillingComplianceRecoveryDecisions"]);
        Assert.True(drops["BillingComplianceRecoveryObligations"] <
                    drops["BillingComplianceRecoveryDecisions"]);
    }

    [Fact]
    public void UpgradeScriptCanBeGeneratedWithoutConnectingToADataStore()
    {
        var options = new DbContextOptionsBuilder<Sati.Data.SatiContext>()
            .UseSqlServer(
                "Server=synthetic.invalid;Database=ModelOnly;Integrated Security=true;Encrypt=true")
            .Options;
        using var context = new Sati.Data.SatiContext(options);
        var script = context.GetService<IMigrator>().GenerateScript(
            "20260911120000_AddAccountSessionLifecycle",
            "20260915004541_CorrectAnnualComplianceAndBillingPolicy");

        Assert.Contains("ADD [TargetEffectiveDate] date NULL", script);
        Assert.Contains("ALTER COLUMN [TargetEffectiveDate] date NOT NULL", script);
        Assert.Contains("THROW 50000", script);
        Assert.Contains("CREATE TABLE [BillingComplianceRecoveryDecisions]", script);
        Assert.Contains("CREATE TABLE [ReleaseObligations]", script);
        Assert.Contains("CREATE TABLE [SignatureComplianceProjections]", script);
        Assert.DoesNotContain("UPDATE [DocumentTemplates]", script, StringComparison.OrdinalIgnoreCase);
    }
}
