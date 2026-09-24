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
    public async Task OlderCycleFormNoteRequiresWrittenJustificationAndRetainsExactEvidence()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var (personId, olderFormId, renewalFormId, olderTarget, renewalTarget) =
            await CreateAssessmentOverlapAsync();
        var justification =
            "Source evidence confirms this was genuinely late work for the older plan.";

        try
        {
            var request = new SaveNoteRequest(
                "Completed the older-plan Comprehensive Assessment.",
                DateTime.Today,
                "Logged",
                30,
                null,
                personId,
                "ComprehensiveAssessment",
                "Form",
                null,
                null,
                GoalProgress: "Moderate",
                FormId: olderFormId,
                Activities: (int)NoteActivity.Form);

            using var refused = await client.PostAsJsonAsync("/api/v1/notes", request);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var refusal = (await refused.Content.ReadFromJsonAsync<ApiErrorDto>())!;
            Assert.Equal(NoteSubmissionGate.RefusalCode, refusal.Code);
            Assert.Contains(olderTarget.ToString("MMMM d, yyyy"), refusal.Message,
                StringComparison.Ordinal);
            Assert.Contains(renewalTarget.ToString("MMMM d, yyyy"), refusal.Message,
                StringComparison.Ordinal);

            await using (var unchangedScope = factory.Services.CreateAsyncScope())
            {
                var unchanged = unchangedScope.ServiceProvider
                    .GetRequiredService<ApiDbContext>();
                Assert.False(await unchanged.Notes.AsNoTracking().AnyAsync(note =>
                    note.PersonId == personId));
                Assert.Null((await unchanged.Forms.AsNoTracking()
                    .SingleAsync(form => form.Id == olderFormId)).CompletedDate);
            }

            using var accepted = await client.PostAsJsonAsync("/api/v1/notes", request with
            {
                CaseManagerJustification = justification
            });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var note = (await accepted.Content.ReadFromJsonAsync<NoteDto>())!;
            Assert.Equal(olderFormId, note.FormId);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.Equal(DateTime.Today, (await db.Forms.AsNoTracking()
                .SingleAsync(form => form.Id == olderFormId)).CompletedDate);
            Assert.Null((await db.Forms.AsNoTracking()
                .SingleAsync(form => form.Id == renewalFormId)).CompletedDate);
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(row =>
                row.Action == "note.older-form-cycle-justified" &&
                row.ResourceId == note.Id.ToString());
            Assert.Contains($"\"selectedFormId\":{olderFormId}", audit.MetadataJson,
                StringComparison.Ordinal);
            Assert.Contains($"\"renewalFormId\":{renewalFormId}", audit.MetadataJson,
                StringComparison.Ordinal);
            Assert.DoesNotContain(justification, audit.MetadataJson,
                StringComparison.Ordinal);
        }
        finally
        {
            await RemoveReviewAsync(personId);
        }
    }

    [Fact]
    public async Task ManualOlderCycleAttestationRefusesTheAmbiguousCheckboxPath()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var (personId, olderFormId, renewalFormId, olderTarget, renewalTarget) =
            await CreateAssessmentOverlapAsync();

        try
        {
            using var response = await client.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/ComprehensiveAssessment/attestation",
                new AttestFormRequest(olderFormId, DateTime.Today));

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(olderTarget.ToString("MMMM d, yyyy"), body,
                StringComparison.Ordinal);
            Assert.Contains(renewalTarget.ToString("MMMM d, yyyy"), body,
                StringComparison.Ordinal);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.Null((await db.Forms.AsNoTracking()
                .SingleAsync(form => form.Id == olderFormId)).CompletedDate);
            Assert.Null((await db.Forms.AsNoTracking()
                .SingleAsync(form => form.Id == renewalFormId)).CompletedDate);
            Assert.False(await db.FormAttestations.AsNoTracking().AnyAsync(row =>
                row.FormId == olderFormId || row.FormId == renewalFormId));
        }
        finally
        {
            await RemoveReviewAsync(personId);
        }
    }

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
    public async Task MigrationCancelledDuplicateDoesNotBlockScheduledNoteConversion()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var completedOn = DateTime.Today.AddDays(-1);
        var plannedOn = DateTime.Today.AddDays(1);
        int personId;
        int formId;
        int survivorId;
        int cancelledDuplicateId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            personId = await db.People.MaxAsync(row => row.Id) + 1;
            var person = new ServerPerson
            {
                Id = personId,
                UserId = 12,
                AgencyId = 1,
                FirstName = "Cancelled",
                LastName = "Duplicate Test",
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
            var survivor = new ServerNote
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
            var cancelledDuplicate = new ServerNote
            {
                PersonId = personId,
                AgencyId = 1,
                Narrative = "Prepare the Safety Plan.",
                EventDate = plannedOn,
                Status = NoteWorkflow.Cancelled,
                Minutes = 15,
                NoteType = (int)NoteType.Form,
                Activities = (int)NoteActivity.Form,
                FormType = (int)FormType.SafetyPlan,
                FormId = formId
            };
            db.Notes.AddRange(survivor, cancelledDuplicate);
            await db.SaveChangesAsync();
            survivorId = survivor.Id;
            cancelledDuplicateId = cancelledDuplicate.Id;
            db.AuditEvents.Add(new ServerAuditEvent
            {
                AgencyId = 1,
                ActorUserId = 12,
                Action = ManualAttestationNoteRules.ScheduledDuplicateCancellationAuditAction,
                ResourceType = "Note",
                ResourceId = cancelledDuplicateId.ToString(),
                CorrelationId = "test-scheduled-duplicate-repair"
            });
            await db.SaveChangesAsync();
        }

        try
        {
            using var first = await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/SafetyPlan/attestation",
                new AttestFormRequest(formId, completedOn));
            Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
            var preview = (await first.Content.ReadFromJsonAsync<ApiErrorDto>())!;
            Assert.Equal(ManualAttestationNoteRules.ScheduledConversionRequiredCode,
                preview.Code);

            using var confirmed = await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/SafetyPlan/attestation",
                new AttestFormRequest(formId, completedOn, survivorId,
                    ConfirmScheduledNoteConversion: true,
                    ScheduledNoteConversionToken: preview.ConfirmationToken));
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var survivorAfter = await db.Notes.AsNoTracking()
                .SingleAsync(row => row.Id == survivorId);
            var duplicateAfter = await db.Notes.AsNoTracking()
                .SingleAsync(row => row.Id == cancelledDuplicateId);
            var attestation = await db.FormAttestations.AsNoTracking()
                .SingleAsync(row => row.FormId == formId);
            Assert.Equal(NoteWorkflow.Pending, survivorAfter.Status);
            Assert.Equal(completedOn, survivorAfter.EventDate);
            Assert.Equal(NoteWorkflow.Cancelled, duplicateAfter.Status);
            Assert.Equal(formId, duplicateAfter.FormId);
            Assert.Equal(survivorId, attestation.EvidenceNoteId);
        }
        finally
        {
            await RemoveReviewAsync(personId);
        }
    }

    [Fact]
    public async Task TwoLiveLinkedNotesRemainAmbiguousForManualAttestation()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var completedOn = DateTime.Today.AddDays(-1);
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
                FirstName = "Live",
                LastName = "Duplicate Test",
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
            foreach (var status in new[] { NoteWorkflow.Scheduled, NoteWorkflow.Pending })
            {
                db.Notes.Add(new ServerNote
                {
                    PersonId = personId,
                    AgencyId = 1,
                    Narrative = "Prepare the Safety Plan.",
                    EventDate = completedOn,
                    Status = status,
                    Minutes = 15,
                    NoteType = (int)NoteType.Form,
                    Activities = (int)NoteActivity.Form,
                    FormType = (int)FormType.SafetyPlan,
                    FormId = formId
                });
            }
            await db.SaveChangesAsync();
        }

        try
        {
            using var response = await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/SafetyPlan/attestation",
                new AttestFormRequest(formId, completedOn));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!;
            Assert.Equal("ambiguous_form_note", problem.Code);
            Assert.Contains("Several notes are linked", problem.Message,
                StringComparison.Ordinal);
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

    private async Task<(int PersonId, int OlderFormId, int RenewalFormId,
        DateTime OlderTarget, DateTime RenewalTarget)> CreateAssessmentOverlapAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var renewalTarget = DateTime.Today.AddDays(90);
        var olderTarget = renewalTarget.AddYears(-1);
        var person = new ServerPerson
        {
            Id = await db.People.MaxAsync(row => row.Id) + 1,
            UserId = 12,
            AgencyId = 1,
            FirstName = "Cycle",
            LastName = "Disambiguation Test",
            BirthDate = new DateTime(1990, 1, 1),
            EffectiveDate = olderTarget
        };
        var older = new ServerForm
        {
            PersonId = person.Id,
            Type = "ComprehensiveAssessment",
            DueDate = olderTarget.AddDays(-90),
            TargetEffectiveDate = olderTarget
        };
        var renewal = new ServerForm
        {
            PersonId = person.Id,
            Type = "ComprehensiveAssessment",
            DueDate = DateTime.Today,
            TargetEffectiveDate = renewalTarget
        };
        db.People.Add(person);
        db.Forms.AddRange(older, renewal);
        await db.SaveChangesAsync();
        return (person.Id, older.Id, renewal.Id, olderTarget, renewalTarget);
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
