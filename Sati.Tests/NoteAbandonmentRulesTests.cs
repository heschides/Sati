using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class NoteAbandonmentRulesTests
{
    [Theory]
    [InlineData(NoteWorkflow.Pending, -8, true)]
    [InlineData(NoteWorkflow.Pending, -7, false)]
    [InlineData(NoteWorkflow.Pending, 0, false)]
    [InlineData(NoteWorkflow.Scheduled, -8, false)]
    [InlineData(NoteWorkflow.Logged, -8, false)]
    [InlineData(NoteWorkflow.Returned, -8, false)]
    [InlineData(NoteWorkflow.Approved, -8, false)]
    public void OnlyPendingOutsideTheWindowMayBeAbandoned(int status, int daysFromToday,
        bool expected)
    {
        var today = new DateTime(2026, 10, 3);
        Assert.Equal(expected, NoteAbandonmentRules.IsEligible(status,
            today.AddDays(daysFromToday), today, 7));
    }
}
