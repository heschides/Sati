using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Sati.Api.Infrastructure;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class ClearinghouseDispatchApiTests
{
    [Fact]
    public async Task ScheduleRechecksActivityAtTurnAdmissionAfterSchedulingPause()
    {
        var timer = new ManualTimeProvider();
        var clock = new AdmissionPauseClock(timer);
        var schedule = Schedule(clock);
        schedule.SignalActivity();
        using var stop = new CancellationTokenSource();
        var turns = 0;
        var run = schedule.RunAsync(_ => Task.FromResult(new DispatchTurn(DispatchTurnKind.Empty,
            Interlocked.Increment(ref turns) == 1 ? clock.GetUtcNow().UtcDateTime.AddSeconds(10) : null)),
            () => Assert.Fail("Unexpected fault"), stop.Token);
        try
        {
            // Expire activity between the wait decision's snapshot and turn admission.
            clock.ExpireOnNextUtcRead();
            timer.Advance(TimeSpan.FromSeconds(3));
            await UntilAsync(() => clock.Expired && timer.ActiveTimerCount == 0);
            Assert.Equal(1, turns);
        }
        finally { stop.Cancel(); await run; }
    }

    private sealed class AdmissionPauseClock(ManualTimeProvider inner) : TimeProvider
    {
        private int pending, expired;
        public bool Expired => Volatile.Read(ref expired) != 0;
        public void ExpireOnNextUtcRead() => Interlocked.Exchange(ref pending, 1);
        public override long TimestampFrequency => inner.TimestampFrequency;
        public override long GetTimestamp() => inner.GetTimestamp();
        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Exchange(ref pending, 0) == 1)
            {
                inner.Advance(TimeSpan.FromMinutes(5));
                Volatile.Write(ref expired, 1);
            }
            return inner.GetUtcNow();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            inner.CreateTimer(callback, state, dueTime, period);
    }

    [Fact]
    public async Task SchedulePublisherSignalsOnlyValidatedAllowlistedRequests()
    {
        await using var fixture = await Fixture.CreateAsync();
        var schedule = fixture.Factory.Services.GetRequiredService<ClearinghouseDispatchSchedule>();
        var before = schedule.Generation;
        using var anonymous = fixture.Factory.CreateClient();
        using var refused = await anonymous.GetAsync("/api/v1/billing/clearinghouse");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        using var health = await anonymous.GetAsync("/health/live");
        health.EnsureSuccessStatusCode();
        using var profile = await fixture.Biller.GetAsync("/api/v1/me");
        profile.EnsureSuccessStatusCode();
        using var renew = await fixture.Biller.PostAsync("/api/v1/auth/renew", null);
        renew.EnsureSuccessStatusCode();
        Assert.Equal(before, schedule.Generation);

        using var workspace = await fixture.Biller.GetAsync("/api/v1/billing/clearinghouse");
        workspace.EnsureSuccessStatusCode();
        Assert.Equal(++before, schedule.Generation);
        await fixture.GenerateAsync(fixture.AccountId);
        Assert.Equal(before, schedule.Generation);
        var request = new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId);
        using var queued = await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches", request);
        queued.EnsureSuccessStatusCode();
        Assert.Equal(++before, schedule.Generation);
        using var replay = await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches", request);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(++before, schedule.Generation);
        using var foreign = await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            request with { AccountId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(before, schedule.Generation);

        await using (var db = fixture.Factory.OpenDatabase())
        {
            (await db.Users.SingleAsync(row => row.Id == fixture.Actors.BillerId)).SecurityVersion++;
            await db.SaveChangesAsync();
        }
        using var revoked = await fixture.Biller.GetAsync("/api/v1/billing/clearinghouse");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal(before, schedule.Generation);
        using var newLogin = await fixture.Factory.SignInAsync("synthetic-biller");
        Assert.Equal(before + 1, schedule.Generation);
    }

    [Fact]
    public async Task ScheduleReopenHintFollowsCommitAndAccountLeaseDisposal()
    {
        var keys = new ControlledPreflightKeys();
        var coordination = new CountingScheduleCoordination();
        await using var fixture = await CreatePreflightFixtureAsync(keys, coordination: coordination);
        Assert.True(await CreateIsolationWorker(fixture.Factory, keys, new IsolationRecordingConnector())
            .ProcessOneAsync(CancellationToken.None));
        var request = await ReadReopenRequestAsync(fixture);
        var schedule = fixture.Factory.Services.GetRequiredService<ClearinghouseDispatchSchedule>();
        var before = schedule.Generation;
        coordination.BeforeAccountRelease = async () =>
        {
            Assert.Equal(before, schedule.Generation);
            await using var committed = fixture.Factory.OpenDatabase();
            Assert.Equal(ClearinghousePreflightDisposition.Ready,
                (await committed.ClearinghouseDispatchReadiness.SingleAsync()).Disposition);
        };
        keys.Available = true;
        using var reopened = await fixture.Biller.PostAsJsonAsync(PreflightReopenRoute(fixture.AccountId), request);
        reopened.EnsureSuccessStatusCode();
        Assert.True(coordination.AccountReleased);
        Assert.Equal(before + 1, schedule.Generation);
        coordination.BeforeAccountRelease = null;
        using var stale = await fixture.Biller.PostAsJsonAsync(PreflightReopenRoute(fixture.AccountId), request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(before + 1, schedule.Generation);
    }

    [Fact]
    public Task ScheduleSnapshotReturnsOnlyQueuedAccountDue() => VerifyScheduleSnapshotAsync(false);

    [Fact]
    public Task ScheduleHostedFakeTimeExpiresWithoutFurtherDatabaseTurns() => VerifyHostedSchedulingAsync(false);

    [SqlServerFact]
    public Task ClaimReleaseSqlScheduleHostedFakeTimeExpiresWithoutFurtherDatabaseTurns() => VerifyHostedSchedulingAsync(true);

    private static async Task VerifyHostedSchedulingAsync(bool sqlServer)
    {
        await using var fixture = await Fixture.CreateAsync(sqlServer: sqlServer);
        var clock = new ManualTimeProvider();
        var gate = fixture.Factory.Services.GetRequiredService<ClearinghouseDispatchGate>();
        var schedule = new ClearinghouseDispatchSchedule(clock, gate);
        var coordination = new CountingScheduleCoordination(sqlServer ? SqlPreflightCoordination(fixture.Factory) : null);
        using var worker = new ClearinghouseDispatchWorker(
            fixture.Factory.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Sati.Api.Data.ApiDbContext>>(),
            new ThrowingConnector(), gate, fixture.Factory.Services.GetRequiredService<EnvelopeProtector>(),
            fixture.Factory.Services.GetRequiredService<IClaimMdSandboxKeySource>(), coordination,
            fixture.Factory.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClearinghouseDispatchWorker>>(),
            clock, schedule, new ClearinghouseDispatchSelector(
                fixture.Factory.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Sati.Api.Data.ApiDbContext>>(), gate, clock));
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await coordination.FirstTurn.Task.WaitAsync(TimeSpan.FromSeconds(15));
            clock.Advance(TimeSpan.FromDays(1));
            Assert.Equal(1, coordination.Turns);
            Assert.Equal(0, clock.ActiveTimerCount);
            schedule.SignalActivity();
            await coordination.SecondTurn.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await UntilAsync(() => clock.ActiveTimerCount == 1);
            clock.Advance(TimeSpan.FromMinutes(5));
            await UntilAsync(() => clock.ActiveTimerCount == 0);
            clock.Advance(TimeSpan.FromDays(1));
            Assert.Equal(2, coordination.Turns);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [SqlServerFact]
    public Task ClaimReleaseSqlScheduleSnapshotReturnsOnlyQueuedAccountDue() => VerifyScheduleSnapshotAsync(true);

    private static async Task VerifyScheduleSnapshotAsync(bool sqlServer)
    {
        var keys = new ControlledPreflightKeys();
        await using var fixture = await CreatePreflightFixtureAsync(keys, sqlServer: sqlServer);
        var clock = new PreflightTestClock();
        var connector = new IsolationRecordingConnector();
        var worker = CreateIsolationWorker(fixture.Factory, keys, connector, clock,
            sqlServer ? SqlPreflightCoordination(fixture.Factory) : null);
        Assert.Equal(DispatchTurnKind.Processed, (await worker.ProcessTurnAsync(CancellationToken.None)).Kind);
        var empty = await worker.ProcessTurnAsync(CancellationToken.None);
        Assert.Equal(DispatchTurnKind.Empty, empty.Kind);
        DateTime due;
        await using (var db = fixture.Factory.OpenDatabase())
            due = (await db.ClearinghouseDispatchReadiness.SingleAsync()).NextEligibleAtUtc!.Value;
        Assert.Equal(due, empty.NextEligibleUtc);
        Assert.Equal(1, keys.Calls);
        Assert.Empty(connector.Dispatches);
        // A due time belonging to work that is now uncertain must not arm a retry.
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var dispatch = await db.ClearinghouseDispatches.SingleAsync();
            dispatch.State = ClearinghouseDispatchState.Sending; dispatch.Revision++;
            await db.SaveChangesAsync();
        }
        var uncertain = await worker.ProcessTurnAsync(CancellationToken.None);
        Assert.Equal(DispatchTurnKind.Empty, uncertain.Kind);
        Assert.Null(uncertain.NextEligibleUtc);
        Assert.Equal(1, keys.Calls); Assert.Empty(connector.Dispatches);
    }

    [Fact]
    public async Task ScheduleUtcChangesCannotExtendMonotonicActivityOrCauseEarlyEligibility()
    {
        var clock = new ManualTimeProvider();
        var schedule = Schedule(clock);
        schedule.SignalActivity();
        using var stop = new CancellationTokenSource();
        var due = clock.GetUtcNow().UtcDateTime.AddSeconds(10);
        var turns = 0; var eligible = 0;
        var run = schedule.RunAsync(_ =>
        {
            turns++;
            if (clock.GetUtcNow().UtcDateTime >= due) eligible++;
            return Task.FromResult(new DispatchTurn(DispatchTurnKind.Empty, eligible == 0 ? due : null));
        }, () => Assert.Fail("Unexpected fault"), stop.Token);
        try
        {
            clock.ShiftUtc(TimeSpan.FromHours(-1));
            clock.Advance(TimeSpan.FromSeconds(10));
            await UntilAsync(() => clock.CreatedTimerCount >= 2 && clock.ActiveTimerCount == 1);
            Assert.Equal(1, turns);
            clock.Advance(TimeSpan.FromSeconds(50));
            await UntilAsync(() => Volatile.Read(ref turns) == 2 && clock.ActiveTimerCount == 1);
            Assert.Equal(0, eligible);
            clock.Advance(TimeSpan.FromSeconds(3));
            await UntilAsync(() => clock.CreatedTimerCount >= 4 && clock.ActiveTimerCount == 1);
            clock.ShiftUtc(TimeSpan.FromHours(2));
            clock.Advance(TimeSpan.FromSeconds(57));
            await UntilAsync(() => Volatile.Read(ref turns) == 3 && clock.ActiveTimerCount == 1);
            Assert.Equal(1, eligible);
            clock.Advance(TimeSpan.FromMinutes(5));
            await UntilAsync(() => clock.ActiveTimerCount == 0);
            var finished = turns;
            clock.Advance(TimeSpan.FromDays(1));
            Assert.Equal(finished, turns);
        }
        finally { stop.Cancel(); await run; }
    }

    [Fact]
    public async Task ScheduleExpiresAndFutureDueDoesNotWakeDormantSql()
    {
        var clock = new ManualTimeProvider();
        var schedule = Schedule(clock);
        schedule.SignalActivity();
        using var stop = new CancellationTokenSource();
        var turns = 0;
        var run = schedule.RunAsync(_ =>
        {
            Interlocked.Increment(ref turns);
            return Task.FromResult(new DispatchTurn(DispatchTurnKind.Empty, clock.GetUtcNow().UtcDateTime.AddHours(1)));
        }, () => Assert.Fail("Unexpected scheduling fault"), stop.Token);
        try
        {
            Assert.Equal(1, turns);
            clock.Advance(TimeSpan.FromMinutes(5));
            await UntilAsync(() => clock.ActiveTimerCount == 0);
            clock.Advance(TimeSpan.FromDays(1));
            Assert.Equal(1, turns);
            schedule.SignalActivity();
            await UntilAsync(() => Volatile.Read(ref turns) == 2 && clock.ActiveTimerCount == 1);
        }
        finally { stop.Cancel(); await run; }
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task ScheduleColdStartIsOneTurnWithoutAnActiveLease()
    {
        var clock = new ManualTimeProvider();
        var schedule = Schedule(clock);
        using var stop = new CancellationTokenSource();
        var turns = 0;
        var run = schedule.RunAsync(_ =>
        {
            turns++;
            return Task.FromResult(DispatchTurn.Processed);
        }, () => Assert.Fail("Unexpected fault"), stop.Token);
        try
        {
            clock.Advance(TimeSpan.FromDays(1));
            Assert.Equal(1, turns);
            Assert.Equal(0, clock.ActiveTimerCount);
        }
        finally { stop.Cancel(); await run; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScheduleDisabledOrCanceledMakesNoTurn(bool disabled)
    {
        var clock = new ManualTimeProvider();
        var schedule = Schedule(clock, !disabled);
        using var stop = new CancellationTokenSource();
        if (!disabled) stop.Cancel();
        schedule.SignalActivity();
        var turns = 0;
        await schedule.RunAsync(_ => { turns++; return Task.FromResult(DispatchTurn.Processed); },
            () => Assert.Fail("Unexpected fault"), stop.Token);
        Assert.Equal(0, turns);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task SchedulePacesSuccessAndCoalescesSignalFlood()
    {
        var clock = new ManualTimeProvider();
        var schedule = Schedule(clock);
        schedule.SignalActivity();
        using var stop = new CancellationTokenSource();
        var starts = new System.Collections.Concurrent.ConcurrentQueue<long>();
        var run = schedule.RunAsync(_ =>
        {
            starts.Enqueue(clock.GetTimestamp());
            return Task.FromResult(starts.Count <= 20 ? DispatchTurn.Processed : new DispatchTurn(DispatchTurnKind.Empty));
        }, () => Assert.Fail("Unexpected fault"), stop.Token);
        try
        {
            for (var i = 0; i < 1000; i++) schedule.SignalActivity();
            clock.Advance(TimeSpan.FromMilliseconds(2999));
            Assert.Single(starts);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await UntilAsync(() => starts.Count == 2 && clock.ActiveTimerCount == 1);
            for (var count = 3; count <= 20; count++)
            {
                clock.Advance(TimeSpan.FromSeconds(3));
                var expected = count;
                await UntilAsync(() => starts.Count == expected && clock.ActiveTimerCount == 1);
            }
            Assert.Equal(20, starts.Count(x => x < TimeSpan.FromMinutes(1).Ticks));
            Assert.All(starts.Zip(starts.Skip(1)), pair =>
                Assert.True(pair.Second - pair.First >= TimeSpan.FromSeconds(3).Ticks));
        }
        finally { stop.Cancel(); await run; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduleDoesNotLoseSignalAroundEmptyDiscovery(bool signalAfterParking)
    {
        var clock = new ManualTimeProvider();
        var schedule = Schedule(clock);
        schedule.SignalActivity();
        using var stop = new CancellationTokenSource();
        var discovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var turns = 0;
        var run = schedule.RunAsync(async _ =>
        {
            if (Interlocked.Increment(ref turns) == 1)
            {
                discovered.TrySetResult();
                await release.Task;
            }
            return new DispatchTurn(DispatchTurnKind.Empty);
        }, () => Assert.Fail("Unexpected fault"), stop.Token);
        try
        {
            await discovered.Task;
            if (signalAfterParking)
            {
                release.TrySetResult();
                await UntilAsync(() => clock.ActiveTimerCount == 1);
            }
            schedule.SignalActivity();
            release.TrySetResult();
            await UntilAsync(() => clock.ActiveTimerCount == 1);
            clock.Advance(TimeSpan.FromSeconds(3));
            await UntilAsync(() => Volatile.Read(ref turns) == 2 && clock.ActiveTimerCount == 1);
        }
        finally { release.TrySetResult(); stop.Cancel(); await run; }
    }

    [Fact]
    public async Task ScheduleUsesExactDueAndActiveReconciliationBoundaries()
    {
        var clock = new ManualTimeProvider();
        var schedule = Schedule(clock);
        schedule.SignalActivity();
        using var stop = new CancellationTokenSource();
        var due = clock.GetUtcNow().UtcDateTime.AddSeconds(10);
        var turns = 0;
        var run = schedule.RunAsync(_ => Task.FromResult(new DispatchTurn(DispatchTurnKind.Empty,
            Interlocked.Increment(ref turns) == 1 ? due : null)), () => Assert.Fail("Unexpected fault"), stop.Token);
        try
        {
            clock.Advance(TimeSpan.FromMilliseconds(9999));
            Assert.Equal(1, turns);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await UntilAsync(() => Volatile.Read(ref turns) == 2 && clock.ActiveTimerCount == 1);
            clock.Advance(TimeSpan.FromMilliseconds(59_999));
            Assert.Equal(2, turns);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await UntilAsync(() => Volatile.Read(ref turns) == 3 && clock.ActiveTimerCount == 1);
        }
        finally { stop.Cancel(); await run; }
    }

    [Fact]
    public async Task ScheduleLaneSkippedUsesOrdinaryPacingWithoutRenewingActivity()
    {
        var clock = new ManualTimeProvider(); var schedule = Schedule(clock);
        schedule.SignalActivity(); var generation = schedule.Generation;
        using var stop = new CancellationTokenSource(); var turns = 0;
        var run = schedule.RunAsync(_ => { Interlocked.Increment(ref turns); return Task.FromResult(DispatchTurn.LaneSkipped); },
            () => Assert.Fail("Unexpected fault"), stop.Token);
        try
        {
            clock.Advance(TimeSpan.FromMilliseconds(2999)); Assert.Equal(1, turns);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await UntilAsync(() => Volatile.Read(ref turns) == 2 && clock.ActiveTimerCount == 1);
            Assert.Equal(generation, schedule.Generation);
            clock.Advance(TimeSpan.FromMinutes(5));
            await UntilAsync(() => clock.ActiveTimerCount == 0);
            Assert.Equal(100, turns); // Starts at 0..297s; no turn at the exact 300s expiry.
            clock.Advance(TimeSpan.FromDays(1)); Assert.Equal(100, turns);
        }
        finally { stop.Cancel(); await run; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduleSharedFailureOrContentionCannotBeBypassedByHints(bool throws)
    {
        var clock = new ManualTimeProvider();
        var schedule = Schedule(clock);
        schedule.SignalActivity();
        using var stop = new CancellationTokenSource();
        var turns = 0;
        var failures = 0;
        var run = schedule.RunAsync(_ =>
        {
            Interlocked.Increment(ref turns);
            if (throws) throw new InvalidOperationException("synthetic fault");
            return Task.FromResult(DispatchTurn.Contended);
        }, () => failures++, stop.Token);
        try
        {
            clock.Advance(TimeSpan.FromMilliseconds(59_999));
            for (var i = 0; i < 100; i++) schedule.SignalActivity();
            Assert.Equal(1, turns);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await UntilAsync(() => Volatile.Read(ref turns) == 2 && clock.ActiveTimerCount == 1);
            Assert.Equal(throws ? 2 : 0, failures);
        }
        finally { stop.Cancel(); await run; }
    }

    [Fact]
    public async Task ScheduleRejectsTwoRunnersAndExpiryDoesNotCancelInFlightWork()
    {
        var clock = new ManualTimeProvider();
        var schedule = Schedule(clock);
        schedule.SignalActivity();
        using var stop = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var turns = 0;
        var run = schedule.RunAsync(async token =>
        {
            turns++;
            await release.Task;
            Assert.False(token.IsCancellationRequested);
            return DispatchTurn.Processed;
        }, () => Assert.Fail("Unexpected fault"), stop.Token);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => schedule.RunAsync(
                _ => Task.FromResult(DispatchTurn.Processed), () => { }, stop.Token));
            clock.Advance(TimeSpan.FromHours(1));
            Assert.Equal(1, turns);
            release.TrySetResult();
            await Task.Yield();
            Assert.Equal(1, turns);
        }
        finally { release.TrySetResult(); stop.Cancel(); await run; }
    }

    private static ClearinghouseDispatchSchedule Schedule(TimeProvider clock, bool enabled = true) => new(
        clock, new ClearinghouseDispatchGate(Options.Create(new SatiApiOptions
        {
            ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests",
            EnableSyntheticClearinghouseDispatch = enabled
        }), new ScheduleHostEnvironment()));

    private sealed class ScheduleHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Sati.Api.Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(1, deadline.Token);
    }

    [Fact]
    public async Task HostedEmptyQueueStopsSchedulingWithoutActivity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var coordination = new CountingScheduleCoordination();
        using var worker = fixture.Worker(new ThrowingConnector(), coordination);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await coordination.FirstTurn.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Observe the real pre-change three-second loop. A fake clock alone
            // would not move its un-injected Task.Delay and would miss the defect.
            await Task.WhenAny(coordination.SecondTurn.Task, Task.Delay(TimeSpan.FromSeconds(4)));
        }
        finally { await worker.StopAsync(CancellationToken.None); }
        Assert.Equal(1, coordination.Turns);
    }

    private sealed class CountingScheduleCoordination(IDemoWorkerResetCoordination? inner = null) : IDemoWorkerResetCoordination
    {
        public Func<Task>? BeforeAccountRelease { get; set; }
        public bool AccountReleased { get; private set; }
        private int turns;
        public int Turns => Volatile.Read(ref turns);
        public TaskCompletionSource FirstTurn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondTurn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, T unavailableResult,
            CancellationToken token)
        {
            var result = inner is null ? await operation(token) : await inner.RunAsync(operation, unavailableResult, token);
            var count = Interlocked.Increment(ref turns);
            if (count == 1) FirstTurn.TrySetResult();
            if (count == 2) SecondTurn.TrySetResult();
            return result;
        }
        public Task<T> RunDispatchAsync<T>(Guid id, Func<CancellationToken, Task<T>> operation,
            T unavailableResult, CancellationToken token) => inner is null ? operation(token)
                : inner.RunDispatchAsync(id, operation, unavailableResult, token);
        public async Task<T> RunAccountPreflightAsync<T>(int agencyId, Guid accountId,
            Func<IAccountPreflightLease, CancellationToken, Task<T>> operation,
            T unavailableResult, CancellationToken token)
        {
            AccountReleased = false;
            async Task<T> Held(IAccountPreflightLease lease, CancellationToken heldToken)
            {
                var result = await operation(lease, heldToken);
                if (BeforeAccountRelease is { } check) await check();
                return result;
            }
            var result = inner is null ? await Held(new UncoordinatedAccountPreflightLease(), token)
                : await inner.RunAccountPreflightAsync(agencyId, accountId, Held, unavailableResult, token);
            AccountReleased = true;
            return result;
        }
    }
}
