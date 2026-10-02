using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class ConsumerScheduleRulesTests
{
    [Fact]
    public void WeeklyScheduleRequiresEffectiveStartAndWeekdays()
    {
        var request = new SaveConsumerScheduleEntryRequest(
            ConsumerScheduleKind.Work, "Work", null, null, null, null,
            ScheduleWeekdays.None, 540, 1020, ModivcareRideStatus.NoRide,
            null, null, null, 0);
        Assert.Contains("weekdays", ConsumerScheduleRules.Validate(request).Keys);
        Assert.DoesNotContain("weekdays", ConsumerScheduleRules.Validate(request with
        {
            EffectiveStart = new DateTime(2026, 10, 1),
            Weekdays = ScheduleWeekdays.Tuesday | ScheduleWeekdays.Thursday
        }).Keys);
    }

    [Fact]
    public void NoRideCannotRetainPickupOrBookingReference()
    {
        var request = new SaveConsumerScheduleEntryRequest(
            ConsumerScheduleKind.DoctorAppointment, "Visit", null,
            new DateTime(2026, 10, 1), null, null, ScheduleWeekdays.None,
            540, 600, ModivcareRideStatus.NoRide, 480, null, "Old booking", 1);
        Assert.Contains("rideStatus", ConsumerScheduleRules.Validate(request).Keys);
    }

    [Fact]
    public void DoctorDateCanBeRecordedBeforeTimesAreKnown()
    {
        var request = new SaveConsumerScheduleEntryRequest(
            ConsumerScheduleKind.DoctorAppointment, "Visit", null,
            new DateTime(2026, 10, 1), null, null, ScheduleWeekdays.None,
            null, null, ModivcareRideStatus.NeedsBooking, null, null, null, 0);
        Assert.Empty(ConsumerScheduleRules.Validate(request));
        Assert.Contains("time", ConsumerScheduleRules.Validate(request with
        {
            Kind = ConsumerScheduleKind.Work, Date = null,
            EffectiveStart = new DateTime(2026, 10, 1),
            Weekdays = ScheduleWeekdays.Monday
        }).Keys);
    }
}
