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
    public async Task ConfirmedSafetyPlanAttestationReusesMovedScheduledNote()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var completedOn = DateTime.Today.AddDays(-1);
        var plannedOn = DateTime.Today.AddDays(1);
        int personId;
        int formId;
        int noteId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            personId = await db.People.MaxAsync(row => row.Id) + 1;
            var person = new ServerPerson
            {
                Id = personId,
                UserId = 12,
                AgencyId = 1,
                FirstName = "Safety",
                LastName = "Plan Test",
                BirthDate = new DateTime(1990, 1, 1),
                EffectiveDate = completedOn.AddYears(-1)
            };
            var form = new ServerForm
            {
                PersonId = personId,
                Type = "SafetyPlan",
                DueDate = completedOn,
                TargetEffectiveDate = completedOn
            };
            db.People.Add(person);
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
            var note = new ServerNote
            {
                PersonId = personId,
                AgencyId = 1,
                Narrative = "Prepare the Safety Plan.",
                EventDate = plannedOn,
                Status = NoteWorkflow.Scheduled,
                Minutes = 15,
                NoteType = (int)NoteType.Form,
                Activities = (int)NoteActivity.Form,
                FormType = (int)FormType.SafetyPlan,
                FormId = formId
            };
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            noteId = note.Id;
        }

        try
        {
            using var first = await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/SafetyPlan/attestation",
                new AttestFormRequest(formId, completedOn));
            Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
            var preview = await first.Content.ReadFromJsonAsync<ApiErrorDto>();
            Assert.NotNull(preview);
            Assert.Equal(ManualAttestationNoteRules.ScheduledConversionRequiredCode,
                preview.Code);
            Assert.False(string.IsNullOrWhiteSpace(preview.ConfirmationToken));

            await using (var unchangedScope = factory.Services.CreateAsyncScope())
            {
                var unchanged = unchangedScope.ServiceProvider.GetRequiredService<ApiDbContext>();
                Assert.Null((await unchanged.Forms.AsNoTracking()
                    .SingleAsync(row => row.Id == formId)).CompletedDate);
                var planned = await unchanged.Notes.AsNoTracking()
                    .SingleAsync(row => row.Id == noteId);
                Assert.Equal(plannedOn, planned.EventDate);
                Assert.Equal(NoteWorkflow.Scheduled, planned.Status);
            }

            using var stale = await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/SafetyPlan/attestation",
                new AttestFormRequest(formId, completedOn, noteId,
                    ConfirmScheduledNoteConversion: true,
                    ScheduledNoteConversionToken: "stale"));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            using var changedActualDate = await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/SafetyPlan/attestation",
                new AttestFormRequest(formId, completedOn.AddDays(1), noteId,
                    ConfirmScheduledNoteConversion: true,
                    ScheduledNoteConversionToken: preview.ConfirmationToken));
            Assert.Equal(HttpStatusCode.Conflict, changedActualDate.StatusCode);

            using var confirmed = await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/SafetyPlan/attestation",
                new AttestFormRequest(formId, completedOn, noteId,
                    ConfirmScheduledNoteConversion: true,
                    ScheduledNoteConversionToken: preview.ConfirmationToken));
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var savedNote = await db.Notes.AsNoTracking()
                .SingleAsync(row => row.FormId == formId);
            var savedForm = await db.Forms.AsNoTracking()
                .SingleAsync(row => row.Id == formId);
            var attestation = await db.FormAttestations.AsNoTracking()
                .SingleAsync(row => row.FormId == formId);
            Assert.Equal(noteId, savedNote.Id);
            Assert.Equal(completedOn, savedNote.EventDate);
            Assert.Equal(NoteWorkflow.Pending, savedNote.Status);
            Assert.Equal(2, savedNote.Revision);
            Assert.Equal(completedOn, savedForm.CompletedDate);
            Assert.Equal(noteId, attestation.EvidenceNoteId);
        }
        finally
        {
            await RemoveReviewAsync(personId);
        }
    }

    [Fact]
    public async Task ManualPcpAttestationOnEffectiveDateCitesItsDraftNote()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var target = DateTime.Today.AddDays(-1);
        int personId;
        int formId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            personId = await db.People.MaxAsync(row => row.Id) + 1;
            var person = new ServerPerson
            {
                Id = personId,
                UserId = 12,
                AgencyId = 1,
                FirstName = "Pcp",
                LastName = "Evidence Test",
                BirthDate = new DateTime(1990, 1, 1),
                EffectiveDate = target.AddYears(-1)
            };
            var form = new ServerForm
            {
                PersonId = personId,
                Type = "PCP",
                DueDate = target,
                TargetEffectiveDate = target
            };
            db.People.Add(person);
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
        }

        try
        {
            using var response = await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/PCP/attestation",
                new AttestFormRequest(formId, target));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var form = await db.Forms.AsNoTracking().SingleAsync(row => row.Id == formId);
            var note = await db.Notes.AsNoTracking().SingleAsync(row => row.FormId == formId);
            var attestation = await db.FormAttestations.AsNoTracking()
                .SingleAsync(row => row.FormId == formId);
            Assert.Equal(target, form.CompletedDate);
            Assert.Equal(target, note.EventDate);
            Assert.Equal(note.Id, attestation.EvidenceNoteId);
        }
        finally
        {
            await RemoveReviewAsync(personId);
        }
    }

    [Fact]
    public async Task PcpAttestationOnEffectiveDateRefusesToDuplicateAnUnlinkedNote()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var target = DateTime.Today.AddDays(-1);
        int personId;
        int formId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            personId = await db.People.MaxAsync(row => row.Id) + 1;
            var noteId = await db.Notes.MaxAsync(row => row.Id) + 1;
            var person = new ServerPerson
            {
                Id = personId,
                UserId = 12,
                AgencyId = 1,
                FirstName = "Pcp",
                LastName = "Boundary Test",
                BirthDate = new DateTime(1990, 1, 1),
                EffectiveDate = target.AddYears(-1)
            };
            var form = new ServerForm
            {
                PersonId = personId,
                Type = "PCP",
                DueDate = target,
                TargetEffectiveDate = target
            };
            db.People.Add(person);
            db.Forms.Add(form);
            db.Notes.Add(new ServerNote
            {
                Id = noteId,
                PersonId = personId,
                AgencyId = 1,
                Narrative = "Completed the PCP.",
                EventDate = target,
                Status = NoteWorkflow.Logged,
                NoteType = (int)NoteType.Form,
                FormType = (int)FormType.PCP
            });
            await db.SaveChangesAsync();
            formId = form.Id;
        }

        try
        {
            var response = await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/PCP/attestation",
                new AttestFormRequest(formId, target));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("unlinked form note", await response.Content.ReadAsStringAsync(),
                StringComparison.OrdinalIgnoreCase);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.Null((await db.Forms.AsNoTracking().SingleAsync(row => row.Id == formId)).CompletedDate);
            Assert.Equal(1, await db.Notes.CountAsync(row => row.PersonId == personId));
        }
        finally
        {
            await RemoveReviewAsync(personId);
        }
    }

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
