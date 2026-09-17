using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Which calendar days the documented daily average counts.
/// <para>
/// Two defects shaped these rules. The average used to divide by every day carrying a pending
/// note, including days still in the future, so a visit scheduled for next week quietly lowered
/// it. And a day documented one note at a time — a 15-minute review written up on Monday while
/// Monday's visits wait for Friday — entered the divisor at one unit and dragged the average
/// down until the rest of that day was written.
/// </para>
/// </summary>
public sealed class ProductivityDayTests
{
    private static readonly DateTime Today = new(2026, 9, 17);
    private const int Window = 7;

    [Fact]
    public void AFutureDayIsNeverInTheAverage()
    {
        var tomorrow = Today.AddDays(1);
        var notes = new ProductivityNoteFact[]
        {
            new(Today.AddDays(-1), "Logged", 60),
            new(tomorrow, "Pending", 60)
        };

        Assert.Equal([Today.AddDays(-1)], ProductivityForecast.DailyAverageDays(notes, Today, Window));
        Assert.Equal(
            ProductivityDayKind.NotCounted,
            ProductivityForecast.ClassifyDay(tomorrow, notes, Today, Window, caseManagerChoice: null));
    }

    [Fact]
    public void ADayStillCarryingScheduledWorkWaitsUntilItIsDocumented()
    {
        // Monday: the 90-day review has been written up, the day's visits have not.
        var monday = Today.AddDays(-3);
        var notes = new ProductivityNoteFact[]
        {
            new(monday, "Logged", 15),
            new(monday, "Scheduled", 60)
        };

        Assert.Equal(
            ProductivityDayKind.OpenUntilDocumented,
            ProductivityForecast.ClassifyDay(monday, notes, Today, Window, caseManagerChoice: null));
        Assert.Empty(ProductivityForecast.DailyAverageDays(notes, Today, Window));

        // Once the visit is written up, the day counts with everything on it.
        var documented = new ProductivityNoteFact[]
        {
            new(monday, "Logged", 15),
            new(monday, "Logged", 60)
        };
        Assert.Equal(
            ProductivityDayKind.CountedWithSecuredUnits,
            ProductivityForecast.ClassifyDay(monday, documented, Today, Window, caseManagerChoice: null));
        Assert.Equal(5m, ProductivityForecast.DailyAverageUnits(documented, Today, Window));
    }

    [Fact]
    public void TheCaseManagerCanCountOrHoldADayWhileItIsStillOpen()
    {
        var monday = Today.AddDays(-3);
        var open = new ProductivityNoteFact[] { new(monday, "Logged", 15), new(monday, "Scheduled", 60) };
        var finished = new ProductivityNoteFact[] { new(monday, "Logged", 15) };

        // Counted early by choice, though work is still scheduled.
        Assert.Equal(
            ProductivityDayKind.CountedWithSecuredUnits,
            ProductivityForecast.ClassifyDay(monday, open, Today, Window, caseManagerChoice: true));
        // Held back by choice, though Sati would have counted it.
        Assert.Equal(
            ProductivityDayKind.OpenUntilDocumented,
            ProductivityForecast.ClassifyDay(monday, finished, Today, Window, caseManagerChoice: false));
        Assert.True(ProductivityForecast.CanChooseDailyAverageDay(monday, finished, Today, Window));
    }

    [Fact]
    public void ASettledDayCountsWhateverWasChosenOrLeftScheduled()
    {
        // Nine days back, so its documentation window has closed.
        var settled = Today.AddDays(-9);
        var notes = new ProductivityNoteFact[]
        {
            new(settled, "Logged", 60),
            new(settled, "Scheduled", 60)
        };

        Assert.Equal(
            ProductivityDayKind.CountedWithSecuredUnits,
            ProductivityForecast.ClassifyDay(settled, notes, Today, Window, caseManagerChoice: false));
        // And the case manager can no longer hold it out.
        Assert.False(ProductivityForecast.CanChooseDailyAverageDay(settled, notes, Today, Window));
    }

    [Fact]
    public void TodayCountsOnceItCarriesDocumentedWorkAndNothingScheduled()
    {
        Assert.Equal(
            ProductivityDayKind.NotCounted,
            ProductivityForecast.ClassifyDay(Today, [new(Today, "Cancelled", 60)], Today, Window, null));
        Assert.Equal(
            ProductivityDayKind.NotCounted,
            ProductivityForecast.ClassifyDay(Today, [new(Today, "Scheduled", 60)], Today, Window, null));
        Assert.Equal(
            ProductivityDayKind.CountedWithoutSecuredUnits,
            ProductivityForecast.ClassifyDay(Today, [new(Today, "Pending", 60)], Today, Window, null));
        Assert.Equal(
            ProductivityDayKind.CountedWithSecuredUnits,
            ProductivityForecast.ClassifyDay(Today, [new(Today, "Logged", 60)], Today, Window, null));
    }

    [Fact]
    public void ADayOutsideThisMonthIsNotCounted()
    {
        var lastMonth = new DateTime(2026, 8, 31);

        Assert.Equal(
            ProductivityDayKind.NotCounted,
            ProductivityForecast.ClassifyDay(lastMonth, [new(lastMonth, "Logged", 60)], Today, Window, null));
    }

    [Fact]
    public void AnOpenDayNeitherRaisesNorLowersTheAverage()
    {
        // Three finished days at 8 units, and a Monday documented one review deep with its
        // visits still scheduled. The average stays 8 until Monday is finished.
        var monday = Today.AddDays(-3);
        var notes = new List<ProductivityNoteFact>
        {
            new(new DateTime(2026, 9, 9), "Logged", 120),
            new(new DateTime(2026, 9, 10), "Logged", 120),
            new(new DateTime(2026, 9, 11), "Logged", 120),
            new(monday, "Logged", 15),
            new(monday, "Scheduled", 60)
        };

        Assert.Equal(8m, ProductivityForecast.DailyAverageUnits(notes, Today, Window));
        Assert.Equal(3, ProductivityForecast.DailyAverageDays(notes, Today, Window).Count);

        // Counting it by hand includes its one unit, which is the case manager's call.
        var choice = new Dictionary<DateTime, bool> { [monday] = true };
        Assert.Equal(6.2m, ProductivityForecast.DailyAverageUnits(notes, Today, Window, choice));
    }

    [Fact]
    public void AStalePendingDayStillCountsWithoutItsUnits()
    {
        // Pending past its window: the day is settled, but nothing on it is recoverable.
        var stale = Today.AddDays(-9);
        var notes = new ProductivityNoteFact[]
        {
            new(new DateTime(2026, 9, 16), "Logged", 60),
            new(stale, "Pending", 60)
        };

        Assert.Equal(2, ProductivityForecast.DailyAverageDays(notes, Today, Window).Count);
        Assert.Equal(2m, ProductivityForecast.DailyAverageUnits(notes, Today, Window));
    }

    [Fact]
    public void TheAverageIsZeroBeforeTheMonthHasAnyCountedDay()
    {
        var notes = new ProductivityNoteFact[] { new(Today.AddDays(2), "Pending", 60) };

        Assert.Equal(0m, ProductivityForecast.DailyAverageUnits(notes, Today, Window));
    }
}
