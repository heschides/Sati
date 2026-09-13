using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class LegacyOverlapApprovalTests(SatiApiFactory factory)
{
    [Fact]
    public async Task ExistingOverlapCannotBecomeAClaimEvenWhenTheLegacyNoteIsApproved()
    {
        var noteId = await factory.CreateApprovedBillableNoteAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var note = await db.Notes.SingleAsync(row => row.Id == noteId);
            note.StartTime = 90;
            note.Minutes = 30;
            db.Notes.Add(new ServerNote { PersonId = note.PersonId, AgencyId = note.AgencyId,
                Narrative = "Synthetic conflicting interval", Status = NoteWorkflow.Pending,
                EventDate = note.EventDate, StartTime = 100, Minutes = 30 });
            await db.SaveChangesAsync();
        }
        using var biller = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var response = await biller.PostAsJsonAsync("/api/v1/billing/claim-lines", new CreateClaimLineRequest(noteId, false, null));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("service_time_overlap", (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        await using var verification = factory.Services.CreateAsyncScope();
        Assert.False(await verification.ServiceProvider.GetRequiredService<ApiDbContext>().ClaimLines.AnyAsync(row => row.NoteId == noteId));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task ApprovalIncludingComplianceOverrideCannotAcceptExistingOverlappingTime(bool useOverride, bool overlaps)
    {
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        int noteId;
        // Every theory row shares the fixture and case manager. Give each person's
        // pair a private day so a previous row is not mistaken for this row's overlap.
        var serviceDate = new DateTime(2020, 1, 1).AddDays(-personId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var person = await db.People.SingleAsync(row => row.Id == personId);
            // Synthetic legacy rows deliberately bypass the ordinary authoring guard.
            var note = new ServerNote { PersonId = personId, AgencyId = person.AgencyId,
                Narrative = "Synthetic review candidate", Status = NoteWorkflow.Logged,
                EventDate = serviceDate, StartTime = 90, Minutes = 30 };
            db.Notes.AddRange(note, new ServerNote { PersonId = personId, AgencyId = person.AgencyId,
                Narrative = "Synthetic existing interval", Status = NoteWorkflow.Pending,
                EventDate = serviceDate, StartTime = overlaps ? 100 : 120, Minutes = 30 });
            await db.SaveChangesAsync();
            noteId = note.Id;
        }
        using var reviewer = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var (_, revision) = await factory.GetNoteStateAsync(noteId);
        var action = useOverride ? "approve-override" : "approve";
        using var response = await reviewer.PostAsJsonAsync($"/api/v1/supervisor/notes/{noteId}/{action}",
            new SupervisorNoteActionRequest(useOverride ? "Documented exception is not a time override." : null, revision));
        Assert.Equal(overlaps ? HttpStatusCode.Conflict : HttpStatusCode.OK, response.StatusCode);
        if (overlaps)
            Assert.Equal("service_time_overlap", (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        await using var verification = factory.Services.CreateAsyncScope();
        var verify = verification.ServiceProvider.GetRequiredService<ApiDbContext>();
        var saved = await verify.Notes.AsNoTracking().SingleAsync(row => row.Id == noteId);
        Assert.Equal(overlaps ? NoteWorkflow.Logged : NoteWorkflow.Approved, saved.Status);
        Assert.Equal(overlaps ? revision : revision + 1, saved.Revision);
        if (overlaps)
        {
            Assert.Null(saved.ApprovedById);
            Assert.False(saved.ComplianceOverride);
        }
    }
}
