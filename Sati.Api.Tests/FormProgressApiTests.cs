using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class FormProgressApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task TelehealthActivityRoundTripsWithoutVisitDocumentation()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        int personId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            personId = await db.People.MaxAsync(p => p.Id) + 1;
            db.People.Add(new ServerPerson { Id = personId, UserId = 12, AgencyId = 1,
                FirstName = "Synthetic", LastName = "Telehealth", BirthDate = new(1990, 1, 1) });
            await db.SaveChangesAsync();
        }
        try
        {
            using var response = await client.PostAsJsonAsync("/api/v1/notes",
                new SaveNoteRequest("Synthetic remote contact.", DateTime.Today, "Pending", 15,
                    null, personId, null, "Phone", null, null, GoalProgress: "None",
                    Activities: (int)NoteActivity.Telehealth));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var returned = (await response.Content.ReadFromJsonAsync<NoteDto>())!;
            Assert.Equal((int)NoteActivity.Telehealth, returned.Activities);
            Assert.Equal("Telehealth", NoteActivityRules.DisplayLabel(returned.Activities, returned.NoteType));
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.Notes.RemoveRange(db.Notes.Where(n => n.PersonId == personId));
            db.People.RemoveRange(db.People.Where(p => p.Id == personId));
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData("PCP")]
    [InlineData("ComprehensiveAssessment")]
    public async Task ExplicitCompletionRecordsBothDatesAndStaleSaveRollsBack(string type)
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var today = DateTime.Today;
        var due = today.AddDays(10);
        var target = type == "PCP" ? due : due.AddDays(90);
        int personId;
        int formId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            personId = await db.People.MaxAsync(p => p.Id) + 1;
            db.People.Add(new ServerPerson { Id = personId, UserId = 12, AgencyId = 1,
                FirstName = "Synthetic", LastName = "Progress", BirthDate = new(1990, 1, 1),
                EffectiveDate = target.AddYears(-1) });
            var form = new ServerForm { PersonId = personId, Type = type, DueDate = due,
                TargetEffectiveDate = target };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
        }
        var request = new SaveNoteRequest("Synthetic document work.", today.AddDays(-1),
            "Pending", 15, null, personId, type, "Form", null, null,
            GoalProgress: "None", FormId: formId, Activities: (int)NoteActivity.Form,
            FormProgress: new(AnnualPcpProgressAction.Complete, today.AddDays(-5), today.AddDays(-2),
                null, null, due, target));
        try
        {
            using var response = await client.PostAsJsonAsync("/api/v1/notes", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var note = (await response.Content.ReadFromJsonAsync<NoteDto>())!;
            using var stale = await client.PostAsJsonAsync("/api/v1/notes", request with
            { FormProgress = request.FormProgress! with { Action = AnnualPcpProgressAction.None,
                OpenedOn = null, CompletedOn = null } });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            using var unchanged = await client.PutAsJsonAsync($"/api/v1/notes/{note.Id}", request with
            { ExpectedRevision = note.Revision, FormProgress = new(AnnualPcpProgressAction.None,
                null, null, today.AddDays(-5), today.AddDays(-2), due, target) });
            Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var form = await db.Forms.AsNoTracking().SingleAsync(f => f.Id == formId);
            Assert.Equal(today.AddDays(-5), form.OpenedDate);
            Assert.Equal(today.AddDays(-2), form.CompletedDate);
            Assert.Single(await db.Notes.Where(n => n.PersonId == personId).ToListAsync());
            Assert.Single(await db.FormAttestations.Where(a => a.FormId == formId).ToListAsync());
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            // Synthetic fixture cleanup bypasses the production append-only
            // SaveChanges guard, matching the existing API attestation tests.
            await db.FormAttestations.Where(a => a.FormId == formId).ExecuteDeleteAsync();
            await db.Notes.Where(n => n.PersonId == personId).ExecuteDeleteAsync();
            await db.Forms.Where(f => f.Id == formId).ExecuteDeleteAsync();
            await db.People.Where(p => p.Id == personId).ExecuteDeleteAsync();
        }
    }
}
