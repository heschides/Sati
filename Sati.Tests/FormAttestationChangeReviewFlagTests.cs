using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class FormAttestationChangeReviewFlagTests
{
    [Fact]
    public void FactoryFreezesDatesAudiencesReasonAndBillingHold()
    {
        var due = new DateTime(2026, 9, 3);
        var revised = due.AddDays(1);
        var impact = FormAttestationImpactRules.Evaluate(
            NoteWorkflow.Approved, hasReachedBilling: true,
            due.AddHours(10), due, revised, due);
        var recorded = new DateTime(2026, 9, 21, 16, 0, 0, DateTimeKind.Utc);

        var flag = FormAttestationChangeReviewFlag.Create(
            3, 12, 25, 41, 88, due.AddHours(10), due,
            due, revised, "  Corrected work date  ", impact, recorded);

        Assert.NotEqual(Guid.Empty, flag.FlagId);
        Assert.Equal(3, flag.AgencyId);
        Assert.Equal(12, flag.PersonId);
        Assert.Equal(25, flag.NoteId);
        Assert.Equal(41, flag.FormId);
        Assert.Equal(88, flag.ClaimLineId);
        Assert.Equal(due, flag.NoteActivityDate);
        Assert.Equal(due, flag.PreviousCompletedOn);
        Assert.Equal(revised, flag.RevisedCompletedOn);
        Assert.Equal("Corrected work date", flag.Reason);
        Assert.True(flag.RequiresSupervisorAttention);
        Assert.True(flag.RequiresBillingAttention);
        Assert.True(flag.MustHoldBilling);
        Assert.Equal(recorded, flag.CreatedAtUtc);
    }

    [Fact]
    public void FactoryRequiresAReasonAndReviewAudience()
    {
        var due = new DateTime(2026, 9, 3);
        var changedDraft = FormAttestationImpactRules.Evaluate(
            NoteWorkflow.Pending, false, due, due, due.AddDays(1), due);
        var changedLogged = FormAttestationImpactRules.Evaluate(
            NoteWorkflow.Logged, false, due, due, due.AddDays(1), due);
        var recorded = new DateTime(2026, 9, 21, 16, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentException>(() => Create(changedDraft, "Correction", recorded));
        Assert.Throws<ArgumentException>(() => Create(changedLogged, " ", recorded));
        Assert.Throws<ArgumentException>(() => Create(changedLogged, "Correction", due));
    }

    [Fact]
    public void BothContextsShareTheSameMappedTableAndLocalWritesAreAppendOnly()
    {
        using var context = new SatiContext(new DbContextOptionsBuilder<SatiContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
        var entity = context.Model.FindEntityType(typeof(FormAttestationChangeReviewFlag));
        Assert.NotNull(entity);
        Assert.Equal("FormAttestationChangeReviewFlags", entity!.GetTableName());
        Assert.Equal(5, entity.GetForeignKeys().Count());

        var due = new DateTime(2026, 9, 3);
        var impact = FormAttestationImpactRules.Evaluate(
            NoteWorkflow.Logged, false, due, due, due.AddDays(1), due);
        var flag = Create(impact, "Corrected", new DateTime(2026, 9, 21, 16, 0, 0, DateTimeKind.Utc));
        context.FormAttestationChangeReviewFlags.Add(flag);
        context.Entry(flag).Property(item => item.Id).CurrentValue = 1;
        context.Entry(flag).State = EntityState.Unchanged;
        context.Entry(flag).State = EntityState.Modified;
        var error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("append-only", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static FormAttestationChangeReviewFlag Create(
        FormAttestationImpact impact, string reason, DateTime recorded) =>
        FormAttestationChangeReviewFlag.Create(
            3, 12, 25, 41, null,
            new DateTime(2026, 9, 3), new DateTime(2026, 9, 3),
            new DateTime(2026, 9, 3), new DateTime(2026, 9, 4),
            reason, impact, recorded);
}
