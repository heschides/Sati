using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Xunit;
using Xunit.Sdk;

namespace Sati.Api.Tests;

public sealed class NoteWorkerConnectionLifetimeTests
{
    [Fact]
    public void TrackerDoesNotCreditUnconfirmedCleanupOrDoubleCountOneConnection()
    {
        var probe = new WorkerConnectionProbe();
        using var connection = new SqlConnection(); // no open or server
        probe.Observe(connection, "opening");
        probe.Observe(connection, "failed");
        probe.Observe(connection, "closing");
        Assert.Equal(1, probe.Held);
        probe.Observe(connection, "opening");
        probe.Observe(connection, "opened");
        probe.Observe(connection, "disposing");
        Assert.Equal(1, probe.Held);
        Assert.Equal(1, probe.Peak);
        Assert.Equal(2, probe.Attempts);
        probe.Observe(connection, "disposed");
        Assert.Equal(0, probe.Held);
        Assert.Equal(1, probe.SeenConnections);
    }

    [SqlServerFact]
    public async Task NormalPassHasOneCoordinationAndOneActiveDiscoveryOrSweepConnection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var worker = fixture.Worker();
        fixture.Audit.BeforeSave = () =>
        {
            Assert.Equal(2, fixture.Probe.Held);
            Assert.Equal(ConnectionState.Closed, fixture.Contexts.Connections[1].State); // discovery object still in scope
            return Task.CompletedTask;
        };
        Assert.Equal(2, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, fixture.Audit.Saves);
        Assert.Equal(2, fixture.Probe.Peak);
        Assert.Equal(0, fixture.Probe.Held);
        Assert.Equal(6, fixture.Contexts.Connections.Count); // two strategy contexts never open
        Assert.Equal(6, fixture.Probe.SeenConnections); // includes successful disposal of unopened strategy objects
        Assert.Equal(4, fixture.Probe.AttemptedConnections);
        Assert.Contains(fixture.Probe.Events, entry => entry.StartsWith("closed:", StringComparison.Ordinal));
        await fixture.AssertEffectsAsync([NoteWorkflow.Abandoned, NoteWorkflow.Abandoned], [2, 2], [1, 2]);
        var attempts = fixture.Probe.Attempts;
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(attempts, fixture.Probe.Attempts);
        fixture.Settings.CurrentValue = new SatiApiOptions { ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo", EnableNoteAbandonmentWorker = false };
        Assert.Equal(0, await fixture.Worker().RunDueAsync(CancellationToken.None));
        Assert.Equal(attempts, fixture.Probe.Attempts);
    }

    [SqlServerFact]
    public async Task PrecommitRecoverableFailureRollsBackClosesAndAllowsHealthyAgencyThenRetry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var worker = fixture.Worker();
        fixture.Audit.BeforeSave = () =>
        {
            Assert.Equal(2, fixture.Probe.Held);
            if (fixture.Audit.Saves == 1) throw new DbUpdateConcurrencyException("Synthetic precommit audit failure.");
            return Task.CompletedTask;
        };
        Assert.Equal(1, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(0, fixture.Probe.Held);
        Assert.Equal(2, fixture.Probe.Peak);
        await fixture.AssertEffectsAsync([NoteWorkflow.Pending, NoteWorkflow.Abandoned], [1, 2], [2]);
        fixture.Audit.BeforeSave = null;
        Assert.Equal(1, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(0, fixture.Probe.Held);
        Assert.Equal(2, fixture.Probe.Peak);
        await fixture.AssertEffectsAsync([NoteWorkflow.Abandoned, NoteWorkflow.Abandoned], [2, 2], [1, 2]);
    }

    [SqlServerFact]
    public async Task CancellationAtAuditBarrierRollsBackClosesAndLeavesBothAgenciesDue()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var worker = fixture.Worker();
        fixture.Audit.BeforeSave = () =>
        {
            Assert.Equal(2, fixture.Probe.Held);
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RunDueAsync(cancellation.Token));
        Assert.Equal(0, fixture.Probe.Held);
        Assert.Equal(2, fixture.Probe.Peak);
        await fixture.AssertEffectsAsync([NoteWorkflow.Pending, NoteWorkflow.Pending], [1, 1], []);
        fixture.Audit.BeforeSave = null;
        Assert.Equal(2, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(0, fixture.Probe.Held);
        await fixture.AssertEffectsAsync([NoteWorkflow.Abandoned, NoteWorkflow.Abandoned], [2, 2], [1, 2]);
    }

    [SqlServerFact]
    public async Task SecondCallerContentionIsCountedAndCannotEnterDiscoveryOrSweep()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.Worker();
        var second = fixture.Worker();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Audit.BeforeSave = async () => { entered.TrySetResult(); await release.Task; };
        var run = first.RunDueAsync(CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(2, fixture.Probe.Held);
            var created = fixture.Contexts.Connections.Count;
            Assert.Equal(0, await second.RunDueAsync(CancellationToken.None));
            Assert.Equal(created + 1, fixture.Contexts.Connections.Count); // its one session, no discovery/business context
            Assert.Equal(2, fixture.Probe.Held);
            Assert.Equal(3, fixture.Probe.Peak); // contending admission itself costs another connection
            Assert.Equal(1, fixture.Audit.Saves);
        }
        finally { release.TrySetResult(); await run.WaitAsync(TimeSpan.FromSeconds(30)); }
        Assert.Equal(2, await run);
        Assert.Equal(0, fixture.Probe.Held);
        await fixture.AssertEffectsAsync([NoteWorkflow.Abandoned, NoteWorkflow.Abandoned], [2, 2], [1, 2]);
    }

