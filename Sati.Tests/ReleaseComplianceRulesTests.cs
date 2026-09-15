using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class ReleaseComplianceRulesTests
{
    private static readonly DateTime Target = new(2027, 3, 7);
    private static readonly DateTime AsOf = new(2027, 1, 15);

    [Theory]
    [InlineData(ReleaseObligationCategory.Agency, true)]
    [InlineData(ReleaseObligationCategory.Medical, true)]
    [InlineData(ReleaseObligationCategory.Dhhs, false)]
    public void EmptyExactTargetUsesCategoryRequirement(
        ReleaseObligationCategory category,
        bool expected)
    {
        Assert.Equal(expected, ReleaseComplianceRules.IsCategoryCompliant(
            category, Target, AsOf, []));
    }

    [Fact]
    public void EveryRequiredRecipientNeedsItsOwnAttestationByTheAsOfDate()
    {
        var first = Fact("medical:1", ReleaseObligationCategory.Medical,
        [
            Attestation("medical:1", AsOf)
        ]);
        var second = Fact("medical:2", ReleaseObligationCategory.Medical,
        [
            Attestation("medical:2", AsOf.AddDays(1))
        ]);

        Assert.False(ReleaseComplianceRules.IsCategoryCompliant(
            ReleaseObligationCategory.Medical, Target, AsOf, [first, second]));
        Assert.True(ReleaseComplianceRules.IsCategoryCompliant(
            ReleaseObligationCategory.Medical, Target, AsOf.AddDays(1), [first, second]));
    }

    [Fact]
    public void AttestationForAnotherRecipientDoesNotSatisfyTheRequiredRow()
    {
        var obligation = Fact("agency:1", ReleaseObligationCategory.Agency,
        [
            Attestation("agency:2", AsOf)
        ]);

        Assert.False(ReleaseComplianceRules.IsCategoryCompliant(
            ReleaseObligationCategory.Agency, Target, AsOf, [obligation]));
    }

    [Fact]
    public void EvaluationUsesOnlyTheExactTargetAndRowsActiveAsOfTheDate()
    {
        var wrongTarget = Fact(
            "medical:prior", ReleaseObligationCategory.Medical, [], Target.AddYears(-1));
        var retiresTomorrow = Fact(
            "medical:active", ReleaseObligationCategory.Medical, [], retiredOn: AsOf.AddDays(1));
        var retiredToday = Fact(
            "medical:retired", ReleaseObligationCategory.Medical, [], retiredOn: AsOf);

        Assert.False(ReleaseComplianceRules.IsCategoryCompliant(
            ReleaseObligationCategory.Medical,
            Target,
            AsOf,
            [wrongTarget, retiresTomorrow, retiredToday]));
        Assert.True(ReleaseComplianceRules.IsCategoryCompliant(
            ReleaseObligationCategory.Medical,
            Target,
            AsOf.AddDays(1),
            [wrongTarget, retiresTomorrow, retiredToday]));
    }

    [Fact]
    public void FutureAppliesFromDoesNotHideAnExistingPreparationWindowObligation()
    {
        var obligation = Fact(
            "agency:future-service",
            ReleaseObligationCategory.Agency,
            [],
            appliesFromOn: AsOf.AddDays(30));

        Assert.False(ReleaseComplianceRules.IsCategoryCompliant(
            ReleaseObligationCategory.Agency, Target, AsOf, [obligation]));
    }

    private static ReleaseComplianceFact Fact(
        string key,
        ReleaseObligationCategory category,
        IReadOnlyCollection<ReleaseAttestationFact> attestations,
        DateTime? target = null,
        DateTime? retiredOn = null,
        DateTime? appliesFromOn = null) =>
        new(
            key,
            category,
            Target,
            appliesFromOn ?? Target,
            retiredOn,
            attestations,
            TargetEffectiveDate: target ?? Target);

    private static ReleaseAttestationFact Attestation(string key, DateTime completedOn) =>
        new(
            key,
            completedOn,
            new DateTime(2027, 1, 20, 12, 0, 0, DateTimeKind.Utc));
}
