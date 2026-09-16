using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// Real SQL Server locks, real HTTP requests, no shared process mutex. The first
/// writer is paused after validation and before saving; the second must either
/// reach the old unsafe save or visibly wait on SQL's application lock. Thus a
/// scheduler that happens to run requests serially cannot make this proof pass.
/// </summary>
[Collection("Synthetic pipeline SQL Server")]
public sealed class ServiceTimeSqlServerConcurrencyTests
{
    [SqlServerFact]
    public async Task SimultaneousCreatesForDifferentConsumersCannotReserveTheSameCaseManagerMinute()
        => await RaceAsync(editExisting: false);

    [SqlServerFact]
    public async Task SimultaneousCrossDateMovesCannotReserveTheSameCaseManagerMinute()
        => await RaceAsync(editExisting: true);

    private static async Task RaceAsync(bool editExisting)
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        var gate = new PauseFirstNoteSave();
        await using var factory = new SyntheticPipelineFactory(database, null, gate);
        var actors = await factory.SeedAsync();
        await using var otherHost = new SyntheticPipelineFactory(database, factory.Vault, gate);
        using var firstClient = await factory.SignInAsync("synthetic-author");
        using var secondClient = await otherHost.SignInAsync("synthetic-author");
        NoteDto? firstDraft = null;
        NoteDto? secondDraft = null;
        if (editExisting)
        {
            firstDraft = await CreateAsync(firstClient, Request(actors.FirstPersonId, DateTime.Today.AddDays(-2), 60));
            secondDraft = await CreateAsync(firstClient, Request(actors.SecondPersonId, DateTime.Today.AddDays(-1), 60));
        }

        gate.Arm();
        var first = SaveAsync(firstClient, actors.FirstPersonId, firstDraft);
        Task<HttpResponseMessage>? second = null;
        try
        {
            await gate.FirstPaused.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = SaveAsync(secondClient, actors.SecondPersonId, secondDraft);
            await WaitForSecondAttemptAsync(database, gate, second);
        }
        finally { gate.Release.TrySetResult(); }

        using var firstResponse = await first.WaitAsync(TimeSpan.FromSeconds(30));
        using var secondResponse = await second!.WaitAsync(TimeSpan.FromSeconds(30));
        var statuses = new[] { firstResponse.StatusCode, secondResponse.StatusCode };
        Assert.Single(statuses, x => x == HttpStatusCode.OK);
        Assert.Single(statuses, x => x == HttpStatusCode.Conflict);

        await using var db = factory.OpenDatabase();
        var claimed = await db.Notes.Where(x => x.EventDate == DateTime.Today && x.StartTime == 120).ToListAsync();
        Assert.Single(claimed);
        Assert.Equal(editExisting ? 2 : 1, await db.Notes.CountAsync());
        if (editExisting)
        {
            Assert.Single(await db.Notes.Where(x => x.EventDate < DateTime.Today).ToListAsync());
            Assert.Single(await db.Notes.Where(x => x.Revision == 2).ToListAsync());
            Assert.Single(await db.Notes.Where(x => x.Revision == 1).ToListAsync());
        }

        Task<HttpResponseMessage> SaveAsync(HttpClient client, int personId, NoteDto? draft) => draft is null
            ? client.PostAsJsonAsync("/api/v1/notes", Request(personId, DateTime.Today, 120))
            : client.PutAsJsonAsync($"/api/v1/notes/{draft.Id}",
                Request(personId, DateTime.Today, 120) with { ExpectedRevision = draft.Revision });
    }

    [SqlServerFact]
    public async Task AdjacentServicesAndDifferentCaseManagersRemainAllowed()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database);
        var actors = await factory.SeedAsync();
        using var author = await factory.SignInAsync("synthetic-author");
        using var other = await factory.SignInAsync("synthetic-other-author");
        await CreateAsync(author, Request(actors.FirstPersonId, DateTime.Today, 120));
        await CreateAsync(author, Request(actors.SecondPersonId, DateTime.Today, 180));
        await CreateAsync(other, Request(actors.OtherAuthorPersonId, DateTime.Today, 120));
        await using var db = factory.OpenDatabase();
        Assert.Equal(3, await db.Notes.CountAsync());
    }

    private static async Task WaitForSecondAttemptAsync(SyntheticPipelineDatabase database,
        PauseFirstNoteSave gate, Task<HttpResponseMessage> second)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!gate.SecondReachedSave.Task.IsCompleted)
        {
            if (await database.HasWaitingApplicationLockAsync()) return;
            if (second.IsCompleted && !gate.SecondReachedSave.Task.IsCompleted)
            {
                using var response = await second;
                Assert.Fail($"Second request ended ({response.StatusCode}) before either reaching the save or waiting on SQL's lock.");
            }
            await Task.Delay(25, timeout.Token);
        }
    }

    private static SaveNoteRequest Request(int personId, DateTime date, int start) => new(
        "Synthetic service-time race proof.", date, "Pending", 60, start, personId, null, "Contact", null, null);

    private static async Task<NoteDto> CreateAsync(HttpClient client, SaveNoteRequest request)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/notes", request);
        Assert.True(response.IsSuccessStatusCode, $"Synthetic draft create failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<NoteDto>())!;
    }

    private sealed class PauseFirstNoteSave : SaveChangesInterceptor
    {
        private int _armed;
        private int _attempts;
        public TaskCompletionSource FirstPaused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondReachedSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Volatile.Write(ref _armed, 1);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) != 1 || eventData.Context is not ApiDbContext db ||
                !db.ChangeTracker.Entries<ServerNote>().Any(x => x.State is EntityState.Added or EntityState.Modified))
                return result;
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                FirstPaused.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            else SecondReachedSave.TrySetResult();
            return result;
        }
    }
}

[CollectionDefinition("Synthetic pipeline SQL Server", DisableParallelization = true)]
public sealed class SyntheticPipelineSqlServerCollection;
