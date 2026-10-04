using Sati.Services;
using Xunit;

namespace Sati.Tests;

public class DateRolloverRefreshTests
{
    [Fact]
    public async Task MidnightRefreshesEachProjectionOnceDespiteTimerAndActivation()
    {
        var clock = new ManualClock();
        var coordinator = new DateRolloverRefreshCoordinator(clock);
        coordinator.BindAccount(41);
        var counts = new int[3];
        Task<bool> Refresh(DateOnly date, Func<bool> isCurrent)
        {
            Assert.Equal(new DateOnly(2026, 10, 4), date);
            Assert.True(isCurrent());
            for (var i = 0; i < counts.Length; i++) counts[i]++;
            return Task.FromResult(true);
        }
        Assert.False(await coordinator.CheckAsync(41, () => false, Refresh));
        clock.AdvanceDay();
        Assert.True(await coordinator.CheckAsync(41, () => false, Refresh));
        Assert.False(await coordinator.CheckAsync(41, () => false, Refresh));
        Assert.Equal(new[] { 1, 1, 1 }, counts);
    }

    [Fact]
    public async Task DirtyDraftDefersWithoutConsumingTheDay()
    {
        var clock = new ManualClock();
        var coordinator = new DateRolloverRefreshCoordinator(clock);
        coordinator.BindAccount(41);
        clock.AdvanceDay();
        var calls = 0;
        Task<bool> Refresh(DateOnly _, Func<bool> __) { calls++; return Task.FromResult(true); }
        Assert.False(await coordinator.CheckAsync(41, () => true, Refresh));
        Assert.Equal(0, calls);
        Assert.True(await coordinator.CheckAsync(41, () => false, Refresh));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ConcurrentActivationCannotDuplicateTimerRefresh()
    {
        var clock = new ManualClock();
        var coordinator = new DateRolloverRefreshCoordinator(clock);
        coordinator.BindAccount(41);
        clock.AdvanceDay();
        var completion = new TaskCompletionSource<bool>();
        var calls = 0;
        Task<bool> Refresh(DateOnly _, Func<bool> __) { calls++; return completion.Task; }
        var pending = coordinator.CheckAsync(41, () => false, Refresh);
        Assert.False(await coordinator.CheckAsync(41, () => false, Refresh));
        completion.SetResult(true);
        Assert.True(await pending);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AccountSwitchInvalidatesInFlightPublication()
    {
        var clock = new ManualClock();
        var coordinator = new DateRolloverRefreshCoordinator(clock);
        coordinator.BindAccount(41);
        clock.AdvanceDay();
        var completion = new TaskCompletionSource<bool>();
        Func<bool>? ownsRequest = null;
        var pending = coordinator.CheckAsync(41, () => false, (_, current) =>
        {
            ownsRequest = current;
            return completion.Task;
        });
        coordinator.BindAccount(42);
        Assert.False(ownsRequest!());
        completion.SetResult(true);
        Assert.False(await pending);
    }

    [Fact]
    public async Task FailedRefreshRemainsDue()
    {
        var clock = new ManualClock();
        var coordinator = new DateRolloverRefreshCoordinator(clock);
        coordinator.BindAccount(41);
        clock.AdvanceDay();
        Assert.False(await coordinator.CheckAsync(41, () => false, (_, _) => Task.FromResult(false)));
        Assert.True(await coordinator.CheckAsync(41, () => false, (_, _) => Task.FromResult(true)));
    }

    [Fact]
    public void ExistingTimerAndActivationRefreshDateSensitiveViews()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SatiLogica.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "Views", "ShellWindow.xaml.cs"));
        Assert.Contains("await TryRefreshForDateChangeAsync();", source);
        Assert.Contains("_dateRefresh.CheckAsync", source);
        Assert.Contains("HasUnsavedDateRefreshWork", source);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 3, 23, 59, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void AdvanceDay() => _now = _now.AddMinutes(2);
    }
}
