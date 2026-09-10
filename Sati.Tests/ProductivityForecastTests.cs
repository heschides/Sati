using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class ProductivityForecastTests
{
    private static readonly DateTime Today = new(2026, 9, 8);

    [Fact]
    public void SeparatesSecuredBacklogAndFutureCapacity()
    {
        var result = ProductivityForecast.Calculate(100, 10, 9, Today, 7,
        [
            new(Today.AddDays(-2), "Logged", 30),
            new(Today.AddDays(-3), "Approved", 15),
            new(Today.AddDays(-4), "Pending", 45)
        ]);

        Assert.Equal(3, result.SecuredUnits);
        Assert.Equal(3, result.RecoverableUnits);
        Assert.Equal(10, result.FutureEligibleDays);
        Assert.Equal(9.7m, result.SecuredPace);
        Assert.Equal(9.4m, result.ProjectedPace);
    }

    [Fact]
    public void PendingWorkReachesItsDeadlineSevenCalendarDaysAfterService()
    {
        var result = ProductivityForecast.Calculate(100, 10, 9, Today, 7,
        [
            new(Today.AddDays(-7), "Pending", 60),
            new(Today.AddDays(-8), "Pending", 30)
        ]);

        Assert.Equal(4, result.DueTodayUnits);
        Assert.Equal(4, result.RecoverableUnits);
        Assert.Equal(2, result.ExpiredPendingUnits);
        Assert.Equal(9.6m, result.ProjectedPace);
        Assert.Equal(11.1m, result.PaceIfDueTodayExpires);
    }

    [Fact]
    public void FutureScheduledWorkIsNotClaimedAsPerformedBacklog()
    {
        var result = ProductivityForecast.Calculate(20, 4, 3, Today, 7,
        [
            new(Today.AddDays(1), "Pending", 60),
            new(Today.AddDays(-1), "Scheduled", 60)
        ]);

        Assert.Equal(0, result.RecoverableUnits);
        Assert.Equal(5m, result.ProjectedPace);
    }

    [Fact]
    public void MissingDurationIsVisibleButNeverInventedAsUnits()
    {
        var result = ProductivityForecast.Calculate(20, 4, 3, Today, 7,
        [
            new(Today.AddDays(-1), "Pending", null)
        ]);

        Assert.Equal(1, result.PendingItemsWithoutUnits);
        Assert.Equal(0, result.RecoverableUnits);
    }

    [Fact]
    public void NoFutureDaysProducesNoPaceUnlessTargetIsAlreadyMet()
    {
        var shortfall = ProductivityForecast.Calculate(20, 0, 0, Today, 7, []);
        var met = ProductivityForecast.Calculate(2, 0, 0, Today, 7,
            [new(Today, "Logged", 30)]);

        Assert.Null(shortfall.ProjectedPace);
        Assert.Equal(0, met.ProjectedPace);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LegacyInvalidDocumentationWindowUsesSevenDayPolicy(int legacyValue)
    {
        var result = ProductivityForecast.Calculate(100, 10, 9, Today, legacyValue,
        [
            new(Today.AddDays(-7), "Pending", 60),
            new(Today.AddDays(-8), "Pending", 30)
        ]);

        Assert.Equal(4, result.DueTodayUnits);
        Assert.Equal(2, result.ExpiredPendingUnits);
    }
}
