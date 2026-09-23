using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class PendingAttestationExactLinkTests
{
    [Fact]
    public void PcpNoteOnEffectiveDatePrefersThePlanDueThatDay()
    {
        var target = new DateTime(2026, 9, 21);
        var next = target.AddYears(1);
        var forms = new[]
        {
            new FormFact(10, 4, "PCP", target, null, target),
            new FormFact(11, 4, "PCP", next, null, next)
        };
        var note = new NoteFact(50, 4, "PCP", target, "Logged", FormId: null);

        var pending = Assert.Single(FormAttestationRules.PendingAttestations(
            [note], forms, target.AddYears(-1), target.AddDays(1)));

        Assert.Equal(10, pending.FormId);
        Assert.True(pending.IsLegacyUnlinked);
    }

    [Fact]
    public void LinkedNoteNeverFallsBackToAnotherCycleForm()
    {
        var effective = new DateTime(2025, 1, 1);
        var workDate = new DateTime(2025, 3, 1);
        var forms = new[]
        {
            new FormFact(10, 4, "Q1R", new DateTime(2025, 4, 1), null, effective),
            new FormFact(11, 4, "Q1R", new DateTime(2026, 4, 1), null,
                effective.AddYears(1))
        };
        var wrongCycleLink = new NoteFact(50, 4, "Q1R", workDate, "Logged", FormId: 11);

        Assert.Empty(FormAttestationRules.PendingAttestations(
            [wrongCycleLink], forms, effective, new DateTime(2026, 9, 1)));
    }

    [Fact]
    public void LegacyUnlinkedNoteIsMarkedAsInferredEvidence()
    {
        var effective = new DateTime(2025, 1, 1);
        var form = new FormFact(10, 4, "Q1R", new DateTime(2025, 4, 1), null,
            effective);
        var linked = new NoteFact(50, 4, "Q1R", new DateTime(2025, 3, 1),
            "Logged", FormId: 10);
        var legacy = linked with { NoteId = 51, FormId = null };

        var exact = Assert.Single(FormAttestationRules.PendingAttestations(
            [linked], [form], effective, new DateTime(2026, 9, 1)));
        Assert.Equal(10, exact.FormId);
        Assert.False(exact.IsLegacyUnlinked);

        var inferred = Assert.Single(FormAttestationRules.PendingAttestations(
            [legacy], [form], effective, new DateTime(2026, 9, 1)));
        Assert.True(inferred.IsLegacyUnlinked);
    }
}
