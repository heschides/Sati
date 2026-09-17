using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Which calendar days the documented daily average counts, and how the calendar tints them.
/// The average used to divide by every day carrying a pending note, including days still in
/// the future, so a scheduled visit next week quietly lowered the reported average.
/// </summary>
public sealed class ProductivityDayTests
{
    private static readonly DateTime Today = new(2026, 9, 17);

    [Fact]
    public void AFutureDayIsNeverInTheAverage()
    {
        var tomorrow = Today.AddDays(1);
        var notes = new ProductivityNoteFact[]
        {
            new(Today.AddDays(-1), "Logged", 60),
            new(tomorrow, "Pending", 60)
        };

        Assert.Equal([Today.AddDays(-1)], ProductivityForecast.DailyAverageDays(notes, Today));
        Assert.Equal(
            ProductivityDayKind.NotCounted,
            ProductivityForecast.ClassifyDay(tomorrow, notes, Today));
    }

    [Fact]
    public void TodayCountsOnceItCarriesAPendingLoggedOrApprovedNote()
    {
        Assert.Equal(
            ProductivityDayKind.NotCounted,
            ProductivityForecast.ClassifyDay(Today, [new(Today, "Cancelled", 60)], Today));
        Assert.Equal(
            ProductivityDayKind.CountedWithoutSecuredUnits,
            ProductivityForecast.ClassifyDay(Today, [new(Today, "Pending", 60)], Today));
        Assert.Equal(
            ProductivityDayKind.CountedWithSecuredUnits,
            ProductivityForecast.ClassifyDay(Today, [new(Today, "Logged", 60)], Today));
    }

    [Fact]
    public void ADayWithBothPendingAndLoggedWorkReadsAsSecured()
    {
        var date = Today.AddDays(-2);

        Assert.Equal(
            ProductivityDayKind.CountedWithSecuredUnits,
            ProductivityForecast.ClassifyDay(
                date, [new(date, "Pending", 30), new(date, "Approved", 30)], Today));
    }

    [Fact]
    public void ADayOutsideThisMonthIsNotCounted()
    {
        var lastMonth = new DateTime(2026, 8, 31);

        Assert.Equal(
            ProductivityDayKind.NotCounted,
            ProductivityForecast.ClassifyDay(lastMonth, [new(lastMonth, "Logged", 60)], Today));
    }

    [Fact]
    public void AnAbandonedDayIsNotCountedAndPendingOnlyDaysStillAre()
    {
        var abandoned = Today.AddDays(-9);
        var pendingOnly = Today.AddDays(-3);
        var notes = new ProductivityNoteFact[]
        {
            new(abandoned, "Abandoned", 60),
            new(pendingOnly, "Pending", 60)
        };

        Assert.Equal(
            ProductivityDayKind.NotCounted,
            ProductivityForecast.ClassifyDay(abandoned, notes, Today));
        Assert.Equal(
            ProductivityDayKind.CountedWithoutSecuredUnits,
            ProductivityForecast.ClassifyDay(pendingOnly, notes, Today));
    }

    [Fact]
    public void TheAverageDividesBySecuredAndRecoverableDaysOnly()
    {
        // Two counted days: 4 secured units and 4 recoverable. The scheduled note next
        // week is neither, and its day must not enlarge the divisor.
        var notes = new ProductivityNoteFact[]
        {
            new(Today.AddDays(-1), "Logged", 60),
            new(Today.AddDays(-2), "Pending", 60),
            new(Today.AddDays(3), "Pending", 60)
        };
        var forecast = ProductivityForecast.Calculate(100, 8, 7, Today, 7, notes);

        Assert.Equal(4m, ProductivityForecast.DailyAverageUnits(forecast, notes, Today));
    }

    [Fact]
    public void TheAverageIsZeroBeforeTheMonthHasAnyCountedDay()
    {
        var notes = new ProductivityNoteFact[] { new(Today.AddDays(2), "Pending", 60) };
        var forecast = ProductivityForecast.Calculate(100, 8, 7, Today, 7, notes);

        Assert.Equal(0m, ProductivityForecast.DailyAverageUnits(forecast, notes, Today));
    }
}
