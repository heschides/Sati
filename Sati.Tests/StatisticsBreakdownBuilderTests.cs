using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class StatisticsBreakdownBuilderTests
{
    private static readonly DateTime Today = new(2026, 2, 1);

    [Theory]
    [InlineData(StatisticsPeriod.Week, 2025, 12, 30, 2026, 1, 4, 2026, 1, 5, 2026, 1, 7)]
    [InlineData(StatisticsPeriod.Month, 2025, 12, 30, 2025, 12, 31, 2026, 1, 1, 2026, 1, 7)]
    [InlineData(StatisticsPeriod.Quarter, 2025, 12, 30, 2025, 12, 31, 2026, 1, 1, 2026, 1, 7)]
    [InlineData(StatisticsPeriod.Year, 2025, 12, 30, 2025, 12, 31, 2026, 1, 1, 2026, 1, 7)]
    public void PeriodsAreClippedToTheWindowAndUnitsStayOnTheirServiceDates(
        StatisticsPeriod period,
        int firstStartYear, int firstStartMonth, int firstStartDay,
        int firstEndYear, int firstEndMonth, int firstEndDay,
        int secondStartYear, int secondStartMonth, int secondStartDay,
        int secondEndYear, int secondEndMonth, int secondEndDay)
    {
        var report = StatisticsBreakdownBuilder.Build(
            new DateTime(2025, 12, 30), new DateTime(2026, 1, 7),
            period, Today, 7,
            [new StatisticsClientFact(101, "Consumer One")],
            [
                Note(101, new DateTime(2025, 12, 31), "Logged", 15),
                Note(101, new DateTime(2026, 1, 5), "Approved", 30),
                Note(101, new DateTime(2025, 12, 29), "Logged", 60),
                Note(101, new DateTime(2026, 1, 8), "Logged", 60)
            ],
            []);

        Assert.Collection(report.Periods,
            first =>
            {
                Assert.Equal(new DateTime(firstStartYear, firstStartMonth, firstStartDay), first.Start);
                Assert.Equal(new DateTime(firstEndYear, firstEndMonth, firstEndDay), first.End);
                Assert.Equal(1, first.Metrics.DocumentedUnits);
            },
            second =>
            {
                Assert.Equal(new DateTime(secondStartYear, secondStartMonth, secondStartDay), second.Start);
                Assert.Equal(new DateTime(secondEndYear, secondEndMonth, secondEndDay), second.End);
                Assert.Equal(2, second.Metrics.DocumentedUnits);
            });
        Assert.Equal(3, report.Totals.DocumentedUnits);
    }

    [Fact]
    public void StatusesActivitiesAndSubmittedClaimsRemainSeparateMeasures()
    {
        var serviceDate = new DateTime(2026, 1, 10);
        var mixed = (int)(NoteActivity.Form | NoteActivity.Visit);
        var report = StatisticsBreakdownBuilder.Build(
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 31),
            StatisticsPeriod.Month, Today, 7,
            [new StatisticsClientFact(101, "Consumer One")],
            [
                Note(101, serviceDate, "Logged", 16, mixed, "Form"),
                Note(101, serviceDate, "Approved", 15),
                Note(101, new DateTime(2026, 1, 5), "Pending", 60),
                Note(101, new DateTime(2026, 1, 5), "Pending", 30),
                Note(101, new DateTime(2026, 1, 11), "ComplianceBlocked", 45),
                Note(101, serviceDate, "Scheduled", 60),
                Note(101, serviceDate, "Cancelled", 60)
            ],
            [
                new StatisticsClaimFact(101, serviceDate, 1.07m, mixed, "Form", true),
                new StatisticsClaimFact(101, serviceDate, null, mixed, "Form", true),
                new StatisticsClaimFact(101, serviceDate, 4m, mixed, "Form", false)
            ]);

        var metrics = report.Totals;
        Assert.Equal(3, metrics.DocumentedUnits);
        Assert.Equal(6, metrics.PendingUnits);
        Assert.Equal(3, metrics.ComplianceBlockedUnits);
        Assert.Equal(2, metrics.FormDocumentedUnits);
        Assert.Equal(2, metrics.VisitDocumentedUnits);
        Assert.Equal(2, metrics.DocumentedNoteCount);
        Assert.Equal(1, metrics.FormNoteCount);
        Assert.Equal(1, metrics.VisitNoteCount);
        Assert.Equal(1, metrics.DocumentedServiceDays);
        Assert.Equal(1, metrics.ExpiredPendingServiceDays);
        Assert.Equal(2, metrics.SubmittedClaimCount);
        Assert.Equal(1, metrics.SubmittedClaimsWithoutUnits);
        Assert.Equal(1.07m, metrics.SubmittedClaimUnits);
        Assert.Equal(1.07m, metrics.SubmittedFormUnits);
        Assert.Equal(1.07m, metrics.SubmittedVisitUnits);
        Assert.Equal(1.07m, metrics.LockedClaimUnits);
        Assert.Equal(2, metrics.LockedClaimCount);
        Assert.Equal(1, metrics.LockedClaimsWithoutUnits);
    }

    [Fact]
    public void InternallyLockedClaimsAreVisibleWithoutBeingCalledTransmitted()
    {
        var date = new DateTime(2026, 1, 10);
        var report = StatisticsBreakdownBuilder.Build(
            date, date, StatisticsPeriod.Month, Today, 7,
            [new StatisticsClientFact(101, "Consumer One")],
            [],
            [
                new StatisticsClaimFact(101, date, 1.23m, null, "Visit", false, true),
                new StatisticsClaimFact(101, date, null, null, "Form", false, true),
                new StatisticsClaimFact(101, date, 9.99m, null, "Form", false, false)
            ]);

        Assert.Equal(1.23m, report.Totals.LockedClaimUnits);
        Assert.Equal(2, report.Totals.LockedClaimCount);
        Assert.Equal(1, report.Totals.LockedClaimsWithoutUnits);
        Assert.Equal(0m, report.Totals.SubmittedClaimUnits);
        Assert.Equal(0, report.Totals.SubmittedClaimCount);
    }

    [Fact]
    public void DocumentationWindowClosesAfterTheLastAllowedDay()
    {
        var report = StatisticsBreakdownBuilder.Build(
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 31),
            StatisticsPeriod.Month, Today, 7,
            [new StatisticsClientFact(101, "Consumer One")],
            [
                Note(101, new DateTime(2026, 1, 24), "Pending", 15),
                Note(101, new DateTime(2026, 1, 25), "Pending", 15)
            ],
            []);

        Assert.Equal(2, report.Totals.PendingUnits);
        Assert.Equal(1, report.Totals.ExpiredPendingServiceDays);
        Assert.Equal(0, report.Totals.DocumentedUnits);
    }

    [Fact]
    public void AbandonedDatesRemainVisibleSeparatelyFromExpiredPendingBacklog()
    {
        var report = StatisticsBreakdownBuilder.Build(
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 31),
            StatisticsPeriod.Month, Today, 7,
            [new StatisticsClientFact(101, "Consumer One")],
            [
                Note(101, new DateTime(2026, 1, 5), "Abandoned", 60),
                Note(101, new DateTime(2026, 1, 5), "Abandoned", 15),
                Note(101, new DateTime(2026, 1, 6), "Pending", 15),
                Note(101, new DateTime(2026, 1, 7), "Logged", 15)
            ],
            []);

        Assert.Equal(1, report.Totals.AbandonedServiceDays);
        Assert.Equal(1, report.Totals.ExpiredPendingServiceDays);
        Assert.Equal(1, report.Totals.DocumentedServiceDays);
        Assert.Equal(1, report.Totals.DocumentedUnits);
        Assert.Equal(1, report.Totals.PendingUnits);
    }

    [Fact]
    public void EmptyPeriodsStillAppearForASelectedWindow()
    {
        var report = StatisticsBreakdownBuilder.Build(
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 12),
            StatisticsPeriod.Week, Today, 7,
            [new StatisticsClientFact(101, "Consumer One")], [], []);

        Assert.Collection(report.Periods,
            first => Assert.Equal((new DateTime(2026, 1, 1), new DateTime(2026, 1, 4)),
                (first.Start, first.End)),
            second => Assert.Equal((new DateTime(2026, 1, 5), new DateTime(2026, 1, 11)),
                (second.Start, second.End)),
            third => Assert.Equal((new DateTime(2026, 1, 12), new DateTime(2026, 1, 12)),
                (third.Start, third.End)));
        Assert.All(report.Periods, row => Assert.Equal(0, row.Metrics.DocumentedUnits));
    }

    [Fact]
    public void CaseloadServiceDaysAreDistinctCalendarDaysRatherThanSumOfClientDays()
    {
        var date = new DateTime(2026, 1, 15);
        var report = StatisticsBreakdownBuilder.Build(
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 31),
            StatisticsPeriod.Month, Today, 7,
            [
                new StatisticsClientFact(101, "Consumer One"),
                new StatisticsClientFact(102, "Consumer Two")
            ],
            [Note(101, date, "Logged", 15), Note(102, date, "Approved", 30)],
            []);

        Assert.Equal(3, report.Totals.DocumentedUnits);
        Assert.Equal(1, report.Totals.DocumentedServiceDays);
        Assert.Collection(report.Clients,
            first =>
            {
                Assert.Equal(101, first.PersonId);
                Assert.Equal(1, first.Metrics.DocumentedUnits);
                Assert.Equal(1, first.Metrics.DocumentedServiceDays);
            },
            second =>
            {
                Assert.Equal(102, second.PersonId);
                Assert.Equal(2, second.Metrics.DocumentedUnits);
                Assert.Equal(1, second.Metrics.DocumentedServiceDays);
        });
    }

    [Fact]
    public void DocumentedBillableMarksAndIntentionalNonBillingHaveSeparateOverlappingDays()
    {
        var mixedDate = new DateTime(2026, 1, 10);
        var report = StatisticsBreakdownBuilder.Build(
            new DateTime(2026, 1, 1), new DateTime(2026, 2, 28),
            StatisticsPeriod.Month, Today, 7,
            [
                new StatisticsClientFact(101, "Consumer One"),
                new StatisticsClientFact(102, "Consumer Two")
            ],
            [
                Note(101, mixedDate, "Logged", 16),
                Note(101, mixedDate, "Approved", 15, isUnbilled: true),
                Note(101, new DateTime(2026, 1, 11), "Logged", 15),
                Note(102, mixedDate, "Approved", 15, isUnbilled: true),
                Note(102, new DateTime(2026, 2, 1), "Approved", 30, isUnbilled: true),
                Note(101, new DateTime(2026, 1, 12), "Pending", 60, isUnbilled: true),
                Note(101, new DateTime(2026, 1, 13), "ComplianceBlocked", 60),
                Note(101, new DateTime(2026, 1, 14), "Abandoned", 60, isUnbilled: true)
            ],
            []);

        Assert.Equal(7, report.Totals.DocumentedUnits);
        Assert.Equal(3, report.Totals.BillableMarkedUnits);
        Assert.Equal(4, report.Totals.NonBillableUnits);
        Assert.Equal(3, report.Totals.DocumentedServiceDays);
        Assert.Equal(2, report.Totals.BillableMarkedServiceDays);
        Assert.Equal(2, report.Totals.NonBillableServiceDays);

        Assert.Collection(report.Periods,
            january =>
            {
                Assert.Equal(3, january.Metrics.BillableMarkedUnits);
                Assert.Equal(2, january.Metrics.NonBillableUnits);
                Assert.Equal(2, january.Metrics.BillableMarkedServiceDays);
                Assert.Equal(1, january.Metrics.NonBillableServiceDays);
            },
            february =>
            {
                Assert.Equal(0, february.Metrics.BillableMarkedUnits);
                Assert.Equal(2, february.Metrics.NonBillableUnits);
                Assert.Equal(0, february.Metrics.BillableMarkedServiceDays);
                Assert.Equal(1, february.Metrics.NonBillableServiceDays);
            });
        Assert.Collection(report.Clients,
            first =>
            {
                Assert.Equal(3, first.Metrics.BillableMarkedUnits);
                Assert.Equal(1, first.Metrics.NonBillableUnits);
                Assert.Equal(2, first.Metrics.BillableMarkedServiceDays);
                Assert.Equal(1, first.Metrics.NonBillableServiceDays);
            },
            second =>
            {
                Assert.Equal(0, second.Metrics.BillableMarkedUnits);
                Assert.Equal(3, second.Metrics.NonBillableUnits);
                Assert.Equal(0, second.Metrics.BillableMarkedServiceDays);
                Assert.Equal(2, second.Metrics.NonBillableServiceDays);
            });
    }

    private static StatisticsNoteFact Note(
        int personId, DateTime date, string status, int minutes,
        int? activities = null, string? legacyNoteType = null, bool isUnbilled = false) =>
        new(personId, date, status, minutes, activities, legacyNoteType, isUnbilled);
}
