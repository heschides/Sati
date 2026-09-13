using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection("Synthetic pipeline SQL Server")]
public sealed class BillingSubmissionSqlServerConcurrencyTests
{
    [SqlServerFact]
    public async Task SubmissionAndGenerationCannotOvertakeAnAlreadyValidatedClaimInsertion()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        var pause = new PauseClaimInsertion();
        await using var host = new SyntheticPipelineFactory(database, null, pause);
        var actors = await host.SeedAsync();
        await using var otherHost = new SyntheticPipelineFactory(database, host.Vault);
        using var addingBiller = await host.SignInAsync("synthetic-biller");
        using var submittingBiller = await otherHost.SignInAsync("synthetic-biller");
        int secondNoteId;
        int periodId;
        await using (var seed = host.OpenDatabase())
        {
            var first = Approved(actors.FirstPersonId, 120);
            var second = Approved(actors.SecondPersonId, 180);
            seed.Notes.AddRange(first, second);
            await seed.SaveChangesAsync();
            secondNoteId = second.Id;
            using var initial = await addingBiller.PostAsJsonAsync("/api/v1/billing/claim-lines",
                new CreateClaimLineRequest(first.Id, false, null));
            initial.EnsureSuccessStatusCode();
            periodId = (await initial.Content.ReadFromJsonAsync<ClaimLineDto>())!.BillingPeriodId;
        }

        pause.Armed = true;
        var insertion = addingBiller.PostAsJsonAsync("/api/v1/billing/claim-lines", new CreateClaimLineRequest(secondNoteId, false, null));
        Task<HttpResponseMessage>? submission = null;
        EdiFileDto? generated = null;
        try
        {
            await pause.Paused.Task.WaitAsync(TimeSpan.FromSeconds(15));
            submission = submittingBiller.PostAsync($"/api/v1/billing/periods/{periodId}/submit", null);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!submission.IsCompleted && !await database.HasWaitingApplicationLockAsync())
                await Task.Delay(25, timeout.Token);
            if (submission.IsCompleted)
            {
                var earlySubmission = await submission;
                if (earlySubmission.IsSuccessStatusCode)
                    generated = await GenerateAsync(submittingBiller, periodId);
            }
        }
        finally { pause.Release.TrySetResult(); }

        using var inserted = await insertion.WaitAsync(TimeSpan.FromSeconds(30));
        using var submitted = await submission!.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(inserted.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var submittedPeriod = (await submitted.Content.ReadFromJsonAsync<BillingPeriodDto>())!;
        generated ??= await GenerateAsync(submittingBiller, periodId);
        await using var final = host.OpenDatabase();
        var retained = await final.BillingPeriods.Include(x => x.Lines).SingleAsync();
        Assert.Equal(1, retained.Status);
        Assert.Equal(retained.Lines.Count, submittedPeriod.Lines.Count);
        Assert.Equal(retained.Lines.Count, ClaimResponseReader.ReadSubmission(generated.Content).Claims.Count);
        Assert.Equal(inserted.IsSuccessStatusCode ? 2 : 1, retained.Lines.Count);
        Assert.Single(await final.EdiGenerations.ToListAsync());

        ServerNote Approved(int personId, int start) => new()
        {
            AgencyId = actors.AgencyId, PersonId = personId, Narrative = "Synthetic submission race fixture.",
            Status = 6, EventDate = DateTime.Today, Minutes = 60, StartTime = start,
            ApprovedById = actors.SupervisorId, ApprovedAt = DateTime.UtcNow
        };
    }

    private static async Task<EdiFileDto> GenerateAsync(HttpClient biller, int periodId)
    {
        using var response = await biller.PostAsJsonAsync($"/api/v1/billing/periods/{periodId}/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")));
        Assert.True(response.IsSuccessStatusCode, $"Synthetic generation failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<EdiFileDto>())!;
    }

    private sealed class PauseClaimInsertion : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is ApiDbContext db &&
                db.ChangeTracker.Entries<ServerClaimLine>().Any(x => x.State == EntityState.Added))
            {
                Paused.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }
}
