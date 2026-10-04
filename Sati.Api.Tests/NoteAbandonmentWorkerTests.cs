using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

public sealed class NoteAbandonmentWorkerTests
{
    [Fact]
    public async Task DisabledWorkerDoesNotOpenSqlOrTryCoordination()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        await using var factory = new SatiApiFactory { ClockOverride = clock };
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        var worker = CreateWorker(factory, clock, enabled: false, new RefusingCoordination());

        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WakeSweepCoversEveryAgencyAndOnlyRunsAgainOnTheNextLocalDay()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        await using var factory = new SatiApiFactory { ClockOverride = clock };
        var own = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending);
        var peer = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending, personId: 103);
        var otherAgency = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending, personId: 201);
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination());

        Assert.True(await worker.RunDueAsync(CancellationToken.None) >= 3);
        Assert.Equal((NoteWorkflow.Abandoned, 2), await factory.GetNoteStateAsync(own));
        Assert.Equal((NoteWorkflow.Abandoned, 2), await factory.GetNoteStateAsync(peer));
        Assert.Equal((NoteWorkflow.Abandoned, 2), await factory.GetNoteStateAsync(otherAgency));
        var events = await factory.GetAuditEventsAsync("note.abandoned-by-system");
        Assert.Equal(2, events.Count);
        Assert.All(events, audit => Assert.Equal(SystemActor.UserId, audit.ActorUserId));
        Assert.Contains(events, audit => audit.AgencyId == 1 &&
            NoteIds(audit.MetadataJson).Contains(own) && NoteIds(audit.MetadataJson).Contains(peer));
        Assert.Contains(events, audit => audit.AgencyId == 2 &&
            NoteIds(audit.MetadataJson).Contains(otherAgency));

        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);

        var next = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending);
        clock.Instant = clock.Instant.AddDays(1);
        Assert.Equal(1, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal((NoteWorkflow.Abandoned, 2), await factory.GetNoteStateAsync(next));
        Assert.Equal(3, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);
    }

    private static int[] NoteIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("noteIds").EnumerateArray()
            .Select(item => item.GetInt32()).ToArray();
    }

    [Fact]
    public async Task SqlCoordinationFailsClosedOnNonSqlStorage()
    {
        await using var factory = new SatiApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        var contexts = factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>();
        var coordination = new SqlNoteAbandonmentCoordination(contexts,
            Options.Create(new SatiApiOptions { ExpectedEnvironment = "Testing",
                ExpectedDatabaseName = "SatiApiTests" }));
        var invoked = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordination.RunOnceAsync(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, CancellationToken.None));
        Assert.False(invoked);
    }

    [SqlServerFact]
    public async Task SeparateApiHostsCannotSweepAtTheSameTime()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        var options = database.Options();
        var testOptions = Options.Create(new SatiApiOptions { ExpectedEnvironment = "Testing",
            ExpectedDatabaseName = "SatiApiTests" });
        var first = new SqlNoteAbandonmentCoordination(new OwnedFactory(options), testOptions);
        var second = new SqlNoteAbandonmentCoordination(new OwnedFactory(options), testOptions);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRun = first.RunOnceAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
        }, CancellationToken.None);
        await entered.Task;
        var secondEntered = false;
        Assert.False(await second.RunOnceAsync(_ =>
        {
            secondEntered = true;
            return Task.CompletedTask;
        }, CancellationToken.None));
        Assert.False(secondEntered);
        release.SetResult();
        Assert.True(await firstRun);
    }

    [SqlServerFact]
    public async Task DemoResetExclusiveLeasePreventsWorkerSweep()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        var options = database.Options();
        await using var resetDb = new ApiDbContext(options);
        await resetDb.Database.OpenConnectionAsync();
        await using var takeResetLease = resetDb.Database.GetDbConnection().CreateCommand();
        takeResetLease.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
            "@Resource = N'SatiDemo.FullReset', @LockMode = 'Exclusive', " +
            "@LockOwner = 'Session', @LockTimeout = 0; SELECT @result;";
        Assert.True(Convert.ToInt32(await takeResetLease.ExecuteScalarAsync()) >= 0);
        try
        {
            var coordination = new SqlNoteAbandonmentCoordination(new OwnedFactory(options),
                Options.Create(new SatiApiOptions { ExpectedEnvironment = "Demo",
                    ExpectedDatabaseName = "SatiDemo" }));
            var entered = false;
            Assert.False(await coordination.RunOnceAsync(_ =>
            {
                entered = true;
                return Task.CompletedTask;
            }, CancellationToken.None));
            Assert.False(entered);
        }
        finally
        {
            await using var releaseResetLease = resetDb.Database.GetDbConnection().CreateCommand();
            releaseResetLease.CommandText = "EXEC sys.sp_releaseapplock " +
                "@Resource = N'SatiDemo.FullReset', @LockOwner = 'Session';";
            await releaseResetLease.ExecuteNonQueryAsync();
        }
    }

    private static NoteAbandonmentWorker CreateWorker(SatiApiFactory factory, TimeProvider provider,
        bool enabled, INoteAbandonmentCoordination coordination)
    {
        var services = factory.Services;
        return new NoteAbandonmentWorker(
            services.GetRequiredService<IDbContextFactory<ApiDbContext>>(),
            services.GetRequiredService<NoteAbandonmentSweep>(),
            coordination,
            new FixedOptionsMonitor(new SatiApiOptions { EnableNoteAbandonmentWorker = enabled }),
            services.GetRequiredService<ApiClock>(), provider,
            NullLogger<NoteAbandonmentWorker>.Instance);
    }

    private sealed class InlineCoordination : INoteAbandonmentCoordination
    {
        public async Task<bool> RunOnceAsync(Func<CancellationToken, Task> sweep, CancellationToken token)
        {
            await sweep(token);
            return true;
        }
    }

    private sealed class RefusingCoordination : INoteAbandonmentCoordination
    {
        public Task<bool> RunOnceAsync(Func<CancellationToken, Task> sweep, CancellationToken token) =>
            throw new Xunit.Sdk.XunitException("Disabled workers must not attempt SQL coordination.");
    }

    private sealed class FixedOptionsMonitor(SatiApiOptions options) : IOptionsMonitor<SatiApiOptions>
    {
        public SatiApiOptions CurrentValue => options;
        public SatiApiOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<SatiApiOptions, string?> listener) => null;
    }

    private sealed class FrozenTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public DateTimeOffset Instant { get; set; } = instant;
        public override DateTimeOffset GetUtcNow() => Instant;
    }

    private sealed class OwnedFactory(DbContextOptions<ApiDbContext> options)
        : IDbContextFactory<ApiDbContext>
    {
        public ApiDbContext CreateDbContext() => new(options);
        public ValueTask<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }
}
