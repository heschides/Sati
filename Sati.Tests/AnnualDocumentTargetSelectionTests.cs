using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class AnnualDocumentTargetSelectionTests
{
    [Fact]
    public void BeforeEnrollmentTheFirstEffectiveDateIsTheOnlyCurrentTarget()
    {
        var effective = new DateTime(2027, 3, 7);

        Assert.Equal(
            effective,
            AnnualDocumentCycle.CurrentStart(effective, new DateTime(2026, 12, 1)));
        Assert.Equal(
            effective,
            AnnualDocumentCycle.SuggestedStart(
                effective, new DateTime(2026, 12, 1), openDaysBefore: 90));
    }

    [Fact]
    public void SuggestedTargetChangesToTheNextAnniversaryWhenItsWindowOpens()
    {
        var effective = new DateTime(2025, 3, 7);
        var nextTarget = new DateTime(2027, 3, 7);
        var opensOn = nextTarget.AddDays(-90);

        Assert.Equal(
            new DateTime(2026, 3, 7),
            AnnualDocumentCycle.SuggestedStart(
                effective, opensOn.AddDays(-1), openDaysBefore: 90));
        Assert.Equal(
            nextTarget,
            AnnualDocumentCycle.SuggestedStart(
                effective, opensOn, openDaysBefore: 90));
    }

    [Fact]
    public void DueOffsetParticipatesInTheAvailabilityDate()
    {
        var effective = new DateTime(2025, 3, 7);
        var nextTarget = new DateTime(2027, 3, 7);
        var opensOn = nextTarget.AddDays(-30).AddDays(-60);

        Assert.Equal(
            nextTarget,
            AnnualDocumentCycle.SuggestedStart(
                effective,
                opensOn,
                openDaysBefore: 60,
                dueDaysBeforeEffective: 30));
    }
}
