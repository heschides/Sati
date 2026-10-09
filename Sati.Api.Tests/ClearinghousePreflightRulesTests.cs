using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

public sealed class ClearinghousePreflightRulesTests
{
    [Fact]
    public void FourDueTimesThenAFifthFailureHoldSurviveRecomputation()
    {
        var account = Guid.NewGuid(); var cycle = Guid.NewGuid();
        var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        ClearinghousePreflightState? state = null;
        foreach (var minutes in new[] { 1, 5, 15, 60 })
        {
            var prior = state;
            state = ClearinghousePreflightRules.MissingKey(account, prior, cycle, now);
            Assert.Equal(state, ClearinghousePreflightRules.MissingKey(account, prior, cycle, now));
            Assert.Equal(ClearinghousePreflightDisposition.Deferred, state.Disposition);
            Assert.InRange(state.NextEligibleAtUtc!.Value - now,
                TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes * 1.1));
            Assert.False(ClearinghousePreflightRules.IsEligible(state, state.NextEligibleAtUtc.Value.AddTicks(-1)));
            Assert.True(ClearinghousePreflightRules.IsEligible(state, state.NextEligibleAtUtc.Value));
            Assert.Throws<InvalidOperationException>(() => ClearinghousePreflightRules.MissingKey(account, state, cycle, now));
            now = state.NextEligibleAtUtc.Value;
        }
        state = ClearinghousePreflightRules.MissingKey(account, state, Guid.NewGuid(), now);
        Assert.Equal(ClearinghousePreflightDisposition.Held, state.Disposition);
        Assert.Equal(5, state.FailureCount); Assert.Null(state.NextEligibleAtUtc);
        Assert.Equal(cycle, state.RecoveryCycleId);
        Assert.False(ClearinghousePreflightRules.IsEligible(state, now.AddYears(10)));
        Assert.Throws<InvalidOperationException>(() => ClearinghousePreflightRules.MissingKey(account, state, cycle, now.AddYears(10)));
        Assert.True(ClearinghousePreflightRules.IsEligible(ClearinghousePreflightState.Ready, now));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void DamagedSchedulingFactsCannotBecomeEligibility(int damage)
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        var state = ClearinghousePreflightRules.MissingKey(Guid.NewGuid(), null, Guid.NewGuid(), now);
        state = damage switch
        {
            0 => state with { Disposition = (ClearinghousePreflightDisposition)99 },
            1 => state with { FailureCount = 5 },
            2 => state with { NextEligibleAtUtc = null },
            3 => state with { RecoveryCycleId = Guid.Empty },
            _ => state with { SafeFailureCode = "untrusted-provider-text" }
        };
        Assert.Throws<InvalidOperationException>(() => ClearinghousePreflightRules.IsEligible(state, now.AddYears(1)));
    }
}
