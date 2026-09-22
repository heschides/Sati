using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class ApiFormNoteAttestationTests(SatiApiFactory factory)
{
    [Fact]
    public async Task LoggingMixedFormAndPhonePersistsBothAndAttestsTheForm()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var (personId, formId) = await CreateReviewAsync(DateTime.Today, completedOn: null);
        var activityDate = DateTime.Today.AddDays(-1);
        var activities = (int)(NoteActivity.Form | NoteActivity.Phone);

        try
        {
            using var response = await client.PostAsJsonAsync("/api/v1/notes", new SaveNoteRequest(
                "Completed the review and called the client.", activityDate, "Logged", 15, null,
                personId, "Q3R", "Form", "Submitted for review.", null,
                GoalProgress: "None", FormId: formId, Activities: activities));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var note = (await response.Content.ReadFromJsonAsync<NoteDto>())!;
            Assert.Equal(activities, note.Activities);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.Equal(activities, (await db.Notes.AsNoTracking()
                .SingleAsync(row => row.Id == note.Id)).Activities);
            Assert.Equal(activityDate.Date, (await db.Forms.AsNoTracking()
                .SingleAsync(row => row.Id == formId)).CompletedDate);
        }
        finally
        {
            await RemoveReviewAsync(personId);
        }
    }

    [Fact]
    public async Task LoggingAFormNoteAttestsItsExactFormOnTheActivityDate()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var (personId, formId) = await CreateReviewAsync(DateTime.Today, completedOn: null);
        var activityDate = DateTime.Today.AddDays(-1);

        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/notes", new SaveNoteRequest(
                "Completed the 90-day review.", activityDate, "Logged", 15, null,
                personId, "Q3R", "Form", "The form note is submitted for review.", null,
                GoalProgress: "None", FormId: formId));
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                await response.Content.ReadAsStringAsync());
            var note = (await response.Content.ReadFromJsonAsync<NoteDto>())!;

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var form = await db.Forms.AsNoTracking().SingleAsync(row => row.Id == formId);
            Assert.Equal(activityDate.Date, form.CompletedDate);
            var evidence = await db.FormAttestations.AsNoTracking()
                .Where(row => row.FormId == formId && row.Kind == "Attested")
                .OrderByDescending(row => row.Id).FirstAsync();
            Assert.Equal(note.Id, evidence.EvidenceNoteId);
            Assert.Equal(activityDate.Date, evidence.CompletedOn);
        }
        finally
        {
            await RemoveReviewAsync(personId);
        }
    }

    [Fact]
    public async Task AConflictingPriorDateRequiresAnExplanationAndRollsBackTheNote()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var originalDate = DateTime.Today.AddDays(-2);
        var newDate = DateTime.Today.AddDays(-1);
        var (personId, formId) = await CreateReviewAsync(DateTime.Today, originalDate);

        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/notes", new SaveNoteRequest(
                "Corrected 90-day review date.", newDate, "Logged", 15, null,
                personId, "Q3R", "Form", "Date correction for review.", null,
                GoalProgress: "None", FormId: formId));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.Equal(originalDate, await db.Forms.AsNoTracking()
                .Where(form => form.Id == formId).Select(form => form.CompletedDate)
                .SingleAsync());
            Assert.False(await db.Notes.AsNoTracking().AnyAsync(note =>
                note.PersonId == personId && note.Narrative == "Corrected 90-day review date."));
        }
        finally
        {
            await RemoveReviewAsync(personId);
        }
    }

    [Fact]
    public async Task AReasonedCorrectionReplacesTheDateAndFlagsSupervisorReview()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var originalDate = DateTime.Today.AddDays(-2);
        var correctedDate = DateTime.Today.AddDays(-1);
        var (personId, formId) = await CreateReviewAsync(DateTime.Today, originalDate);

        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/notes", new SaveNoteRequest(
                "Corrected review work date.", correctedDate, "Logged", 15, null,
                personId, "Q3R", "Form", "Correcting the earlier date.", null,
                GoalProgress: "None", FormId: formId,
                FormDateCorrectionReason: "The work was done one day later."));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var note = (await response.Content.ReadFromJsonAsync<NoteDto>())!;

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.Equal(correctedDate, await db.Forms.AsNoTracking()
                .Where(form => form.Id == formId).Select(form => form.CompletedDate)
                .SingleAsync());
            var ledger = await db.FormAttestations.AsNoTracking()
                .Where(row => row.FormId == formId)
                .OrderBy(row => row.Id).ToListAsync();
            Assert.Equal(["Revoked", "Attested"], ledger.Select(row => row.Kind).ToArray());
            Assert.Equal(note.Id, ledger[^1].EvidenceNoteId);
            var flag = await db.FormAttestationChangeReviewFlags.AsNoTracking()
                .SingleAsync(row => row.NoteId == note.Id);
            Assert.True(flag.RequiresSupervisorAttention);
            Assert.Equal("The work was done one day later.", flag.Reason);
        }
        finally
        {
            await RemoveReviewAsync(personId);
        }
    }

    [Fact]
    public async Task AReviewCompletedOneDayLateCanBeLoggedWithoutSelfBlocking()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var dueDate = DateTime.Today.AddDays(-1);
        var (personId, formId) = await CreateReviewAsync(
            dueDate, completedOn: null, completeOtherObligations: true);

        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/notes", new SaveNoteRequest(
                "Completed the late review and documented the actual date.",
                DateTime.Today, "Logged", 15, null, personId, "Q3R", "Form",
                null, null, GoalProgress: "None", FormId: formId));
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                await response.Content.ReadAsStringAsync());

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.Equal(DateTime.Today, await db.Forms.AsNoTracking()
                .Where(form => form.Id == formId).Select(form => form.CompletedDate)
                .SingleAsync());
            var note = await db.Notes.AsNoTracking().SingleAsync(row => row.PersonId == personId);
            var result = FormWorkBillingRules.Evaluate(
                new FormWorkNoteFact(personId, "Q3R", note.EventDate, formId),
                new FormWorkObligationFact(formId, personId, "Q3R", dueDate, DateTime.Today));
            Assert.False(result.Passed);
        }
        finally
        {
            await RemoveReviewAsync(personId);
        }
    }

    [Fact]
    public async Task AnotherOverdueObligationStillBlocksSubmissionAndRollsBackAttestation()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var (personId, formId) = await CreateReviewAsync(
            DateTime.Today.AddDays(-1), completedOn: null);

        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/notes", new SaveNoteRequest(
                "Review note with a separate outstanding PCP.", DateTime.Today,
                "Logged", 15, null, personId, "Q3R", "Form", null, null,
                GoalProgress: "None", FormId: formId));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.Null(await db.Forms.AsNoTracking().Where(form => form.Id == formId)
                .Select(form => form.CompletedDate).SingleAsync());
            Assert.False(await db.Notes.AsNoTracking().AnyAsync(note =>
                note.PersonId == personId &&
                note.Narrative == "Review note with a separate outstanding PCP."));
        }
        finally
        {
            await RemoveReviewAsync(personId);
        }
    }

    private async Task<(int PersonId, int FormId)> CreateReviewAsync(
        DateTime dueDate, DateTime? completedOn,
        bool completeOtherObligations = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var person = new ServerPerson
        {
            Id = await db.People.MaxAsync(row => row.Id) + 1,
            UserId = 12,
            AgencyId = 1,
            FirstName = "Form",
            LastName = "Note Test",
            BirthDate = new DateTime(1990, 1, 1),
            EffectiveDate = dueDate.AddMonths(-6)
        };
        var form = new ServerForm
        {
            PersonId = person.Id,
            Type = "Q3R",
            DueDate = dueDate.Date,
            TargetEffectiveDate = dueDate.AddMonths(-6).Date,
            CompletedDate = completedOn?.Date
        };
        db.People.Add(person);
        db.Forms.Add(form);
        if (completeOtherObligations)
        {
            var effective = person.EffectiveDate!.Value.Date;
            foreach (var (type, deadline) in new[]
                     {
                         ("ComprehensiveAssessment", effective.AddDays(-90)),
                         ("PCP", effective),
                         ("Q1R", effective.AddDays(90)),
                         ("Q2R", effective.AddDays(180))
                     })
            {
                db.Forms.Add(new ServerForm
                {
                    PersonId = person.Id,
                    Type = type,
                    DueDate = deadline,
                    TargetEffectiveDate = effective,
                    CompletedDate = deadline
                });
            }
        }
        await db.SaveChangesAsync();
        return (person.Id, form.Id);
    }

    private async Task RemoveReviewAsync(int personId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var formIds = await db.Forms.Where(form => form.PersonId == personId)
            .Select(form => form.Id).ToListAsync();
        await db.FormAttestations.Where(row => formIds.Contains(row.FormId))
            .ExecuteDeleteAsync();
        await db.FormAttestationChangeReviewFlags.Where(row => row.PersonId == personId)
            .ExecuteDeleteAsync();
        await db.Notes.Where(note => note.PersonId == personId).ExecuteDeleteAsync();
        await db.Forms.Where(form => form.PersonId == personId).ExecuteDeleteAsync();
        await db.People.Where(person => person.Id == personId).ExecuteDeleteAsync();
    }
}
