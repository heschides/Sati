using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Signatures;
using Xunit;

namespace Sati.Api.Tests;

public sealed class SignatureProcessingServiceTests
{
    [Fact]
    public async Task HostedSignatureDiscoveryCannotRunWhenResetAdmissionIsDenied()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Reset.Allow = false;
        // Real first tick exercises the original hosted owner without a new test seam.
        await using var services = fixture.Services(TimeProvider.System);
        var worker = services.GetRequiredService<SignatureProcessingService>();
        fixture.Probe.Armed = true;
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var observed = await Task.WhenAny(fixture.Reset.Attempted.Task, fixture.Probe.ReadObserved.Task)
                .WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Same(fixture.Reset.Attempted.Task, observed);
            Assert.Equal(0, fixture.Probe.Reads);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData("worker")]
    [InlineData("feature")]
    [InlineData("production")]
    public async Task DisabledSignaturePassNeverSeeksAdmissionOrReads(string gate)
    {
        await using var f = await Fixture.CreateAsync();
        if (gate == "worker") f.Signature.WorkersEnabled = false;
        if (gate == "feature") f.Signature.Enabled = false;
        if (gate == "production") f.Signature.ExpectedEnvironment = "Production";
        await using var services = f.Services(TimeProvider.System);
        f.Probe.Armed = true;
        await services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, f.Reset.Attempts); Assert.Equal(0, f.Probe.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TargetMismatchBeforeOrDuringAdmissionCannotRead(bool during)
    {
        await using var f = await Fixture.CreateAsync();
        if (during) f.Reset.Before = () =>
        { f.Signature.ExpectedEnvironment = "Demo"; f.Signature.ExpectedDatabaseName = "SatiDemo"; };
        else f.Api = new SatiApiOptions { ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo" };
        await using var services = f.Services(TimeProvider.System);
        f.Probe.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None));
        Assert.Equal(during ? 1 : 0, f.Reset.Attempts);
        Assert.Equal(0, f.Reset.Active); Assert.Equal(0, f.Probe.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisablementDuringAdmissionStopsDiscovery(bool feature)
    {
        await using var f = await Fixture.CreateAsync();
        f.Reset.Before = () => { if (feature) f.Signature.Enabled = false; else f.Signature.WorkersEnabled = false; };
        await using var services = f.Services(TimeProvider.System);
        f.Probe.Armed = true;
        await services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, f.Reset.Attempts); Assert.Equal(0, f.Reset.Active); Assert.Equal(0, f.Probe.Reads);
    }

    [Fact]
    public async Task DisablementDuringProjectionPreventsPackageAndMailReads()
    {
        await using var f = await Fixture.CreateAsync();
        f.Probe.Before = _ => { f.Signature.WorkersEnabled = false; return Task.CompletedTask; };
        await using var services = f.Services(TimeProvider.System);
        f.Probe.Armed = true;
        await services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, f.Probe.Reads); Assert.Equal(0, f.Reset.Active);
    }

    [Fact]
    public async Task CancellationRacingWithDeniedAdmissionCannotReturnNormally()
    {
        await using var f = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        f.Reset.Allow = false; f.Reset.Before = cancellation.Cancel;
        await using var services = f.Services(TimeProvider.System);
        f.Probe.Armed = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(cancellation.Token));
        Assert.Equal(0, f.Probe.Reads); Assert.Equal(0, f.Reset.Active);
    }

    [Fact]
    public async Task RetryingOuterExecutionCannotEnterSignaturePass()
    {
        await using var f = await Fixture.CreateAsync();
        await using var services = f.Services(TimeProvider.System);
        await using var db = f.Open();
        f.Probe.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Retrying(db).ExecuteAsync(() =>
            services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None)));
        Assert.Equal(0, f.Reset.Attempts); Assert.Equal(0, f.Probe.Reads);
    }

    [Fact]
    public async Task DemoSignatureCoordinationFailsClosedOnTestingSqlite()
    {
        await using var f = await Fixture.CreateAsync(); f.Demo();
        var coordination = new SqlDemoWorkerResetCoordination(f.Contexts(), Options.Create(f.Api));
        await using var services = f.Services(TimeProvider.System, coordination);
        f.Probe.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.GetRequiredService<SignatureProcessingService>().RunOnceAsync(CancellationToken.None));
        Assert.Equal(0, f.Probe.Reads);
    }

    [Fact]
    public async Task HostedSignatureTimerWaitsForExactFirstTickAndDisposesOnStop()
    {
        await using var f = await Fixture.CreateAsync();
        var clock = new ManualTimeProvider(); f.Reset.Allow = false;
        var observedClock = new ObservedClock(clock);
        await using var services = f.Services(observedClock);
        var worker = services.GetRequiredService<SignatureProcessingService>();
        f.Probe.Armed = true;
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await observedClock.Registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, clock.ActiveTimerCount); Assert.Equal(0, f.Reset.Attempts);
            clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromTicks(1));
            Assert.Equal(0, f.Reset.Attempts);
            clock.Advance(TimeSpan.FromTicks(1));
            await f.Reset.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, f.Reset.Attempts); Assert.Equal(0, f.Probe.Reads);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
        Assert.Equal(0, clock.ActiveTimerCount);
        clock.Advance(TimeSpan.FromMinutes(1)); Assert.Equal(1, f.Reset.Attempts);
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlExclusiveResetPreventsHostedSignatureDiscovery()
    {
        await using var f = await Fixture.CreateAsync(true); f.Demo();
        var coordination = new SqlDemoWorkerResetCoordination(f.Contexts(), Options.Create(f.Api));
        await using var services = f.Services(TimeProvider.System, coordination);
        var worker = services.GetRequiredService<SignatureProcessingService>();
        await using var reset = f.Open(); await reset.Database.OpenConnectionAsync();
        Assert.True(await ExclusiveAsync(reset) >= 0);
        f.Probe.Armed = true;
        try { await worker.RunOnceAsync(CancellationToken.None); Assert.Equal(0, f.Probe.Reads); }
        finally { await ReleaseAsync(reset); }
        await worker.RunOnceAsync(CancellationToken.None); Assert.Equal(3, f.Probe.Reads);
        Assert.True(await ExclusiveAsync(reset) >= 0); await ReleaseAsync(reset);
    }

    [SqlServerTheory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("fault")]
    public async Task ClaimReleaseSqlSignatureResetLeaseSpansDiscoveryAndReleasesAfterExit(string exit)
    {
        await using var f = await Fixture.CreateAsync(true); f.Demo();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Probe.Before = async token =>
        {
            await release.Task.WaitAsync(token);
            if (exit == "fault") throw new InvalidOperationException("Synthetic signature query fault.");
        };
        using var cancellation = new CancellationTokenSource();
        var coordination = new SqlDemoWorkerResetCoordination(f.Contexts(), Options.Create(f.Api));
        await using var services = f.Services(TimeProvider.System, coordination);
        var worker = services.GetRequiredService<SignatureProcessingService>();
        await using var reset = f.Open(); await reset.Database.OpenConnectionAsync();
        f.Probe.Armed = true;
        var pass = worker.RunOnceAsync(cancellation.Token);
        try
        {
            await f.Probe.ReadObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(-1, await ExclusiveAsync(reset));
            if (exit == "cancel") cancellation.Cancel();
        }
        finally { release.TrySetResult(); }
        if (exit == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pass);
        else await pass;
        Assert.True(await ExclusiveAsync(reset) >= 0); await ReleaseAsync(reset);
        f.Probe.Before = null;
        await worker.RunOnceAsync(CancellationToken.None);
    }

    private static async Task<int> ExclusiveAsync(ApiDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'SatiDemo.FullReset', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @r;";
        return (int)(await command.ExecuteScalarAsync())!;
    }
    private static async Task ReleaseAsync(ApiDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXEC sys.sp_releaseapplock @Resource=N'SatiDemo.FullReset', @LockOwner='Session';";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class Retrying(ApiDbContext db) : ExecutionStrategy(db, 1, TimeSpan.Zero)
    { protected override bool ShouldRetryOn(Exception exception) => false; }

    private sealed class ObservedClock(ManualTimeProvider clock) : TimeProvider
    {
        internal TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();
        public override long GetTimestamp() => clock.GetTimestamp();
        public override long TimestampFrequency => clock.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = clock.CreateTimer(callback, state, dueTime, period);
            Registered.TrySetResult(); return timer;
        }
    }

    private sealed class Fixture(SyntheticPipelineDatabase database) : IAsyncDisposable
    {
        internal Probe Probe { get; } = new();
        internal Reset Reset { get; } = new();
        internal SignatureOptions Signature { get; } = new()
        {
            Enabled = true, WorkersEnabled = true,
            ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests"
        };
        internal SatiApiOptions Api { get; set; } = new()
        { ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests" };
        internal void Demo()
        {
            Signature.ExpectedEnvironment = "Demo"; Signature.ExpectedDatabaseName = "SatiDemo";
            Api = new SatiApiOptions { ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo" };
        }
        internal Factory Contexts() => new(database.Options(Probe));
        internal ApiDbContext Open() => new(database.Options());
        internal static async Task<Fixture> CreateAsync(bool sql = false)
        {
            var database = new SyntheticPipelineDatabase(sqlServer: sql);
            try { await database.InitializeAsync(); return new(database); }
            catch { await database.DisposeAsync(); throw; }
        }
        internal ServiceProvider Services(TimeProvider clock, IDemoWorkerResetCoordination? reset = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IDbContextFactory<ApiDbContext>>(new Factory(database.Options(Probe)));
            services.AddSingleton(clock);
            services.AddSingleton(Signature);
            services.AddSingleton(new SignatureFeature(Signature));
            services.AddSingleton(Options.Create(Api));
            services.AddSingleton<IDemoWorkerResetCoordination>(reset ?? Reset);
            services.AddSingleton<ApiClock>();
            services.AddSingleton<ISignatureBlobStore, UnconfiguredSignatureBlobStore>();
            services.AddSingleton<ISignatureOutboxKeyWrapper, UnconfiguredSigningKeyWrapper>();
            services.AddSingleton<ISignatureEmailSender, DisabledSignatureEmailSender>();
            services.AddSingleton<SignatureOutboxProtector>();
            services.AddSingleton<SignaturePackageBuilder>();
            services.AddSingleton<SignatureCompletionWorker>();
            services.AddSingleton<SignatureMailWorker>();
            services.AddSingleton<SignatureComplianceProjectionService>();
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger<SignatureProcessingService>>(NullLogger<SignatureProcessingService>.Instance);
            services.AddSingleton<SignatureProcessingService>();
            return services.BuildServiceProvider();
        }
        public ValueTask DisposeAsync() => database.DisposeAsync();
    }

    private sealed class Factory(DbContextOptions<ApiDbContext> options) : IDbContextFactory<ApiDbContext>
    {
        public ApiDbContext CreateDbContext() => new(options);
        public Task<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(CreateDbContext()); }
    }

    private sealed class Probe : DbCommandInterceptor
    {
        internal bool Armed;
        internal int Reads;
        internal Func<CancellationToken, Task>? Before;
        internal TaskCompletionSource ReadObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Interlocked.Increment(ref Reads); ReadObserved.TrySetResult();
                if (Before is { } before) await before(cancellationToken);
            }
            return result;
        }
    }

    private sealed class Reset : IDemoWorkerResetCoordination
    {
        internal bool Allow = true;
        internal int Attempts, Active;
        internal Action? Before;
        internal TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, T unavailableResult, CancellationToken token)
        {
            Interlocked.Increment(ref Attempts); Attempted.TrySetResult(); Before?.Invoke();
            if (!Allow) return unavailableResult;
            Interlocked.Increment(ref Active);
            try { return await operation(token); }
            finally { Interlocked.Decrement(ref Active); }
        }
        public Task<T> RunDispatchAsync<T>(Guid id, Func<CancellationToken, Task<T>> operation, T unavailable, CancellationToken token) => throw new NotSupportedException();
        public Task<T> RunAccountPreflightAsync<T>(int agency, Guid account, Func<IAccountPreflightLease, CancellationToken, Task<T>> operation, T unavailable, CancellationToken token) => throw new NotSupportedException();
    }
}
