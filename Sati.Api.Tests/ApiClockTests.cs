using Microsoft.Extensions.Options;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using SatiLogica.Contracts;
using Xunit;

namespace Sati.Api.Tests;

public sealed class ApiClockTests
{
    [Theory]
    [InlineData(2026, 7, 1, 2026, 6, 30, 20, 30)]
    [InlineData(2027, 1, 1, 2026, 12, 31, 19, 30)]
    public void ApiUsesPreviousMaineDateAtUtcMidnight(
        int utcYear, int utcMonth, int utcDay,
        int localYear, int localMonth, int localDay, int localHour, int localMinute)
    {
        var instant = new DateTimeOffset(utcYear, utcMonth, utcDay, 0, 30, 0, TimeSpan.Zero);
        var clock = new ApiClock(
            Options.Create(new SatiApiOptions { TimeZoneId = TenantClock.MaineTimeZoneId }),
            new FrozenTimeProvider(instant));
        var expectedDate = new DateTime(localYear, localMonth, localDay);

        Assert.Equal(expectedDate, clock.Today);
        Assert.Equal(expectedDate, clock.ToAgencyDate(instant.UtcDateTime));
        Assert.Equal(expectedDate.AddHours(localHour).AddMinutes(localMinute), clock.Now);
        Assert.Equal(instant, clock.UtcNow);
    }

    [Fact]
    public void AnnualReminderAndSafetyDeviceValidationUseTheInjectedMaineDay()
    {
        var instant = new DateTimeOffset(2026, 7, 1, 0, 30, 0, TimeSpan.Zero);
        var clock = new ApiClock(Options.Create(new SatiApiOptions()), new FrozenTimeProvider(instant));
        var key = "release:v1:2026-07-01:dhhs:annual";
        var obligation = new ReleaseComplianceFact(
            key, ReleaseObligationCategory.Dhhs, new DateTime(2026, 7, 1),
            new DateTime(2026, 6, 1), null,
            [new ReleaseAttestationFact(key, new DateTime(2026, 7, 1), instant.UtcDateTime)],
            Guid.NewGuid(), new DateTime(2026, 7, 1), new DateTime(2026, 6, 1), null);

        var reminder = AnnualDocumentReminder.Describe(true, true, [], clock.Today,
            [obligation], safetyPlanAttested: true, privacyPracticesAttested: true);
        Assert.Contains("DHHS authorization to release", reminder);

        var meeting = new SafetyDeviceRequest(PlanningTeamMeetingDate: new DateOnly(2026, 7, 1));
        var errors = SafetyDeviceRules.Validate(meeting, DateOnly.FromDateTime(clock.Today));
        Assert.Contains(nameof(SafetyDeviceRequest.PlanningTeamMeetingDate), errors.Keys);
    }

    private sealed class FrozenTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
