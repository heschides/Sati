using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class ConsumerPermissionRevocationTests(SatiApiFactory factory)
{
    [Theory]
    [InlineData("journal-read")]
    [InlineData("journal-write")]
    [InlineData("journal-entry")]
    [InlineData("note-write")]
    [InlineData("note-delete")]
    [InlineData("notes-year")]
    [InlineData("notes-abandon")]
    [InlineData("form-open")]
    [InlineData("person-status")]
    [InlineData("productivity-report")]
    [InlineData("consumer-loss-report")]
    public async Task RetainedAssignmentDoesNotAuthorizeRevokedCasework(string operation)
    {
        var data = await SeedAsync();
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var permissions = await GetPermissionsAsync(12);
        await factory.ChangeUserPermissionsAsync(12, UserPermissions.Billing);
        try
        {
            using var response = await SendAsync(client, operation, data);
            Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
                $"{operation} should deny revoked casework, received {response.StatusCode}.");
            await AssertUnchangedAsync(data);
            using var billing = await client.GetAsync("/api/v1/billing/periods");
            Assert.Equal(HttpStatusCode.OK, billing.StatusCode);
        }
        finally { await factory.ChangeUserPermissionsAsync(12, permissions); }
    }

    [Theory]
    [InlineData("journal-read")]
    [InlineData("journal-write")]
    [InlineData("journal-entry")]
    [InlineData("note-write")]
    [InlineData("note-delete")]
    [InlineData("form-open")]
    public async Task CurrentOwnerStillHasItsIntendedCaseworkOperations(string operation)
    {
        var data = await SeedAsync();
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var response = await SendAsync(client, operation, data);
        Assert.True(response.IsSuccessStatusCode,
            $"{operation}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        if (operation == "note-delete")
            Assert.False(await db.Notes.AnyAsync(x => x.Id == data.NoteId));
        if (operation == "note-write")
            Assert.Equal("Changed", await db.Notes.Where(x => x.Id == data.NoteId).Select(x => x.Narrative).SingleAsync());
        if (operation == "form-open")
            Assert.Equal(DateTime.Today, await db.Forms.Where(x => x.Id == data.FormId).Select(x => x.OpenedDate).SingleAsync());
        if (operation is "journal-write" or "journal-entry")
            Assert.Contains("Changed", await db.People.Where(x => x.Id == data.PersonId).Select(x => x.Journal).SingleAsync());
    }

    [Fact]
    public async Task RetainedBillingCanCreateClaimWithoutClinicalCaseworkAccess()
    {
        var noteId = await factory.CreateApprovedBillableNoteAsync();
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        // Isolate this claim from other tests that submit the shared owner's month.
        // Billing is agency-scoped and does not require the biller to own its source.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var ownerId = await db.Users.MaxAsync(x => x.Id) + 1;
            db.Users.Add(new ServerUser { Id = ownerId, AgencyId = 1, Username = $"revocation-billing-{ownerId}",
                DisplayName = "Synthetic billing source owner", Role = "CaseManager", Permissions = UserPermissions.CaseManagement });
            var note = await db.Notes.SingleAsync(x => x.Id == noteId);
            (await db.People.SingleAsync(x => x.Id == note.PersonId)).UserId = ownerId;
            await db.SaveChangesAsync();
        }
        var permissions = await GetPermissionsAsync(12);
        await factory.ChangeUserPermissionsAsync(12, UserPermissions.Billing);
        try
        {
            using var response = await client.PostAsJsonAsync("/api/v1/billing/claim-lines",
                new CreateClaimLineRequest(noteId, false, null));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var notes = await client.GetAsync("/api/v1/notes/year/2026");
            Assert.Equal(HttpStatusCode.Forbidden, notes.StatusCode);
        }
        finally { await factory.ChangeUserPermissionsAsync(12, permissions); }
    }

    [Fact]
    public async Task AdministrationWithoutCaseManagementRetainsConsumerStatusAuthority()
    {
        var data = await SeedAsync();
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        var permissions = await GetPermissionsAsync(11);
        await factory.ChangeUserPermissionsAsync(11, UserPermissions.Administration);
        try
        {
            using var response = await SendAsync(client, "person-status", data);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var scope = factory.Services.CreateAsyncScope();
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApiDbContext>().People
                .Where(x => x.Id == data.PersonId).Select(x => x.Status).SingleAsync());
        }
        finally { await factory.ChangeUserPermissionsAsync(11, permissions); }
    }

    [Theory]
    [InlineData("caseload")]
    [InlineData("notes-person")]
    [InlineData("notes-month")]
    [InlineData("notes-day")]
    [InlineData("contacts")]
    [InlineData("providers")]
    [InlineData("reviews-person")]
    [InlineData("reviews-user")]
    [InlineData("appointments")]
    [InlineData("assessment")]
    [InlineData("pcp-source")]
    [InlineData("at-person")]
    [InlineData("at-user")]
    [InlineData("check-person")]
    [InlineData("documents")]
    [InlineData("annual-documents")]
    [InlineData("safety-plan")]
    [InlineData("ssn")]
    [InlineData("ai-context")]
    [InlineData("attestations")]
    [InlineData("prerequisite")]
    public async Task ExistingConsumerReadBoundariesAlsoObserveRevocation(string operation)
    {
        var data = await SeedAsync();
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var permissions = await GetPermissionsAsync(12);
        await factory.ChangeUserPermissionsAsync(12, UserPermissions.Billing);
        try
        {
            using var response = await ReadAsync(client, operation, data);
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
                $"{operation} returned {response.StatusCode}.");
            await AssertUnchangedAsync(data);
        }
        finally { await factory.ChangeUserPermissionsAsync(12, permissions); }
    }

    [Theory]
    [InlineData("reviews-person")]
    [InlineData("appointments")]
    [InlineData("pcp-source")]
    [InlineData("at-person")]
    [InlineData("check-person")]
    [InlineData("documents")]
    [InlineData("annual-documents")]
    [InlineData("safety-plan")]
    public async Task AssignedSupervisionRemainsIndependentFromCaseManagement(string operation)
    {
        var data = await SeedAsync();
        using var client = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var permissions = await GetPermissionsAsync(13);
        await factory.ChangeUserPermissionsAsync(13, UserPermissions.Supervision);
        try
        {
            using var response = await ReadAsync(client, operation, data);
            Assert.True(response.IsSuccessStatusCode,
                $"{operation}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
        finally { await factory.ChangeUserPermissionsAsync(13, permissions); }
    }

    [Theory]
    [InlineData("reviews-person")]
    [InlineData("appointments")]
    [InlineData("pcp-source")]
    [InlineData("at-person")]
    [InlineData("check-person")]
    public async Task PersonAgencyCannotBeOverriddenByARetainedOwnerId(string operation)
    {
        var data = await SeedAsync();
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await SetPersonAgencyAsync(data.PersonId, 2);
        try
        {
            using var response = await ReadAsync(client, operation, data);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await AssertUnchangedAsync(data);
        }
        finally { await SetPersonAgencyAsync(data.PersonId, 1); }
    }

    [Theory]
    [InlineData("journal-read")]
    [InlineData("journal-write")]
    [InlineData("journal-entry")]
    [InlineData("note-write")]
    [InlineData("note-delete")]
    [InlineData("form-open")]
    [InlineData("person-status")]
    public async Task OwnCaseworkQueriesRequireThePersonsExactAgency(string operation)
    {
        var data = await SeedAsync();
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await SetPersonAgencyAsync(data.PersonId, 2);
        try
        {
            using var response = await SendAsync(client, operation, data);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await AssertUnchangedAsync(data);
        }
        finally { await SetPersonAgencyAsync(data.PersonId, 1); }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(null)]
    public async Task OwnListsExcludeInconsistentPersonAndNoteTenantMarkers(int? noteAgencyId)
    {
        var foreignPerson = await SeedAsync();
        var foreignNote = await SeedAsync();
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await SetPersonAgencyAsync(foreignPerson.PersonId, 2);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var note = await db.Notes.SingleAsync(x => x.Id == foreignNote.NoteId);
            note.AgencyId = noteAgencyId;
            await db.SaveChangesAsync();
        }
        try
        {
            var caseload = await client.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
            Assert.DoesNotContain(caseload!, person => person.Id == foreignPerson.PersonId);
            foreach (var route in new[] { $"/api/v1/notes/year/{DateTime.Today.Year}",
                $"/api/v1/notes/day?date={foreignPerson.EventDate:yyyy-MM-dd}",
                $"/api/v1/people/{foreignNote.PersonId}/notes" })
            {
                var notes = await client.GetFromJsonAsync<List<NoteDto>>(route);
                Assert.DoesNotContain(notes!, note => note.Id == foreignPerson.NoteId || note.Id == foreignNote.NoteId);
            }
            using var abandoned = await client.PostAsync("/api/v1/notes/abandon-overdue", null);
            Assert.Equal(HttpStatusCode.OK, abandoned.StatusCode);
            await AssertUnchangedAsync(foreignPerson);
            await AssertUnchangedAsync(foreignNote);
        }
        finally
        {
            await SetPersonAgencyAsync(foreignPerson.PersonId, 1);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var note = await db.Notes.SingleAsync(x => x.Id == foreignNote.NoteId);
            note.AgencyId = 1;
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BillingRejectsMismatchedConsumerOrNoteAgencyWithoutLosingBillingPermission(bool personMismatch)
    {
        var noteId = await factory.CreateApprovedBillableNoteAsync();
        using var client = await factory.CreateAuthenticatedClientAsync("billing-only-one");
        int personId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var note = await db.Notes.SingleAsync(x => x.Id == noteId);
            personId = note.PersonId;
            if (personMismatch) (await db.People.SingleAsync(x => x.Id == personId)).AgencyId = 2;
            else note.AgencyId = 2;
            await db.SaveChangesAsync();
        }
        try
        {
            var candidates = await client.GetFromJsonAsync<List<BillingCandidateDto>>("/api/v1/billing/candidates");
            Assert.DoesNotContain(candidates!, candidate => candidate.NoteId == noteId);
            using var response = await client.PostAsJsonAsync("/api/v1/billing/claim-lines", new CreateClaimLineRequest(noteId, false, null));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await using var scope = factory.Services.CreateAsyncScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<ApiDbContext>().ClaimLines.AnyAsync(x => x.NoteId == noteId));
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            (await db.Notes.SingleAsync(x => x.Id == noteId)).AgencyId = 1;
            (await db.People.SingleAsync(x => x.Id == personId)).AgencyId = 1;
            await db.SaveChangesAsync();
        }
    }

    private static Task<HttpResponseMessage> ReadAsync(HttpClient client, string operation, CaseworkData data) =>
        client.GetAsync(operation switch
        {
            "caseload" => "/api/v1/caseload",
            "notes-person" => $"/api/v1/people/{data.PersonId}/notes",
            "notes-month" => "/api/v1/notes/monthly",
            "notes-day" => $"/api/v1/notes/day?date={data.EventDate:yyyy-MM-dd}",
            "contacts" => $"/api/v1/people/{data.PersonId}/contacts",
            "providers" => $"/api/v1/people/{data.PersonId}/providers",
            "reviews-person" => $"/api/v1/people/{data.PersonId}/reviews",
            "reviews-user" => "/api/v1/reviews?userId=12",
            "appointments" => $"/api/v1/people/{data.PersonId}/appointments/latest",
            "assessment" => $"/api/v1/people/{data.PersonId}/assessments/latest",
            "pcp-source" => $"/api/v1/people/{data.PersonId}/pcp-source?preferredAuthorUserId=12",
            "at-person" => $"/api/v1/people/{data.PersonId}/at-requests",
            "at-user" => "/api/v1/at-requests?userId=12",
            "check-person" => $"/api/v1/people/{data.PersonId}/check-requests",
            "documents" => $"/api/v1/people/{data.PersonId}/documents?cycleStart=2026-01-01",
            "annual-documents" => $"/api/v1/people/{data.PersonId}/annual-documents?cycleStart={DateTime.Today.AddMonths(-1):yyyy-MM-dd}",
            "safety-plan" => $"/api/v1/people/{data.PersonId}/safety-plans/latest",
            "ssn" => $"/api/v1/people/{data.PersonId}/ssn",
            "ai-context" => $"/api/v1/people/{data.PersonId}/ai-context",
            "attestations" => $"/api/v1/people/{data.PersonId}/attestations/pending",
            "prerequisite" => $"/api/v1/people/{data.PersonId}/forms/PCP/prerequisite?formId={data.FormId}",
            _ => throw new ArgumentException(operation)
        });

    private async Task SetPersonAgencyAsync(int personId, int agencyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        (await db.People.SingleAsync(x => x.Id == personId)).AgencyId = agencyId;
        await db.SaveChangesAsync();
    }

    private async Task<CaseworkData> SeedAsync()
    {
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        var noteId = await factory.CreateNoteInStatusAsync(1, personId);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var person = await db.People.SingleAsync(x => x.Id == personId);
        person.Journal = "Synthetic protected journal";
        var note = await db.Notes.SingleAsync(x => x.Id == noteId);
        note.EventDate = DateTime.Today.AddDays(-20);
        var form = await db.Forms.FirstAsync(x => x.PersonId == personId);
        form.CompletedDate = null;
        await db.SaveChangesAsync();
        return new(personId, noteId, form.Id, person.Revision, note.Revision,
            note.Narrative, note.EventDate!.Value, form.OpenedDate);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string operation, CaseworkData data) =>
        operation switch
        {
            "journal-read" => client.GetAsync($"/api/v1/people/{data.PersonId}/journal"),
            "journal-write" => client.PutAsJsonAsync($"/api/v1/people/{data.PersonId}/journal", new SaveJournalRequest("Changed")),
            "journal-entry" => client.PostAsJsonAsync($"/api/v1/people/{data.PersonId}/journal/entries", new { text = "Changed" }),
            "note-write" => client.PutAsJsonAsync($"/api/v1/notes/{data.NoteId}", new SaveNoteRequest(
                "Changed", DateTime.Today, "Pending", 15, null, data.PersonId, null, null, null, null, data.NoteRevision)),
            "note-delete" => client.DeleteAsync($"/api/v1/notes/{data.NoteId}?expectedRevision={data.NoteRevision}"),
            "notes-year" => client.GetAsync($"/api/v1/notes/year/{DateTime.Today.Year}"),
            "notes-abandon" => client.PostAsync("/api/v1/notes/abandon-overdue", null),
            "form-open" => client.PutAsJsonAsync($"/api/v1/forms/{data.FormId}", new UpdateFormRequest(null, DateTime.Today)),
            "person-status" => client.PutAsJsonAsync($"/api/v1/people/{data.PersonId}/status",
                new SetPersonStatusRequest("NoLongerServed", "Synthetic status", data.PersonRevision)),
            "productivity-report" => client.GetAsync("/api/v1/reports/productivity-units?start=2026-01-01&end=2026-12-31"),
            "consumer-loss-report" => client.GetAsync("/api/v1/reports/consumer-billing-loss?start=2026-01-01&end=2026-12-31"),
            _ => throw new ArgumentException(operation)
        };

    private async Task AssertUnchangedAsync(CaseworkData data)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var person = await db.People.AsNoTracking().SingleAsync(x => x.Id == data.PersonId);
        Assert.Equal(12, person.UserId);
        Assert.Equal(0, person.Status);
        Assert.Equal(data.PersonRevision, person.Revision);
        Assert.Equal("Synthetic protected journal", person.Journal);
        var note = await db.Notes.AsNoTracking().SingleAsync(x => x.Id == data.NoteId);
        Assert.Equal(data.NoteRevision, note.Revision);
        Assert.Equal(1, note.Status);
        Assert.Equal(data.Narrative, note.Narrative);
        Assert.Equal(data.EventDate, note.EventDate);
        var form = await db.Forms.AsNoTracking().SingleAsync(x => x.Id == data.FormId);
        Assert.Equal(data.OpenedDate, form.OpenedDate);
        Assert.False(await db.PersonVersions.AnyAsync(x => x.PersonId == data.PersonId));
    }

    private async Task<UserPermissions> GetPermissionsAsync(int userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApiDbContext>().Users
            .Where(x => x.Id == userId).Select(x => x.Permissions).SingleAsync();
    }

    private sealed record CaseworkData(int PersonId, int NoteId, int FormId, int PersonRevision,
        int NoteRevision, string Narrative, DateTime EventDate, DateTime? OpenedDate);
}
