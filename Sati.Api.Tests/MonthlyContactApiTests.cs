using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models;
using Xunit;

namespace Sati.Api.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class MonthlyContactApiCollection : ICollectionFixture<SatiApiFactory>
{
    // A private API and database: this collection enforces monthly contact from a past
    // date, which would change every other suite's billing results.
    public const string CollectionName = "Monthly contact API";
}

[Collection(MonthlyContactApiCollection.CollectionName)]
public sealed class MonthlyContactApiTests(SatiApiFactory factory)
{
    private static readonly DateTime Effective = DateTime.Today.AddDays(-100);

    [Fact]
    public async Task ServiceAfterThirtyDaysWithoutContactIsBlockedUntilTheNextContact()
    {
        var personId = await PrepareAsync();
        int onTimeId, lateId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var next = await db.Notes.MaxAsync(note => note.Id) + 1;
            onTimeId = next + 2;
            lateId = next + 3;
            db.Notes.AddRange(
                Note(next, personId, Effective.AddDays(10), NoteWorkflow.Logged, "Visit"),
                // Scheduled work has not happened and must not restart the clock.
                Note(next + 1, personId, Effective.AddDays(45), NoteWorkflow.Scheduled, "Visit"),
                Note(onTimeId, personId, Effective.AddDays(40), NoteWorkflow.Logged, "Other"),
                Note(lateId, personId, Effective.AddDays(50), NoteWorkflow.Logged, "Other"));
            await db.SaveChangesAsync();
        }

        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var page = await supervisor.GetFromJsonAsync<NoteReviewPage<NoteDto>>(
            $"/api/v1/supervisor/notes/page?personId={personId}");

        var onTime = Assert.Single(page!.Notes, note => note.Id == onTimeId);
        var late = Assert.Single(page.Notes, note => note.Id == lateId);
        Assert.Empty(onTime.ComplianceFailureReasons!);
        Assert.Contains(late.ComplianceFailureReasons!,
            reason => reason.StartsWith("Monthly contact was due", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AContactBeingLoggedCountsTowardItsOwnServiceDate()
    {
        var personId = await PrepareAsync();
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        // Sixty days after the effective date with no contact: other work is refused.
        var other = await caseManager.PostAsJsonAsync("/api/v1/notes",
            Logged(personId, Effective.AddDays(60), "Other"));
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Contains("Monthly contact", await other.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // The phone call itself is the contact, so it is billable on its own date.
        var phone = await caseManager.PostAsJsonAsync("/api/v1/notes",
            Logged(personId, Effective.AddDays(61), "Phone"));
        Assert.Equal(HttpStatusCode.OK, phone.StatusCode);

        // And it restarts the clock for the next day's work.
        var after = await caseManager.PostAsJsonAsync("/api/v1/notes",
            Logged(personId, Effective.AddDays(62), "Other"));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task ChangingAContactNoteToOtherWorkStopsItCounting()
    {
        var personId = await PrepareAsync();
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        // A pending visit happened and counts, but is still editable; a logged note is locked.
        var visit = await caseManager.PostAsJsonAsync("/api/v1/notes",
            Logged(personId, Effective.AddDays(70), "Visit") with { Status = "Pending" });
        Assert.Equal(HttpStatusCode.OK, visit.StatusCode);
        var saved = (await visit.Content.ReadFromJsonAsync<NoteDto>())!;

        var changed = await caseManager.PutAsJsonAsync($"/api/v1/notes/{saved.Id}",
            Logged(personId, Effective.AddDays(70), "Other") with
            {
                ExpectedRevision = saved.Revision
            });

        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Contains("Monthly contact", await changed.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    private async Task<int> PrepareAsync()
    {
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var person = await db.People.SingleAsync(item => item.Id == personId);
        person.EffectiveDate = Effective;
        if (!await db.BillingCompliancePolicyVersions.AnyAsync(version =>
                version.AgencyId == 1 &&
                version.Requirements == BillingComplianceRequirements.MonthlyContact))
        {
            db.BillingCompliancePolicyVersions.Add(BillingCompliancePolicyVersion.Create(
                1,
                BillingComplianceRequirements.MonthlyContact,
                Effective.AddDays(-100),
                Effective.AddDays(-100),
                11,
                DateTime.UtcNow,
                changeId: Guid.NewGuid()));
        }
        await db.SaveChangesAsync();
        return personId;
    }

    private static ServerNote Note(int id, int personId, DateTime date, int status, string type)
    {
        ContractMapper.TryParseNoteType(type, out var noteType);
        return new ServerNote
        {
            Id = id,
            PersonId = personId,
            AgencyId = 1,
            Narrative = "Synthetic monthly contact note",
            EventDate = date,
            Minutes = 15,
            Status = status,
            NoteType = noteType
        };
    }

    private static SaveNoteRequest Logged(int personId, DateTime date, string noteType) => new(
        "Synthetic monthly contact note.",
        date,
        "Logged",
        15,
        null,
        personId,
        null,
        noteType,
        null,
        null,
        GoalProgress: "None");
}
