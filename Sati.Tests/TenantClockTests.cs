using SatiLogica.Contracts;
using Xunit;

namespace Sati.Tests;

public sealed class TenantClockTests
{
    [Theory]
    [InlineData(2026, 7, 1, 2026, 6, 30, 20, 30)]
    [InlineData(2027, 1, 1, 2026, 12, 31, 19, 30)]
    public void UtcMidnightUsesPreviousMaineDate(
        int utcYear, int utcMonth, int utcDay,
        int localYear, int localMonth, int localDay, int localHour, int localMinute)
    {
        var instant = new DateTimeOffset(utcYear, utcMonth, utcDay, 0, 30, 0, TimeSpan.Zero);
        var provider = new FrozenTimeProvider(instant);
        var clock = new TenantClock(TenantClock.MaineTimeZoneId, provider);
        var expectedDate = new DateTime(localYear, localMonth, localDay);

        Assert.Equal(expectedDate, clock.Today);
        Assert.Equal(expectedDate, clock.DateAt(instant));
        Assert.Equal(expectedDate, clock.ToLocalDate(instant.UtcDateTime));
        Assert.Equal(expectedDate, TenantClock.MaineDate(instant));
        Assert.Equal(expectedDate, TenantClock.MaineDate(instant.UtcDateTime));
        Assert.Equal(expectedDate.AddHours(localHour).AddMinutes(localMinute), clock.Now);
        Assert.Equal(instant, clock.UtcNow);
    }

    private sealed class FrozenTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
