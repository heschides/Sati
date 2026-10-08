using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Reflection;
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
    public async Task AgencyDiscoveryMaterializesAtMostOneHundredIdsAndVisitsEveryStableAgencyOnce()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory
        {
            ClockOverride = clock, DatabaseCommandInterceptor = probe
        };
        var noteByAgency = await SeedAgencyNotesAsync(factory, 251);
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination());
        probe.Armed = true;

        Assert.Equal(251, await worker.RunDueAsync(CancellationToken.None));
        Assert.All(noteByAgency.Keys, agencyId => Assert.Equal(1, probe.Attempts(agencyId)));
        var audits = await factory.GetAuditEventsAsync("note.abandoned-by-system");
        Assert.Equal(251, audits.Count);
        foreach (var (agencyId, noteId) in noteByAgency)
        {
            var audit = Assert.Single(audits, item => item.AgencyId == agencyId);
            Assert.Equal(SystemActor.UserId, audit.ActorUserId);
            Assert.Equal([noteId], NoteIds(audit.MetadataJson));
        }
        await AssertNotesAsync(factory, noteByAgency.Values, NoteWorkflow.Abandoned, revision: 2);
        Assert.NotEmpty(probe.AgencyPages);
        Assert.All(probe.AgencyPages, page => Assert.InRange(page.Count, 0, 100));
        Assert.Equal(noteByAgency.Keys.Order(), probe.AgencyPages.SelectMany(page => page));

        var completedPageCount = probe.AgencyPages.Count;
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(completedPageCount, probe.AgencyPages.Count);
        Assert.All(noteByAgency.Keys, agencyId => Assert.Equal(1, probe.Attempts(agencyId)));
        Assert.Equal(251, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);
    }

    [Fact]
    public async Task RecoverableFaultAndExactNoteLimitStayDueWhileLaterPagesAndCompletedAgenciesProgress()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory { ClockOverride = clock, DatabaseCommandInterceptor = probe };
        var noteByAgency = await SeedAgencyNotesAsync(factory, 251);
        var extraNoteIds = Enumerable.Range(30_000, 99).ToArray();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.Notes.AddRange(extraNoteIds.Select(id => new ServerNote
            {
                Id = id, AgencyId = 2, PersonId = 10_002, Status = NoteWorkflow.Pending,
                EventDate = new DateTime(2026, 8, 3), Narrative = "Synthetic bounded batch note", Minutes = 15
            }));
            await db.SaveChangesAsync();
        }
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination());
        probe.Armed = true;
        probe.AuditFailure = id => id == 1 ? new DbUpdateConcurrencyException("Synthetic recoverable fault.") : null;

        Assert.Equal(349, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(noteByAgency[1]));
        Assert.Equal((NoteWorkflow.Abandoned, 2), await factory.GetNoteStateAsync(noteByAgency[251]));
        Assert.All(noteByAgency.Keys, id => Assert.Equal(1, probe.Attempts(id)));
        Assert.Equal([100, 100, 51], probe.AgencyPages.Select(page => page.Count));
        var audits = await factory.GetAuditEventsAsync("note.abandoned-by-system");
        Assert.Equal(250, audits.Count);
        Assert.DoesNotContain(audits, audit => audit.AgencyId == 1);
        Assert.Equal(new[] { noteByAgency[2] }.Concat(extraNoteIds),
            NoteIds(Assert.Single(audits, audit => audit.AgencyId == 2).MetadataJson));
        await AssertNotesAsync(factory, extraNoteIds, NoteWorkflow.Abandoned, revision: 2);

        clock.Instant = clock.Instant.AddHours(1);
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, probe.Attempts(1));
        Assert.Equal(2, probe.Attempts(2));
        Assert.All(noteByAgency.Keys.Where(id => id > 2), id => Assert.Equal(1, probe.Attempts(id)));
        Assert.Equal(250, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);

        probe.AuditFailure = null;
        clock.Instant = clock.Instant.AddHours(1);
        Assert.Equal(1, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(3, probe.Attempts(1));
        Assert.Equal(2, probe.Attempts(2));
        Assert.All(noteByAgency.Keys.Where(id => id > 2), id => Assert.Equal(1, probe.Attempts(id)));
        Assert.Equal(251, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);
        Assert.All(probe.AgencyPages, page => Assert.InRange(page.Count, 0, 100));
        var completedPageCount = probe.AgencyPages.Count;
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(completedPageCount, probe.AgencyPages.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisablementOrCancellationBetweenPagesLeavesLaterWorkDue(bool cancel)
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory { ClockOverride = clock, DatabaseCommandInterceptor = probe };
        var noteByAgency = await SeedAgencyNotesAsync(factory, 251);
        var settings = new TestOptionsMonitor(new SatiApiOptions { EnableNoteAbandonmentWorker = true });
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination(), settings);
        var enteredPage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        probe.Armed = true;
        probe.BeforeAgencyPage = async (page, token) =>
        {
            if (page != 2) return;
            enteredPage.TrySetResult();
            await releasePage.Task.WaitAsync(token);
        };
        var run = worker.RunDueAsync(cancellation.Token);
        try
        {
            await enteredPage.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.All(noteByAgency.Keys.Where(id => id <= 100), id => Assert.Equal(1, probe.Attempts(id)));
            Assert.All(noteByAgency.Keys.Where(id => id > 100), id => Assert.Equal(0, probe.Attempts(id)));
            if (cancel) cancellation.Cancel();
            else settings.CurrentValue = new SatiApiOptions { EnableNoteAbandonmentWorker = false };
            releasePage.TrySetResult();
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            else Assert.Equal(100, await run);
        }
        finally
        {
            releasePage.TrySetResult();
            cancellation.Cancel();
            try { await run; } catch (OperationCanceledException) { }
        }
        Assert.All(noteByAgency.Keys.Where(id => id > 100), id => Assert.Equal(0, probe.Attempts(id)));
        Assert.Equal(100, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);
        await AssertNotesAsync(factory, noteByAgency.Where(item => item.Key <= 100).Select(item => item.Value),
            NoteWorkflow.Abandoned, revision: 2);
        await AssertNotesAsync(factory, noteByAgency.Where(item => item.Key > 100).Select(item => item.Value),
            NoteWorkflow.Pending, revision: 1);

        probe.BeforeAgencyPage = null;
        settings.CurrentValue = new SatiApiOptions { EnableNoteAbandonmentWorker = true };
        clock.Instant = clock.Instant.AddHours(1);
        Assert.Equal(151, await worker.RunDueAsync(CancellationToken.None));
        Assert.All(noteByAgency.Keys, id => Assert.Equal(1, probe.Attempts(id)));
        Assert.Equal(251, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);
        Assert.All(probe.AgencyPages, page => Assert.InRange(page.Count, 0, 100));
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(0)]
    public async Task InitialNullableCursorPreservesDamagedNonpositiveAgencyFailure(int damagedId)
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory { ClockOverride = clock, DatabaseCommandInterceptor = probe };
        var noteByAgency = await SeedAgencyNotesAsync(factory, 2);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"Agencies\" (\"Id\", \"Name\") VALUES ({damagedId}, {"Synthetic damaged agency"})");
        }
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination());
        probe.Armed = true;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(damagedId, Assert.Single(probe.AgencyPages)[0]);
        Assert.All(noteByAgency.Keys, id => Assert.Equal(0, probe.Attempts(id)));
        await AssertNotesAsync(factory, noteByAgency.Values, NoteWorkflow.Pending, revision: 1);
        Assert.Empty(await factory.GetAuditEventsAsync("note.abandoned-by-system"));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await db.Agencies.Where(agency => agency.Id == damagedId).ExecuteDeleteAsync();
        }
        Assert.Equal(2, await worker.RunDueAsync(CancellationToken.None));
        Assert.All(noteByAgency.Keys, id => Assert.Equal(1, probe.Attempts(id)));
    }

    [Fact]
    public async Task CapturedRangeExcludesHigherGrowthAndTheNextPassVisitsItWithoutRepeatingCompletedAgencies()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory { ClockOverride = clock, DatabaseCommandInterceptor = probe };
        var noteByAgency = await SeedAgencyNotesAsync(factory, 251);
        const int addedAgencyId = 999;
        var addedNoteId = 20_000 + addedAgencyId;
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination());
        probe.Armed = true;
        probe.BeforeAgencyPage = async (page, _) =>
        {
            if (page != 1) return;
            Assert.Equal(1, probe.AgencyRangeQueries);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            AddAgencyNote(db, addedAgencyId);
            await db.SaveChangesAsync();
        };

        Assert.Equal(251, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(noteByAgency.Keys.Order(), probe.AgencyPages.SelectMany(page => page));
        Assert.Equal(0, probe.Attempts(addedAgencyId));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(addedNoteId));
        Assert.Equal(1, probe.AgencyGrowthQueries);
        probe.BeforeAgencyPage = null;
        clock.Instant = clock.Instant.AddHours(1);
        Assert.Equal(1, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(1, probe.Attempts(addedAgencyId));
        Assert.All(noteByAgency.Keys, id => Assert.Equal(1, probe.Attempts(id)));
        Assert.Equal((NoteWorkflow.Abandoned, 2), await factory.GetNoteStateAsync(addedNoteId));
        Assert.Equal(252, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);
        Assert.All(probe.AgencyPages, page => Assert.InRange(page.Count, 0, 100));
        Assert.Equal(2, probe.AgencyGrowthQueries);
        var rangeQueries = probe.AgencyRangeQueries;
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(rangeQueries, probe.AgencyRangeQueries);
    }

    [Fact]
    public async Task InitiallyEmptyRangeUsesAnyAgencyGrowthCheckAndLeavesObservedInsertionDue()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe { Armed = true };
        await using var database = new SyntheticPipelineDatabase();
        await database.InitializeAsync();
        var contexts = new OwnedFactory(database.Options(probe));
        var apiClock = new ApiClock(Options.Create(new SatiApiOptions()), clock);
        var worker = new NoteAbandonmentWorker(contexts, new NoteAbandonmentSweep(contexts, apiClock),
            new InlineCoordination(), new TestOptionsMonitor(new SatiApiOptions { EnableNoteAbandonmentWorker = true }),
            apiClock, clock, NullLogger<NoteAbandonmentWorker>.Instance);
        probe.BeforeAgencyGrowth = async _ =>
        {
            probe.BeforeAgencyGrowth = null;
            Assert.Equal(1, probe.AgencyRangeQueries);
            Assert.Empty(probe.AgencyPages);
            await using var db = await contexts.CreateDbContextAsync();
            db.Agencies.Add(new ServerAgency { Id = 1, Name = "Synthetic agency arriving after empty range" });
            await db.SaveChangesAsync();
        };

        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Empty(probe.AgencyPages);
        Assert.Equal(1, probe.AgencyGrowthQueries);
        Assert.Equal(0, probe.Attempts(1));
        clock.Instant = clock.Instant.AddHours(1);
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal([1], Assert.Single(probe.AgencyPages));
        Assert.Equal(1, probe.Attempts(1));
        Assert.Equal(2, probe.AgencyGrowthQueries);
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, probe.AgencyRangeQueries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedRangeOrGrowthFailurePropagatesAndCannotCacheGlobalCompletion(bool atGrowthCheck)
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory { ClockOverride = clock, DatabaseCommandInterceptor = probe };
        var noteByAgency = await SeedAgencyNotesAsync(factory, 2);
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination());
        var error = new DbUpdateConcurrencyException("Synthetic shared discovery fault.");
        probe.Armed = true;
        if (atGrowthCheck) probe.AgencyGrowthFailure = error;
        else probe.AgencyRangeFailure = error;

        Assert.Same(error, await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => worker.RunDueAsync(CancellationToken.None)));
        Assert.All(noteByAgency.Keys, id => Assert.Equal(atGrowthCheck ? 1 : 0, probe.Attempts(id)));
        Assert.Equal(atGrowthCheck ? 2 : 0, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);
        probe.AgencyRangeFailure = null;
        probe.AgencyGrowthFailure = null;
        Assert.Equal(atGrowthCheck ? 0 : 2, await worker.RunDueAsync(CancellationToken.None));
        Assert.All(noteByAgency.Keys, id => Assert.Equal(1, probe.Attempts(id)));
        Assert.Equal(2, probe.AgencyRangeQueries);
        Assert.Equal(2, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, probe.AgencyRangeQueries);
    }

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
    public async Task RecoverableAuditFailureRollsBackAgencyAAndAllowsBThenRetriesOnlyAOnLaterPass()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory
        {
            ClockOverride = clock, DatabaseCommandInterceptor = probe
        };
        await RemoveSeededPendingNotesAsync(factory);
        var agencyA = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending);
        var agencyB = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending, personId: 201);
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination());
        probe.Armed = true;
        probe.AuditFailure = agencyId => agencyId == 1
            ? new DbUpdateConcurrencyException("Synthetic agency-local concurrency failure.") : null;

        Assert.Equal(1, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(1, probe.Attempts(1));
        Assert.Equal(1, probe.Attempts(2));
        Assert.Equal(1, probe.AuditFailures);
        Assert.Equal(1, probe.GuardedUpdates(1));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyA));
        Assert.Equal((NoteWorkflow.Abandoned, 2), await factory.GetNoteStateAsync(agencyB));
        var firstAudit = Assert.Single(await factory.GetAuditEventsAsync("note.abandoned-by-system"));
        Assert.Equal(2, firstAudit.AgencyId);
        Assert.Equal(SystemActor.UserId, firstAudit.ActorUserId);
        Assert.Equal([agencyB], NoteIds(firstAudit.MetadataJson));

        // A recurring fault is attempted once on each existing pass, without redoing B.
        clock.Instant = clock.Instant.AddHours(1);
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, probe.Attempts(1));
        Assert.Equal(1, probe.Attempts(2));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyA));
        Assert.Single(await factory.GetAuditEventsAsync("note.abandoned-by-system"));

        probe.AuditFailure = null;
        clock.Instant = clock.Instant.AddHours(1);
        Assert.Equal(1, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(3, probe.Attempts(1));
        Assert.Equal(1, probe.Attempts(2));
        Assert.Equal((NoteWorkflow.Abandoned, 2), await factory.GetNoteStateAsync(agencyA));
        Assert.Equal((NoteWorkflow.Abandoned, 2), await factory.GetNoteStateAsync(agencyB));
        var audits = await factory.GetAuditEventsAsync("note.abandoned-by-system");
        Assert.Equal(2, audits.Count);
        Assert.Equal([agencyA], NoteIds(Assert.Single(audits, audit => audit.AgencyId == 1).MetadataJson));
        Assert.Equal([agencyB], NoteIds(Assert.Single(audits, audit => audit.AgencyId == 2).MetadataJson));
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(3, probe.Attempts(1));
        Assert.Equal(1, probe.Attempts(2));
    }

    [Theory]
    [InlineData("wrapped-concurrency")]
    [InlineData("deadlock")]
    [InlineData("retry-exhausted-deadlock")]
    public async Task ExplicitRecoverableAuditFaultsAllowHealthyAgencyToComplete(string fault)
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory
        {
            ClockOverride = clock, DatabaseCommandInterceptor = probe
        };
        await RemoveSeededPendingNotesAsync(factory);
        var agencyA = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending);
        var agencyB = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending, personId: 201);
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination());
        probe.Armed = true;
        probe.AuditFailure = agencyId => agencyId == 1 ? Fault(fault) : null;

        Assert.Equal(1, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(1, probe.Attempts(1));
        Assert.Equal(1, probe.Attempts(2));
        Assert.Equal(1, probe.AuditFailures);
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyA));
        Assert.Equal((NoteWorkflow.Abandoned, 2), await factory.GetNoteStateAsync(agencyB));
        Assert.Equal([agencyB], NoteIds(Assert.Single(
            await factory.GetAuditEventsAsync("note.abandoned-by-system")).MetadataJson));
    }

    [Theory]
    [InlineData("database-update")]
    [InlineData("timeout")]
    [InlineData("sql-timeout")]
    [InlineData("sql-connection")]
    [InlineData("mixed-deadlock-timeout")]
    [InlineData("fatal-deadlock")]
    [InlineData("arbitrary-concurrency-wrapper")]
    public async Task UnknownFatalOrConnectionAuditFaultsPropagateAndDoNotCacheCompletion(string fault)
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory
        {
            ClockOverride = clock, DatabaseCommandInterceptor = probe
        };
        await RemoveSeededPendingNotesAsync(factory);
        var agencyA = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending);
        var agencyB = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending, personId: 201);
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination());
        probe.Armed = true;
        var injected = Fault(fault);
        probe.AuditFailure = agencyId => agencyId == 1 ? injected : null;

        var error = await Record.ExceptionAsync(() => worker.RunDueAsync(CancellationToken.None));
        Assert.NotNull(error);
        Assert.True(ContainsException(error, injected), "The original failure must propagate through EF's wrapper.");
        Assert.Equal(1, probe.AuditFailures);
        Assert.Equal(1, probe.Attempts(1));
        Assert.Equal(0, probe.Attempts(2));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyA));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyB));
        Assert.Empty(await factory.GetAuditEventsAsync("note.abandoned-by-system"));

        probe.AuditFailure = null;
        Assert.Equal(2, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, probe.Attempts(1));
        Assert.Equal(1, probe.Attempts(2));
        Assert.Equal(2, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);
    }

    [Fact]
    public async Task CancelledAgencySweepPropagatesAndRemainsDueForTheSameDay()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory
        {
            ClockOverride = clock, DatabaseCommandInterceptor = probe
        };
        await RemoveSeededPendingNotesAsync(factory);
        var agencyA = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending);
        var agencyB = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending, personId: 201);
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination());
        using var cancellation = new CancellationTokenSource();
        probe.Armed = true;
        probe.BeforeAudit = (_, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RunDueAsync(cancellation.Token));
        Assert.Equal(1, probe.GuardedUpdates(1));
        Assert.Equal(0, probe.Attempts(2));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyA));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyB));
        Assert.Empty(await factory.GetAuditEventsAsync("note.abandoned-by-system"));
        probe.BeforeAudit = null;
        Assert.Equal(2, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, probe.Attempts(1));
        Assert.Equal(1, probe.Attempts(2));
    }

    [Fact]
    public async Task CancellationRacingWithRecoverableFaultCannotBeSwallowed()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory
        {
            ClockOverride = clock, DatabaseCommandInterceptor = probe
        };
        await RemoveSeededPendingNotesAsync(factory);
        var agencyA = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending);
        var agencyB = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending, personId: 201);
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination());
        using var cancellation = new CancellationTokenSource();
        probe.Armed = true;
        probe.AuditFailure = _ =>
        {
            cancellation.Cancel();
            return new DbUpdateConcurrencyException("Synthetic concurrency fault racing cancellation.");
        };

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => worker.RunDueAsync(cancellation.Token));
        Assert.Equal(1, probe.AuditFailures);
        Assert.Equal(0, probe.Attempts(2));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyA));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyB));
        Assert.Empty(await factory.GetAuditEventsAsync("note.abandoned-by-system"));
        probe.AuditFailure = null;
        Assert.Equal(2, await worker.RunDueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SharedAgencyListFailurePropagatesEvenWithARecoverableExceptionType()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory
        {
            ClockOverride = clock, DatabaseCommandInterceptor = probe
        };
        await RemoveSeededPendingNotesAsync(factory);
        var agencyA = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending);
        var agencyB = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending, personId: 201);
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination());
        probe.Armed = true;
        probe.AgencyListFailure = new DbUpdateConcurrencyException("Synthetic shared list failure.");

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(1, probe.AgencyListQueries);
        Assert.Equal(0, probe.Attempts(1));
        Assert.Equal(0, probe.Attempts(2));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyA));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyB));
        Assert.Empty(await factory.GetAuditEventsAsync("note.abandoned-by-system"));
        probe.AgencyListFailure = null;
        Assert.Equal(2, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, probe.AgencyListQueries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoordinationFailureBeforeOrAfterSweepCannotCacheGlobalCompletion(bool afterSweep)
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        await using var factory = new SatiApiFactory { ClockOverride = clock };
        await RemoveSeededPendingNotesAsync(factory);
        var agencyA = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending);
        var agencyB = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending, personId: 201);
        var coordination = new FaultingCoordination(afterSweep);
        var worker = CreateWorker(factory, clock, enabled: true, coordination);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(1, coordination.Attempts);
        var expected = afterSweep ? (NoteWorkflow.Abandoned, 2) : (NoteWorkflow.Pending, 1);
        Assert.Equal(expected, await factory.GetNoteStateAsync(agencyA));
        Assert.Equal(expected, await factory.GetNoteStateAsync(agencyB));
        Assert.Equal(afterSweep ? 2 : 0,
            (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);
        coordination.Fail = false;
        Assert.Equal(afterSweep ? 0 : 2, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, coordination.Attempts);
        Assert.Equal(2, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, coordination.Attempts);
    }

    [Fact]
    public async Task SharedContextConnectionFailurePropagatesAndCanRecoverOnLaterPass()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        await using var factory = new SatiApiFactory { ClockOverride = clock };
        await RemoveSeededPendingNotesAsync(factory);
        var agencyA = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending);
        var agencyB = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending, personId: 201);
        var contexts = new FaultingContextFactory(factory.Services
            .GetRequiredService<IDbContextFactory<ApiDbContext>>());
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination(), contexts: contexts);

        await Assert.ThrowsAsync<SqlException>(() => worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(1, contexts.Attempts);
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyA));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyB));
        Assert.Empty(await factory.GetAuditEventsAsync("note.abandoned-by-system"));
        contexts.Fail = false;
        Assert.Equal(2, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, contexts.Attempts);
    }

    [Fact]
    public async Task ExactBatchLimitRemainsDueUntilLaterPassWhileHealthyAgencyIsSkipped()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory
        {
            ClockOverride = clock, DatabaseCommandInterceptor = probe
        };
        await RemoveSeededPendingNotesAsync(factory);
        int[] agencyA;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var firstId = await db.Notes.MaxAsync(note => note.Id) + 1;
            agencyA = Enumerable.Range(firstId, NoteAbandonmentSweep.WorkerBatchSize).ToArray();
            db.Notes.AddRange(agencyA.Select(id => new ServerNote
            {
                Id = id, AgencyId = 1, PersonId = 101, Status = NoteWorkflow.Pending,
                EventDate = new DateTime(2026, 8, 3), Narrative = "Synthetic batch note", Minutes = 15
            }));
            await db.SaveChangesAsync();
        }
        var agencyB = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending, personId: 201);
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination());
        probe.Armed = true;

        Assert.Equal(101, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(100, probe.GuardedUpdates(1));
        Assert.Equal((NoteWorkflow.Abandoned, 2), await factory.GetNoteStateAsync(agencyB));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var states = await db.Notes.AsNoTracking().Where(note => agencyA.Contains(note.Id)).ToListAsync();
            Assert.Equal(100, states.Count);
            Assert.All(states, note =>
            {
                Assert.Equal(NoteWorkflow.Abandoned, note.Status);
                Assert.Equal(2, note.Revision);
            });
        }
        var audits = await factory.GetAuditEventsAsync("note.abandoned-by-system");
        Assert.Equal(2, audits.Count);
        Assert.Equal(agencyA, NoteIds(Assert.Single(audits, audit => audit.AgencyId == 1).MetadataJson));
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, probe.Attempts(1));
        Assert.Equal(1, probe.Attempts(2));
        Assert.Equal(2, probe.AgencyListQueries);
        Assert.Equal(2, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(2, probe.AgencyListQueries);
    }

    [Fact]
    public async Task DisablingBeforeAgencyBTurnStopsBAndLeavesItDueWhenEnabledAgain()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var probe = new SweepCommandProbe();
        await using var factory = new SatiApiFactory
        {
            ClockOverride = clock, DatabaseCommandInterceptor = probe
        };
        await RemoveSeededPendingNotesAsync(factory);
        var agencyA = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending);
        var agencyB = await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending, personId: 201);
        var settings = new TestOptionsMonitor(new SatiApiOptions { EnableNoteAbandonmentWorker = true });
        var worker = CreateWorker(factory, clock, enabled: true, new InlineCoordination(), settings);
        var enteredAudit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAudit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runCancellation = new CancellationTokenSource();
        probe.Armed = true;
        probe.BeforeAudit = async (agencyId, token) =>
        {
            Assert.Equal(1, agencyId);
            enteredAudit.SetResult();
            await releaseAudit.Task.WaitAsync(token);
        };

        var run = worker.RunDueAsync(runCancellation.Token);
        try
        {
            await enteredAudit.Task.WaitAsync(TimeSpan.FromSeconds(15));
            settings.CurrentValue = new SatiApiOptions { EnableNoteAbandonmentWorker = false };
        }
        finally
        {
            releaseAudit.TrySetResult();
            if (!enteredAudit.Task.IsCompletedSuccessfully) runCancellation.Cancel();
            await run; // Drain before disposing the database, even if the barrier failed.
        }
        Assert.Equal(1, await run);
        Assert.Equal((NoteWorkflow.Abandoned, 2), await factory.GetNoteStateAsync(agencyA));
        Assert.Equal((NoteWorkflow.Pending, 1), await factory.GetNoteStateAsync(agencyB));
        Assert.Equal(1, probe.Attempts(1));
        Assert.Equal(0, probe.Attempts(2));
        Assert.Single(await factory.GetAuditEventsAsync("note.abandoned-by-system"));
        Assert.Equal(0, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(1, probe.AgencyListQueries);
        probe.BeforeAudit = null;
        settings.CurrentValue = new SatiApiOptions { EnableNoteAbandonmentWorker = true };
        Assert.Equal(1, await worker.RunDueAsync(CancellationToken.None));
        Assert.Equal(1, probe.Attempts(1));
        Assert.Equal(1, probe.Attempts(2));
        Assert.Equal((NoteWorkflow.Abandoned, 2), await factory.GetNoteStateAsync(agencyB));
        Assert.Equal(2, (await factory.GetAuditEventsAsync("note.abandoned-by-system")).Count);
    }

    private static bool ContainsException(Exception error, Exception expected) =>
        ReferenceEquals(error, expected) || error.InnerException is { } inner && ContainsException(inner, expected);

    private static Exception Fault(string kind) => kind switch
    {
        "wrapped-concurrency" => new DbUpdateException("Synthetic EF wrapper.",
            new DbUpdateConcurrencyException("Synthetic concurrency failure.")),
        "deadlock" => SqlFailure((1205, 13)),
        "retry-exhausted-deadlock" => new RetryLimitExceededException("Synthetic exhausted execution strategy.",
            new DbUpdateException("Synthetic EF wrapper.", SqlFailure((1205, 13)))),
        "database-update" => new DbUpdateException("Synthetic unknown database failure."),
        "timeout" => new TimeoutException("Synthetic timeout."),
        "sql-timeout" => SqlFailure((-2, 11)),
        "sql-connection" => SqlFailure((233, 20)),
        "mixed-deadlock-timeout" => SqlFailure((1205, 13), (-2, 11)),
        "fatal-deadlock" => SqlFailure((1205, 20)),
        "arbitrary-concurrency-wrapper" => new InvalidOperationException("Synthetic unknown wrapper.",
            new DbUpdateConcurrencyException("Synthetic concurrency failure.")),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    // Test the SQL fault classification without contacting SQL Server. These synthetic
    // exceptions prove continuation policy, not a real deadlock or provider retry cycle.
    private static SqlException SqlFailure(params (int Number, byte Severity)[] errors)
    {
        var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        var errorConstructor = typeof(SqlError).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null, [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string),
                typeof(string), typeof(int), typeof(Exception)], modifiers: null)!;
        var add = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var (number, severity) in errors)
        {
            var error = errorConstructor.Invoke([number, (byte)0, severity, "synthetic-server",
                "Synthetic SQL fault.", "synthetic-procedure", 0, null]);
            add.Invoke(collection, [error]);
        }
        var create = typeof(SqlException).GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static,
            binder: null, [typeof(SqlErrorCollection), typeof(string)], modifiers: null)!;
        return (SqlException)create.Invoke(null, [collection, "synthetic-version"])!;
    }

    private static async Task RemoveSeededPendingNotesAsync(SatiApiFactory factory)
    {
        using var client = await factory.CreateSeededAnonymousClientAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        await db.Notes.Where(note => note.Status == NoteWorkflow.Pending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(note => note.Status, NoteWorkflow.Logged));
    }

    private static async Task<Dictionary<int, int>> SeedAgencyNotesAsync(SatiApiFactory factory, int agencyCount)
    {
        await RemoveSeededPendingNotesAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.Agencies.AddRange(Enumerable.Range(3, agencyCount - 2)
            .Select(id => new ServerAgency { Id = id, Name = $"Synthetic agency {id}" }));
        var noteByAgency = Enumerable.Range(1, agencyCount).ToDictionary(id => id, id => 20_000 + id);
        foreach (var (agencyId, noteId) in noteByAgency)
            AddAgencyNote(db, agencyId, addAgency: false);
        await db.SaveChangesAsync();
        return noteByAgency;
    }

    private static void AddAgencyNote(ApiDbContext db, int agencyId, bool addAgency = true)
    {
        if (addAgency) db.Agencies.Add(new ServerAgency { Id = agencyId, Name = $"Synthetic agency {agencyId}" });
        var personId = 10_000 + agencyId;
        db.People.Add(new ServerPerson
        {
            Id = personId, UserId = 12, AgencyId = agencyId,
            FirstName = "Synthetic", LastName = $"Consumer {agencyId}"
        });
        db.Notes.Add(new ServerNote
        {
            Id = 20_000 + agencyId, AgencyId = agencyId, PersonId = personId, Status = NoteWorkflow.Pending,
            EventDate = new DateTime(2026, 8, 3), Narrative = "Synthetic discovery note", Minutes = 15
        });
    }

    private static async Task AssertNotesAsync(SatiApiFactory factory, IEnumerable<int> noteIds, int status, long revision)
    {
        var ids = noteIds.ToArray();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var notes = await db.Notes.AsNoTracking().Where(note => ids.Contains(note.Id)).ToListAsync();
        Assert.Equal(ids.Length, notes.Count);
        Assert.All(notes, note =>
        {
            Assert.Equal(status, note.Status);
            Assert.Equal(revision, note.Revision);
        });
    }

    private sealed class SweepCommandProbe : DbCommandInterceptor
    {
        private readonly Dictionary<int, int> attempts = [];
        private readonly Dictionary<int, int> updates = [];
        public bool Armed { get; set; }
        public Func<int, Exception?>? AuditFailure { get; set; }
        public Func<int, CancellationToken, Task>? BeforeAudit { get; set; }
        public Func<int, CancellationToken, Task>? BeforeAgencyPage { get; set; }
        public Func<CancellationToken, Task>? BeforeAgencyGrowth { get; set; }
        public Exception? AgencyListFailure { get; set; }
        public Exception? AgencyRangeFailure { get; set; }
        public Exception? AgencyGrowthFailure { get; set; }
        public int AgencyListQueries { get; private set; }
        public int AgencyRangeQueries { get; private set; }
        public int AgencyGrowthQueries { get; private set; }
        public List<List<int>> AgencyPages { get; } = [];
        public int AuditFailures { get; private set; }
        public int Attempts(int agencyId) => attempts.GetValueOrDefault(agencyId);
        public int GuardedUpdates(int agencyId) => updates.GetValueOrDefault(agencyId);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result,
            CancellationToken token = default)
        {
            if (Armed && IsAgencyPage(command))
            {
                AgencyListQueries++;
                if (AgencyListFailure is { } error) throw error;
                if (BeforeAgencyPage is not null) await BeforeAgencyPage(AgencyListQueries, token);
            }
            if (Armed && IsAgencyQuery(command) && command.CommandText.Contains("MAX(", StringComparison.Ordinal))
            {
                AgencyRangeQueries++;
                if (AgencyRangeFailure is { } error) throw error;
            }
            if (Armed && IsAgencyQuery(command) && command.CommandText.Contains("EXISTS", StringComparison.Ordinal))
            {
                AgencyGrowthQueries++;
                if (AgencyGrowthFailure is { } error) throw error;
                if (BeforeAgencyGrowth is not null) await BeforeAgencyGrowth(token);
            }
            if (Armed && command.CommandText.Contains("FROM \"Settings\"", StringComparison.Ordinal))
            {
                var agencyId = AgencyParameter(command);
                attempts[agencyId] = Attempts(agencyId) + 1;
            }
            if (Armed && command.CommandText.Contains("INSERT INTO \"AuditEvents\"", StringComparison.Ordinal))
            {
                var audit = data.Context!.ChangeTracker.Entries<ServerAuditEvent>()
                    .Single(entry => entry.State == EntityState.Added &&
                        entry.Entity.Action == "note.abandoned-by-system").Entity;
                if (BeforeAudit is not null) await BeforeAudit(audit.AgencyId, token);
                if (AuditFailure?.Invoke(audit.AgencyId) is { } error)
                {
                    AuditFailures++;
                    throw error;
                }
            }
            return await base.ReaderExecutingAsync(command, data, result, token);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData data, DbDataReader result, CancellationToken token = default)
        {
            if (!Armed || !IsAgencyPage(command)) return ValueTask.FromResult(result);
            var ids = new List<int>();
            AgencyPages.Add(ids);
            return ValueTask.FromResult<DbDataReader>(new ObservedAgencyReader(result, ids));
        }

        private static bool IsAgencyPage(DbCommand command) =>
            IsAgencyQuery(command) &&
            command.CommandText.Contains("ORDER BY", StringComparison.Ordinal);

        private static bool IsAgencyQuery(DbCommand command) =>
            command.CommandText.Contains("FROM \"Agencies\"", StringComparison.Ordinal);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData data, InterceptionResult<int> result,
            CancellationToken token = default)
        {
            if (Armed && command.CommandText.Contains("UPDATE \"Notes\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("\"Revision\"", StringComparison.Ordinal))
            {
                var agencyId = AgencyParameter(command);
                updates[agencyId] = GuardedUpdates(agencyId) + 1;
            }
            return base.NonQueryExecutingAsync(command, data, result, token);
        }

        private static int AgencyParameter(DbCommand command) => Convert.ToInt32(command.Parameters
            .Cast<DbParameter>().Single(parameter => parameter.ParameterName.Contains("agencyId", StringComparison.Ordinal)).Value);
    }

    // Counts rows actually handed to EF, rather than inferring bounded materialization from SQL text.
    private sealed class ObservedAgencyReader(DbDataReader inner, List<int> ids) : DbDataReader
    {
        private bool Observe(bool read) { if (read) ids.Add(inner.GetInt32(0)); return read; }
        public override bool Read() => Observe(inner.Read());
        public override async Task<bool> ReadAsync(CancellationToken cancellationToken) =>
            Observe(await inner.ReadAsync(cancellationToken));
        public override bool NextResult() => inner.NextResult();
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => inner.NextResultAsync(cancellationToken);
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, offset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, offset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
        public override DataTable? GetSchemaTable() => inner.GetSchemaTable();
        public override void Close() => inner.Close();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
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
        bool enabled, INoteAbandonmentCoordination coordination, IOptionsMonitor<SatiApiOptions>? settings = null,
        IDbContextFactory<ApiDbContext>? contexts = null)
    {
        var services = factory.Services;
        return new NoteAbandonmentWorker(
            contexts ?? services.GetRequiredService<IDbContextFactory<ApiDbContext>>(),
            services.GetRequiredService<NoteAbandonmentSweep>(),
            coordination,
            settings ?? new TestOptionsMonitor(new SatiApiOptions { EnableNoteAbandonmentWorker = enabled }),
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

    private sealed class FaultingCoordination(bool afterSweep) : INoteAbandonmentCoordination
    {
        public bool Fail { get; set; } = true;
        public int Attempts { get; private set; }
        public async Task<bool> RunOnceAsync(Func<CancellationToken, Task> sweep, CancellationToken token)
        {
            Attempts++;
            if (Fail && !afterSweep) throw new DbUpdateConcurrencyException("Synthetic coordination failure.");
            await sweep(token);
            if (Fail) throw new DbUpdateConcurrencyException("Synthetic coordination release failure.");
            return true;
        }
    }

    private sealed class FaultingContextFactory(IDbContextFactory<ApiDbContext> inner)
        : IDbContextFactory<ApiDbContext>
    {
        public bool Fail { get; set; } = true;
        public int Attempts { get; private set; }
        public ApiDbContext CreateDbContext()
        {
            Attempts++;
            if (Fail) throw SqlFailure((233, 20));
            return inner.CreateDbContext();
        }
        public ValueTask<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }

    private sealed class TestOptionsMonitor(SatiApiOptions options) : IOptionsMonitor<SatiApiOptions>
    {
        public SatiApiOptions CurrentValue { get; set; } = options;
        public SatiApiOptions Get(string? name) => CurrentValue;
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
