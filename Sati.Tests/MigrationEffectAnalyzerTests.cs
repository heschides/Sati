using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Sati.Data;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The verdict that decides whether Sati starts.
///
/// A PartiallyPresent finding stops startup on purpose: neither applying nor recording
/// the migration is correct and a person has to choose. That makes a FALSE partial as
/// damaging as a missed one - it refuses to open a caseload over a problem that does
/// not exist, and 1.2.36 did exactly that on every Local machine.
///
/// The analyzer reads SQL Server catalog views, so these exercise the classification
/// rules against a hand-built schema rather than a live database.
/// </summary>
public sealed class MigrationEffectAnalyzerTests
{
    private const int Unbounded = -1;

    [Fact]
    public void NarrowingAnUnboundedColumnReadsAsNotAppliedRatherThanPartiallyApplied()
    {
        // Exactly 20260901150802_AddUniqueFormPersonTypeDueDateIndex against a database
        // that has had none of it: Forms.Type is still nvarchar(max) and the unique
        // index does not exist.
        //
        // Type is NOT NULL before and after, so judged on nullability alone the alter
        // looked satisfied while the index looked missing - one present, one missing,
        // PartiallyPresent, startup refused. Nothing had actually been applied.
        var schema = MigrationEffectAnalyzer.LiveSchema.ForTests(
            new() { ["Forms"] = new() { ["Type"] = (false, Unbounded) } });

        var finding = MigrationEffectAnalyzer.Classify(
            "20260901150802_AddUniqueFormPersonTypeDueDateIndex",
            [
                new AlterColumnOperation
                {
                    Table = "Forms", Name = "Type", ClrType = typeof(string),
                    MaxLength = 40, IsNullable = false
                },
                new CreateIndexOperation
                {
                    Table = "Forms", Name = "IX_Forms_PersonId_Type_DueDate",
                    Columns = ["PersonId", "Type", "DueDate"], IsUnique = true
                }
            ],
            schema);

        Assert.Equal(MigrationEffectState.NotApplied, finding.State);
        Assert.Empty(finding.PresentEffects);
        Assert.Equal(2, finding.MissingEffects.Count);
    }

    [Fact]
    public void ADefaultOnlyAlterIsNotEvidenceThatAMigrationRan()
    {
        // 20260915004541_CorrectAnnualComplianceAndBillingPolicy against a database that
        // has had none of it. The correction alters Settings.BillingComplianceRequirements
        // only to change its default from 31 to 7: same type, same nullability, no bound.
        // That leaves nothing to observe, so counting it as applied made an untouched
        // database read PartiallyPresent and refused to start on a real workstation.
        var schema = MigrationEffectAnalyzer.LiveSchema.ForTests(
            new() { ["Settings"] = new() { ["BillingComplianceRequirements"] = (false, Unbounded) } });

        var finding = MigrationEffectAnalyzer.Classify(
            "20260915004541_CorrectAnnualComplianceAndBillingPolicy",
            [
                new AlterColumnOperation
                {
                    Table = "Settings", Name = "BillingComplianceRequirements",
                    ClrType = typeof(int), IsNullable = false, DefaultValue = 7,
                    OldColumn = new AddColumnOperation
                    {
                        Table = "Settings", Name = "BillingComplianceRequirements",
                        ClrType = typeof(int), IsNullable = false, DefaultValue = 31
                    }
                },
                new CreateTableOperation { Name = "ReleaseObligations" }
            ],
            schema);

        Assert.Equal(MigrationEffectState.NotApplied, finding.State);
        Assert.Empty(finding.PresentEffects);
    }

    [Fact]
    public void ANarrowedColumnAndItsIndexReadAsAlreadyPresent()
    {
        // The same migration against a database that has had all of it. This is the
        // verdict that lets the startup path record the migration rather than reapply
        // it and fail with SQL 2705.
        var schema = MigrationEffectAnalyzer.LiveSchema.ForTests(
            new() { ["Forms"] = new() { ["Type"] = (false, 40) } },
            [("Forms", new[] { "PersonId", "Type", "DueDate" })]);

        var finding = MigrationEffectAnalyzer.Classify(
            "20260901150802_AddUniqueFormPersonTypeDueDateIndex",
            [
                new AlterColumnOperation
                {
                    Table = "Forms", Name = "Type", ClrType = typeof(string),
                    MaxLength = 40, IsNullable = false
                },
                new CreateIndexOperation
                {
                    Table = "Forms", Name = "IX_Forms_PersonId_Type_DueDate",
                    Columns = ["PersonId", "Type", "DueDate"], IsUnique = true
                }
            ],
            schema);

        Assert.Equal(MigrationEffectState.AlreadyPresent, finding.State);
        Assert.Empty(finding.MissingEffects);
    }

