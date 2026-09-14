using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[CollectionDefinition(Name)]
public sealed class NoteSubmissionComplianceCollection : ICollectionFixture<SatiApiFactory>
{
    public const string Name = "Note submission compliance";
}

[Collection(NoteSubmissionComplianceCollection.Name)]
public sealed class NoteSubmissionComplianceApiTests(SatiApiFactory factory)
{
    [Theory]
    [InlineData(false, "current")]
    [InlineData(true, "current")]
    [InlineData(false, "historical")]
    [InlineData(true, "historical")]
    [InlineData(false, "form-tag")]
    [InlineData(true, "form-tag")]
    [InlineData(false, "justification")]
    [InlineData(true, "justification")]
    public async Task LoggedSubmissionRefusesComplianceFailuresWithoutWriting(bool update, string contingency)
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var today = BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow);
        var personId = await SeedPersonAsync(today.AddDays(-5),
            contingency == "historical" ? today : null);
        var noteId = update ? await factory.CreateNoteInStatusAsync(NoteWorkflow.Pending, personId) : 0;
        var revision = update ? (await factory.GetNoteStateAsync(noteId)).Revision : 0;
        var request = Request(personId, "Logged", today.AddDays(-2), revision) with
        {
            FormType = contingency == "form-tag" ? "PCP" : null,
            NoteType = contingency == "form-tag" ? "Form" : "Contact",
            CaseManagerJustification = contingency == "justification" ? "Please override the gate." : null
        };
        var before = await ReadStateAsync(personId);

        var response = update
            ? await client.PutAsJsonAsync($"/api/v1/notes/{noteId}", request)
            : await client.PostAsJsonAsync("/api/v1/notes", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        Assert.Equal("note_compliance_blocked", error!.Code);
        Assert.Contains("PCP", error.Message);
        Assert.Contains("Pending", error.Message);
        Assert.Equal(before, await ReadStateAsync(personId));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("HeldForCompliance")]
    [InlineData("ComplianceBlocked")]
    public async Task NoncompliantServiceDocumentationCanStillBeSavedAndEdited(string status)
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var today = BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow);
        var personId = await SeedPersonAsync(today.AddDays(-5), null);
        var request = Request(personId, status, today);
        var created = await client.PostAsJsonAsync("/api/v1/notes", request);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var note = (await created.Content.ReadFromJsonAsync<NoteDto>())!;

        var edited = await client.PutAsJsonAsync($"/api/v1/notes/{note.Id}", request with
        {
            Narrative = "Retained service documentation, corrected.", ExpectedRevision = note.Revision
        });

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(status, (await edited.Content.ReadFromJsonAsync<NoteDto>())!.Status);
    }

    [Theory]
    [InlineData("due-today")]
    [InlineData("future-due")]
    [InlineData("completed-today")]
    [InlineData("requirement-disabled")]
    public async Task LoggedSubmissionPreservesConfiguredDateBoundaries(string contingency)
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var today = BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow);
        var dueDate = contingency switch
        {
            "due-today" => today,
            "future-due" => today.AddDays(1),
            _ => today.AddDays(-5)
        };
        var personId = await SeedPersonAsync(dueDate,
            contingency == "completed-today" ? today : null,
            contingency == "requirement-disabled" ? BillingComplianceRequirements.None : BillingComplianceRequirements.Pcp);

        var response = await client.PostAsJsonAsync("/api/v1/notes", Request(personId, "Logged", today));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Logged", (await response.Content.ReadFromJsonAsync<NoteDto>())!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturnedAndReassignedNotesCannotEnterReviewForABlockedTarget(bool reassign)
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var today = BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow);
        var blockedPerson = await SeedPersonAsync(today.AddDays(-5), null);
        var originalPerson = reassign ? await factory.CreateBillingWorkflowPersonAsync() : blockedPerson;
        var noteId = await factory.CreateNoteInStatusAsync(NoteWorkflow.Returned, originalPerson);
        var (_, revision) = await factory.GetNoteStateAsync(noteId);
        var before = await ReadStateAsync(originalPerson);

        var response = await client.PutAsJsonAsync($"/api/v1/notes/{noteId}",
            Request(blockedPerson, "Logged", today, revision));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("note_compliance_blocked", (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        Assert.Equal(before, await ReadStateAsync(originalPerson));
    }

    private async Task<int> SeedPersonAsync(DateTime dueDate, DateTime? completedDate,
        BillingComplianceRequirements requirements = BillingComplianceRequirements.Pcp)
    {
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var form = await db.Forms.SingleAsync(x => x.PersonId == personId && x.Type == "PCP");
        form.DueDate = dueDate;
        form.CompletedDate = completedDate;
        var settings = await db.Settings.SingleOrDefaultAsync(x => x.AgencyId == 1);
        if (settings is null) db.Settings.Add(settings = new ServerSettings { AgencyId = 1 });
        settings.BillingComplianceRequirements = requirements;
        await db.SaveChangesAsync();
        return personId;
    }

    private async Task<string> ReadStateAsync(int personId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var notes = await db.Notes.AsNoTracking().Where(x => x.PersonId == personId)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.Narrative, x.Status, x.Revision, x.PersonId }).ToListAsync();
        return System.Text.Json.JsonSerializer.Serialize(notes);
    }

    private static SaveNoteRequest Request(int personId, string status, DateTime serviceDate, int revision = 0) =>
        new("Do not lose this clinical draft.", serviceDate, status, 30, null, personId,
            null, "Contact", null, null, ExpectedRevision: revision, GoalProgress: "Moderate");
}