    [SqlServerFact]
    public async Task AdverseControlDetectsAnExtraHeldConnectionAtTheRealTransactionBarrier()
    {
        await using var fixture = await Fixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Audit.BeforeSave = async () => { entered.TrySetResult(); await release.Task; };
        var run = fixture.Worker().RunDueAsync(CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using (var extra = fixture.Contexts.CreateDbContext())
            {
                await extra.Database.OpenConnectionAsync();
                Assert.Equal(3, fixture.Probe.Held);
                Assert.Throws<EqualException>(() => Assert.Equal(2, fixture.Probe.Held));
                Assert.Equal(3, fixture.Probe.Peak);
            }
            Assert.Equal(2, fixture.Probe.Held);
        }
        finally { release.TrySetResult(); await run.WaitAsync(TimeSpan.FromSeconds(30)); }
        Assert.Equal(2, await run);
        Assert.Equal(0, fixture.Probe.Held);
        await fixture.AssertEffectsAsync([NoteWorkflow.Abandoned, NoteWorkflow.Abandoned], [2, 2], [1, 2]);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public SyntheticPipelineDatabase Database { get; } = new(sqlServer: true);
        public WorkerConnectionProbe Probe { get; } = new();
        public AuditBarrier Audit { get; } = new();
        public ContextFactory Contexts { get; private set; } = null!;
        public SettingsMonitor Settings { get; } = new();
        private readonly TimeProvider clock = new FrozenClock();
        private readonly List<int> noteIds = [];

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            try
            {
                await fixture.Database.InitializeAsync();
                // Setup/evidence use a different, uninstrumented factory so neither counts as worker cost.
                await using var db = new ApiDbContext(fixture.Database.Options());
                for (var index = 1; index <= 2; index++)
                {
                    var agency = new ServerAgency { Name = $"Synthetic lifetime agency {index}" };
                    db.Agencies.Add(agency); await db.SaveChangesAsync();
                    var user = new ServerUser { AgencyId = agency.Id, Username = $"synthetic-lifetime-{index}", Role = "CaseManager" };
                    db.Users.Add(user); await db.SaveChangesAsync();
                    var person = new ServerPerson { AgencyId = agency.Id, UserId = user.Id, FirstName = "Synthetic", LastName = "Lifetime" };
                    db.People.Add(person); await db.SaveChangesAsync();
                    var note = new ServerNote { AgencyId = agency.Id, PersonId = person.Id, Status = NoteWorkflow.Pending,
                        EventDate = new DateTime(2026, 8, 1), Narrative = "Synthetic lifetime note", Minutes = 15 };
                    db.Notes.Add(note); await db.SaveChangesAsync(); fixture.noteIds.Add(note.Id);
                }
                fixture.Contexts = new ContextFactory(fixture.Database.Options(fixture.Probe, fixture.Audit));
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public NoteAbandonmentWorker Worker()
        {
            var options = Options.Create(Settings.CurrentValue);
            var apiClock = new ApiClock(options, clock);
            return new NoteAbandonmentWorker(Contexts, new NoteAbandonmentSweep(Contexts, apiClock),
                new SqlNoteAbandonmentCoordination(Contexts, options), Settings, apiClock, clock,
                NullLogger<NoteAbandonmentWorker>.Instance);
        }

        public async Task AssertEffectsAsync(int[] statuses, int[] revisions, int[] auditAgencies)
        {
            await using var evidence = new ApiDbContext(Database.Options());
            var notes = await evidence.Notes.OrderBy(row => row.Id).Select(row => new { row.Id, row.Status, row.Revision }).ToListAsync();
            Assert.Equal(noteIds, notes.Select(row => row.Id));
            Assert.Equal(statuses, notes.Select(row => row.Status!.Value));
            Assert.Equal(revisions, notes.Select(row => row.Revision));
            Assert.Equal(auditAgencies, await evidence.AuditEvents.Where(row => row.Action == AuditActions.NoteAbandonedBySystem)
                .OrderBy(row => row.AgencyId).Select(row => row.AgencyId).ToArrayAsync());
        }
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class ContextFactory(DbContextOptions<ApiDbContext> options) : IDbContextFactory<ApiDbContext>
    {
        public List<DbConnection> Connections { get; } = [];
        public ApiDbContext CreateDbContext()
        {
            var db = new ApiDbContext(options); Connections.Add(db.Database.GetDbConnection()); return db;
        }
        public Task<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(CreateDbContext()); }
    }
    private sealed class AuditBarrier : SaveChangesInterceptor
    {
        public int Saves { get; private set; }
        public Func<Task>? BeforeSave { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ServerAuditEvent>().Any(row => row.State == EntityState.Added))
            { Saves++; if (BeforeSave is { } callback) await callback(); }
            return result;
        }
    }
    private sealed class SettingsMonitor : IOptionsMonitor<SatiApiOptions>
    {
        public SatiApiOptions CurrentValue { get; set; } = new() { ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiDemo", EnableNoteAbandonmentWorker = true };
        public SatiApiOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<SatiApiOptions, string?> listener) => null;
    }
    private sealed class FrozenClock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero); }
}