    [Fact]
    public void AnIndexRebuiltInPlaceIsNotEvidenceThatAMigrationRan()
    {
        // CorrectAnnualComplianceAndBillingPolicy drops IX_DocumentArtifacts_OneLivePerCycle
        // and recreates it on the same three columns with a narrower filter. Inspected by
        // table and column the old index satisfies the create, so on the production
        // workstation - which had none of the release - that one create read as present
        // against every table, column and other index reading as missing. The verdict was
        // PartiallyPresent and 1.3.12 refused to open the caseload.
        //
        // A drop and a create of one name in one migration look the same before and
        // after. That is an absence of evidence, not evidence of application.
        var schema = MigrationEffectAnalyzer.LiveSchema.ForTests(
            new() { ["DocumentArtifacts"] = new() { ["PersonId"] = (false, null) } },
            [("DocumentArtifacts", new[] { "PersonId", "Kind", "CycleStart" })]);

        var finding = MigrationEffectAnalyzer.Classify(
            "20260915004541_CorrectAnnualComplianceAndBillingPolicy",
            [
                new DropIndexOperation
                {
                    Table = "DocumentArtifacts", Name = "IX_DocumentArtifacts_OneLivePerCycle"
                },
                new CreateIndexOperation
                {
                    Table = "DocumentArtifacts", Name = "IX_DocumentArtifacts_OneLivePerCycle",
                    Columns = ["PersonId", "Kind", "CycleStart"], IsUnique = true,
                    Filter = "[ReleaseObligationId] IS NULL AND [SupersededByArtifactId] IS NULL"
                },
                new CreateTableOperation { Name = "ReleaseObligations" }
            ],
            schema);

        Assert.Equal(MigrationEffectState.NotApplied, finding.State);
        Assert.Empty(finding.PresentEffects);
        Assert.Contains(
            finding.UnverifiableSteps,
            step => step.Contains("IX_DocumentArtifacts_OneLivePerCycle"));
    }

    [Fact]
    public void AnAbsentIndexStillCountsEvenWhenTheMigrationRebuildsIt()
    {
        // Only presence is uninformative. AllowSupersedingBillingComplianceRecovery
        // rebuilds one index and does nothing else structural, so excluding it either
        // way left a database without the table reading as Indeterminate instead of
        // NotApplied. An index that is not there was never rebuilt.
        var schema = MigrationEffectAnalyzer.LiveSchema.ForTests(new());

        var finding = MigrationEffectAnalyzer.Classify(
            "20260915153000_AllowSupersedingBillingComplianceRecovery",
            [
                new DropIndexOperation
                {
                    Table = "BillingComplianceRecoveryNotes",
                    Name = "IX_BillingComplianceRecoveryNotes_NoteId"
                },
                new CreateIndexOperation
                {
                    Table = "BillingComplianceRecoveryNotes",
                    Name = "IX_BillingComplianceRecoveryNotes_NoteId",
                    Columns = ["NoteId"]
                }
            ],
            schema);

        Assert.Equal(MigrationEffectState.NotApplied, finding.State);
    }

    [Fact]
    public void AnIndexCreatedWithoutBeingDroppedStillCounts()
    {
        // The exclusion above is scoped to a name this migration drops. An ordinary
        // create still reads as present when the index is there, or the analyzer would
        // stop recognising a migration that genuinely ran.
        var schema = MigrationEffectAnalyzer.LiveSchema.ForTests(
            new() { ["DocumentArtifacts"] = new() { ["PersonId"] = (false, null) } },
            [("DocumentArtifacts", new[] { "PersonId", "Kind", "CycleStart" })]);

        var finding = MigrationEffectAnalyzer.Classify(
            "test",
            [
                new CreateIndexOperation
                {
                    Table = "DocumentArtifacts", Name = "IX_DocumentArtifacts_OneLivePerCycle",
                    Columns = ["PersonId", "Kind", "CycleStart"], IsUnique = true
                }
            ],
            schema);

        Assert.Equal(MigrationEffectState.AlreadyPresent, finding.State);
    }

    [Fact]
    public void AColumnWiderThanTheMigrationDeclaredStillCounts()
    {
        // Benign drift. The bound was applied; the column is merely roomier than this
        // migration asked for. Failing here would stop startup over something that
        // affects nothing, which is the mistake this whole area is prone to.
        var schema = MigrationEffectAnalyzer.LiveSchema.ForTests(
            new() { ["Forms"] = new() { ["Type"] = (false, 100) } });

        var finding = MigrationEffectAnalyzer.Classify(
            "test",
            [
                new AlterColumnOperation
                {
                    Table = "Forms", Name = "Type", ClrType = typeof(string),
                    MaxLength = 40, IsNullable = false
                }
            ],
            schema);

        Assert.Equal(MigrationEffectState.AlreadyPresent, finding.State);
    }

    [Fact]
    public void NullabilityRemainsTheSignalWhenNoLengthIsDeclared()
    {
        // The original purpose of this check, unchanged: the chain makes columns NOT
        // NULL after a backfill, so nullability is how a completed backfill is
        // recognised. A still-nullable column means the backfill has not run.
        var schema = MigrationEffectAnalyzer.LiveSchema.ForTests(
            new() { ["Settings"] = new() { ["AgencyId"] = (true, null) } });

        var finding = MigrationEffectAnalyzer.Classify(
            "test",
            [
                new AlterColumnOperation
                {
                    Table = "Settings", Name = "AgencyId", ClrType = typeof(int),
                    IsNullable = false
                }
            ],
            schema);

        Assert.Equal(MigrationEffectState.NotApplied, finding.State);
    }

    [Fact]
    public void AGenuinelyMixedMigrationStillStopsStartup()
    {
        // The state the refusal exists for, pinned so the fix above does not quietly
        // widen into "assume it is fine". The column was narrowed but the index beside
        // it never appeared, so which half is missing genuinely needs a person.
        var schema = MigrationEffectAnalyzer.LiveSchema.ForTests(
            new() { ["Forms"] = new() { ["Type"] = (false, 40) } });

        var finding = MigrationEffectAnalyzer.Classify(
            "test",
            [
                new AlterColumnOperation
                {
                    Table = "Forms", Name = "Type", ClrType = typeof(string),
                    MaxLength = 40, IsNullable = false
                },
                new CreateIndexOperation
                {
                    Table = "Forms", Name = "IX_Forms_PersonId_Type_DueDate",
                    Columns = ["PersonId", "Type", "DueDate"], IsUnique = true
                }
            ],
            schema);

        Assert.Equal(MigrationEffectState.PartiallyPresent, finding.State);
    }
}
