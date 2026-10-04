using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class ConsumerProviderOrderTests
{
    private static ConsumerProviderDto Fact(int id, bool primary = false, DateTime? end = null) =>
        new(id, 40, id + 100, "Role", primary, null, end, false, 0);

    [Fact]
    public void VersionIgnoresEnumerationOrderAndDetectsEveryRelationshipChange()
    {
        var a = Fact(1);
        var b = Fact(2, end: new DateTime(2026, 8, 28));
        var version = ConsumerProviderOrder.Version([a, b]);
        Assert.Equal(version, ConsumerProviderOrder.Version([b, a]));
        foreach (var changed in new[] { a with { ProviderId = 333 }, a with { SortOrder = 1 }, a with { Role = "Changed" },
            a with { IsPrimaryCare = true }, a with { EndDate = DateTime.Today }, a with { HasActiveRelease = true },
            a with { StartDate = DateTime.Today }, a with { AssignmentKnownOn = DateTime.Today } })
            Assert.NotEqual(version, ConsumerProviderOrder.Version([changed, b]));
        Assert.NotEqual(version, ConsumerProviderOrder.Version([a]));
    }

    [Fact]
    public void OnlyCompleteCurrentPermutationWithPrimaryFirstIsAllowed()
    {
        var facts = new[] { Fact(1, primary: true), Fact(2), Fact(3), Fact(4, end: DateTime.Today) };
        Assert.Null(ConsumerProviderOrder.Validate(new("", [1, 3, 2]), facts));
        foreach (var ids in new[] { new[] { 2, 1, 3 }, new[] { 1, 2 }, new[] { 1, 2, 2 }, new[] { 1, 2, 4 }, new[] { 1, 2, 99 } })
            Assert.NotNull(ConsumerProviderOrder.Validate(new("", ids), facts));
        Assert.NotNull(ConsumerProviderOrder.Validate(new("", null!), facts));
        Assert.Null(ConsumerProviderOrder.Validate(new("", []), []));
    }
}
