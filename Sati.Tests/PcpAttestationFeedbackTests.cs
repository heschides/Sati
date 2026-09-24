using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels;
using Xunit;

namespace Sati.Tests;

public sealed class PcpAttestationFeedbackTests
{
    [Theory]
    [InlineData(NoteWorkflow.Cancelled, false, true)]
    [InlineData(NoteWorkflow.Cancelled, true, false)]
    [InlineData(NoteWorkflow.Delayed, true, true)]
    [InlineData(NoteWorkflow.Abandoned, true, true)]
    [InlineData(NoteWorkflow.Returned, true, true)]
    public void OnlyAuditBackedMigrationCancellationLeavesManualAttestationResolution(
        int status,
        bool hasScheduledDuplicateCancellationAudit,
        bool expected)
    {
        Assert.Equal(expected, ManualAttestationNoteRules.CompetesForManualAttestation(
            status, hasScheduledDuplicateCancellationAudit));
    }

    [Fact]
    public async Task ManualOlderCycleAttestationRefusesWhenIncompleteRenewalIsAvailable()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var completedOn = DateTime.Today;
        var renewalTarget = completedOn.AddDays(90);
        var olderTarget = renewalTarget.AddYears(-1);
        Form older;
        int renewalFormId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(row => row.Id == fixture.PersonOneId);
            person.EffectiveDate = olderTarget;
            older = new Form(
                FormType.ComprehensiveAssessment,
                olderTarget.AddDays(-90),
                targetEffectiveDate: olderTarget)
            {
                PersonId = person.Id
            };
            var renewal = new Form(
                FormType.ComprehensiveAssessment,
                completedOn,
                targetEffectiveDate: renewalTarget)
            {
                PersonId = person.Id
            };
            db.Forms.AddRange(older, renewal);
            await db.SaveChangesAsync();
            renewalFormId = renewal.Id;
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FormService(fixture.Factory, session).AttestAsync(older, completedOn));

        Assert.Contains(olderTarget.ToString("MMMM d, yyyy"), error.Message,
            StringComparison.Ordinal);
        Assert.Contains(renewalTarget.ToString("MMMM d, yyyy"), error.Message,
            StringComparison.Ordinal);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Null((await verify.Forms.AsNoTracking()
            .SingleAsync(row => row.Id == older.Id)).CompletedDate);
        Assert.Null((await verify.Forms.AsNoTracking()
            .SingleAsync(row => row.Id == renewalFormId)).CompletedDate);
        Assert.Empty(await verify.FormAttestations.AsNoTracking().ToListAsync());
        Assert.Empty(await verify.Notes.AsNoTracking().ToListAsync());
    }

    [Fact]
    public void ScheduledConversionKeepsMixedAndClaimedNotesInCorrectionWorkflow()
    {
        Assert.True(ManualAttestationNoteRules.CanConvertScheduledFormNote(
            NoteWorkflow.Scheduled, "Form", (int)NoteActivity.Form, false));
        Assert.True(ManualAttestationNoteRules.CanConvertScheduledFormNote(
            NoteWorkflow.Scheduled, "Form", null, false));
        Assert.False(ManualAttestationNoteRules.CanConvertScheduledFormNote(
            NoteWorkflow.Scheduled, "Form", (int)(NoteActivity.Form | NoteActivity.Phone), false));
        Assert.False(ManualAttestationNoteRules.CanConvertScheduledFormNote(
            NoteWorkflow.Scheduled, "Form", (int)NoteActivity.Form, true));
        Assert.False(ManualAttestationNoteRules.CanConvertScheduledFormNote(
            NoteWorkflow.Logged, "Form", (int)NoteActivity.Form, false));
    }

    [Fact]
    public void MovedScheduledFormNoteExplainsPlannedDateVersusCompletionDate()
    {
        var explanation = ManualAttestationNoteRules.Conflict(
            new DateTime(2026, 9, 21),
            new DateTime(2026, 9, 23),
            NoteWorkflow.Scheduled,
            hasClaimLine: false);

        Assert.Contains("(planned)", explanation);
        Assert.Contains("(actual)", explanation);
        Assert.Contains("Sep 21, 2026", explanation);
        Assert.Contains("Sep 23, 2026", explanation);
        Assert.Contains("save as Pending", explanation);
        Assert.DoesNotContain("earlier attestation", explanation);
    }

    [Fact]
    public async Task ConfirmedSafetyPlanAttestationReusesMovedScheduledNote()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var completedOn = DateTime.Today.AddDays(-1);
        var plannedOn = DateTime.Today.AddDays(1);
        Form form;
        int noteId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(row => row.Id == fixture.PersonOneId);
            person.EffectiveDate = completedOn.AddYears(-1);
            form = new Form(FormType.SafetyPlan, completedOn,
                targetEffectiveDate: completedOn) { PersonId = person.Id };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            var note = Note.Create("Prepare the Safety Plan.", plannedOn,
                NoteStatus.Scheduled, 15, person.Id,
                FormType.SafetyPlan, NoteType.Form, form.Id);
            note.Activities = NoteActivity.Form;
            note.AgencyId = fixture.CaseManagerOne.AgencyId;
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            noteId = note.Id;
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new FormService(fixture.Factory, session);

        var prompt = await Assert.ThrowsAsync<ScheduledFormNoteConversionRequiredException>(
            () => service.AttestAsync(form, completedOn));
        Assert.Contains("planned note", prompt.Message);
        Assert.Contains("actual work", prompt.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AttestAsync(form, completedOn, noteId, true, "stale"));
        await using (var unchanged = fixture.Factory.CreateDbContext())
        {
            Assert.Null((await unchanged.Forms.SingleAsync(row => row.Id == form.Id)).CompletedDate);
            var note = await unchanged.Notes.SingleAsync(row => row.Id == noteId);
            Assert.Equal(plannedOn, note.EventDate);
            Assert.Equal(NoteStatus.Scheduled, note.Status);
        }

        var panel = new FormAttestationViewModel(service);
        panel.Begin(form, completedOn.AddYears(-1), "Safety Plan");
        panel.CompletionDate = completedOn;
        await panel.CompleteAttestationCommand.ExecuteAsync(null);
        Assert.True(panel.HasScheduledNoteConversionPrompt);
        Assert.Null(form.CompletedDate);
        panel.CancelScheduledNoteConversionCommand.Execute(null);
        Assert.False(panel.HasScheduledNoteConversionPrompt);
        await panel.CompleteAttestationCommand.ExecuteAsync(null);
        Assert.True(panel.ConfirmScheduledNoteConversionCommand.CanExecute(null));
        await panel.ConfirmScheduledNoteConversionCommand.ExecuteAsync(null);
        Assert.False(panel.HasScheduledNoteConversionPrompt);

        await using var verification = fixture.Factory.CreateDbContext();
        var savedNote = await verification.Notes.SingleAsync(row => row.FormId == form.Id);
        var savedForm = await verification.Forms.Include(row => row.Attestations)
            .SingleAsync(row => row.Id == form.Id);
        Assert.Equal(noteId, savedNote.Id);
        Assert.Equal(completedOn, savedNote.EventDate);
        Assert.Equal(NoteStatus.Pending, savedNote.Status);
        Assert.Equal(2, savedNote.Revision);
        Assert.Equal(completedOn, savedForm.CompletedDate);
        Assert.Equal(noteId, Assert.Single(savedForm.Attestations).EvidenceNoteId);
    }

    [Fact]
    public async Task MigrationCancelledDuplicateDoesNotBlockScheduledNoteConversion()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var completedOn = DateTime.Today.AddDays(-1);
        var plannedOn = DateTime.Today.AddDays(1);
        Form form;
        int survivorId;
        int cancelledDuplicateId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(row => row.Id == fixture.PersonOneId);
            person.EffectiveDate = completedOn.AddYears(-1);
            form = new Form(FormType.SafetyPlan, completedOn,
                targetEffectiveDate: completedOn) { PersonId = person.Id };
            db.Forms.Add(form);
            await db.SaveChangesAsync();

            var survivor = Note.Create("Prepare the Safety Plan.", plannedOn,
                NoteStatus.Scheduled, 15, person.Id,
                FormType.SafetyPlan, NoteType.Form, form.Id);
            survivor.Activities = NoteActivity.Form;
            survivor.AgencyId = fixture.CaseManagerOne.AgencyId;
            var cancelledDuplicate = Note.Create("Prepare the Safety Plan.", plannedOn,
                NoteStatus.Cancelled, 15, person.Id,
                FormType.SafetyPlan, NoteType.Form, form.Id);
            cancelledDuplicate.Activities = NoteActivity.Form;
            cancelledDuplicate.AgencyId = fixture.CaseManagerOne.AgencyId;
            db.Notes.AddRange(survivor, cancelledDuplicate);
            await db.SaveChangesAsync();
            survivorId = survivor.Id;
            cancelledDuplicateId = cancelledDuplicate.Id;
            db.AuditEvents.Add(new AuditEvent
            {
                AgencyId = fixture.CaseManagerOne.AgencyId,
                ActorUserId = fixture.CaseManagerOne.Id,
                Action = ManualAttestationNoteRules.ScheduledDuplicateCancellationAuditAction,
                ResourceType = "Note",
                ResourceId = cancelledDuplicateId.ToString(),
                CorrelationId = "test-scheduled-duplicate-repair"
            });
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new FormService(fixture.Factory, session);

        var prompt = await Assert.ThrowsAsync<ScheduledFormNoteConversionRequiredException>(
            () => service.AttestAsync(form, completedOn));
        await service.AttestAsync(form, completedOn, survivorId,
            confirmScheduledNoteConversion: true, prompt.ConfirmationToken);

        await using var verification = fixture.Factory.CreateDbContext();
        var survivorAfter = await verification.Notes.SingleAsync(row => row.Id == survivorId);
        var duplicateAfter = await verification.Notes.SingleAsync(row => row.Id == cancelledDuplicateId);
        var formAfter = await verification.Forms.Include(row => row.Attestations)
            .SingleAsync(row => row.Id == form.Id);
        Assert.Equal(NoteStatus.Pending, survivorAfter.Status);
        Assert.Equal(completedOn, survivorAfter.EventDate);
        Assert.Equal(NoteStatus.Cancelled, duplicateAfter.Status);
        Assert.Equal(form.Id, duplicateAfter.FormId);
        Assert.Equal(cancelledDuplicateId, duplicateAfter.Id);
        Assert.Equal(survivorId, Assert.Single(formAfter.Attestations).EvidenceNoteId);
    }

    [Fact]
    public async Task TwoLiveLinkedNotesRemainAmbiguousForManualAttestation()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var completedOn = DateTime.Today.AddDays(-1);
        Form form;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(row => row.Id == fixture.PersonOneId);
            person.EffectiveDate = completedOn.AddYears(-1);
            form = new Form(FormType.SafetyPlan, completedOn,
                targetEffectiveDate: completedOn) { PersonId = person.Id };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            foreach (var status in new[] { NoteStatus.Scheduled, NoteStatus.Pending })
            {
                var note = Note.Create("Prepare the Safety Plan.", completedOn,
                    status, 15, person.Id,
                    FormType.SafetyPlan, NoteType.Form, form.Id);
                note.Activities = NoteActivity.Form;
                note.AgencyId = fixture.CaseManagerOne.AgencyId;
                db.Notes.Add(note);
            }
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FormService(fixture.Factory, session).AttestAsync(form, completedOn));

        Assert.Contains("Several notes are linked", error.Message, StringComparison.Ordinal);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Null((await verification.Forms.SingleAsync(row => row.Id == form.Id)).CompletedDate);
        Assert.Empty(await verification.FormAttestations.ToListAsync());
    }

    [Fact]
    public async Task PrivacyAttestationDoesNotPreventPcpOrSafetyPlanForSameTarget()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var target = DateTime.Today.AddDays(-1);
        Form[] forms;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(row => row.Id == fixture.PersonOneId);
            person.EffectiveDate = target.AddYears(-1);
            forms = new[] { FormType.PrivacyPractices, FormType.PCP, FormType.SafetyPlan }
                .Select(type => new Form(type, target, targetEffectiveDate: target)
                {
                    PersonId = person.Id
                })
                .ToArray();
            db.Forms.AddRange(forms);
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new FormService(fixture.Factory, session);
        foreach (var form in forms)
            await service.AttestAsync(form, target);

        await using var verification = fixture.Factory.CreateDbContext();
        var formIds = forms.Select(form => form.Id).ToArray();
        var saved = await verification.Forms
            .Where(row => formIds.Contains(row.Id))
            .ToListAsync();
        Assert.Equal(3, saved.Count);
        Assert.All(saved, form => Assert.Equal(target, form.CompletedDate));
        Assert.Equal(3, await verification.Notes.CountAsync(row =>
            row.PersonId == fixture.PersonOneId && row.FormId != null));
    }

    [Fact]
    public async Task ManualPcpAttestationOnEffectiveDateCitesItsNewDraft()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var target = DateTime.Today.AddDays(-1);
        Form form;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(row => row.Id == fixture.PersonOneId);
            person.EffectiveDate = target.AddYears(-1);
            form = new Form(FormType.PCP, target, targetEffectiveDate: target)
            {
                PersonId = person.Id
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        await new FormService(fixture.Factory, session).AttestAsync(form, target);

        await using var verification = fixture.Factory.CreateDbContext();
        var saved = await verification.Forms.Include(row => row.Attestations)
            .SingleAsync(row => row.Id == form.Id);
        var note = await verification.Notes.SingleAsync(row => row.FormId == form.Id);
        Assert.Equal(target, saved.CompletedDate);
        Assert.Equal(target, note.EventDate);
        Assert.Equal(note.Id, Assert.Single(saved.Attestations).EvidenceNoteId);
    }

    [Fact]
    public async Task UnlinkedPcpNoteOnEffectiveDateCannotProduceDuplicateDraft()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var target = DateTime.Today.AddDays(-1);
        Form form;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(row => row.Id == fixture.PersonOneId);
            person.EffectiveDate = target.AddYears(-1);
            form = new Form(FormType.PCP, target, targetEffectiveDate: target)
            {
                PersonId = person.Id
            };
            db.Forms.Add(form);
            var note = Note.Create("Completed the PCP.", target,
                NoteStatus.Logged, 15, person.Id, FormType.PCP, NoteType.Form);
            note.AgencyId = fixture.CaseManagerOne.AgencyId;
            note.GoalProgress = GoalProgressLevel.None;
            db.Notes.Add(note);
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new FormService(fixture.Factory, session);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AttestAsync(form, target));

        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Empty(await verification.FormAttestations.ToListAsync());
        Assert.Single(await verification.Notes.ToListAsync());
    }

    [Theory]
    [InlineData(FormType.PCP)]
    [InlineData(FormType.SafetyPlan)]
    public async Task ExistingUnlinkedFormNoteExplainsWhyAttestationWasNotSaved(
        FormType formType)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var target = DateTime.Today.AddDays(30);
        var effectiveDate = target.AddYears(-1);
        Form form;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(row => row.Id == fixture.PersonOneId);
            person.EffectiveDate = effectiveDate;
            form = new Form(formType, target, targetEffectiveDate: target)
            {
                PersonId = person.Id
            };
            db.Forms.Add(form);
            var note = Note.Create("Completed the form.", DateTime.Today,
                NoteStatus.Logged, 15, person.Id, formType, NoteType.Form);
            note.AgencyId = fixture.CaseManagerOne.AgencyId;
            note.GoalProgress = GoalProgressLevel.None;
            db.Notes.Add(note);
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var viewModel = new FormAttestationViewModel(
            new FormService(fixture.Factory, session));
        viewModel.Begin(form, effectiveDate, $"{formType} renewal");
        viewModel.CompletionDate = DateTime.Today;
        viewModel.HasConfirmedEvergreenCompletion = true;

        Assert.True(viewModel.CompleteAttestationCommand.CanExecute(null));
        await viewModel.CompleteAttestationCommand.ExecuteAsync(null);

        Assert.Contains("unlinked form note", viewModel.PrerequisiteError,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supervisor", viewModel.PrerequisiteError,
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(form.CompletedDate);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Empty(await verification.FormAttestations.ToListAsync());
        Assert.Single(await verification.Notes.ToListAsync());
    }

    [Fact]
    public void AttestationFailureMessageIsVisibleForPcpAsWellAsReclassification()
    {
        var view = XDocument.Load(Path.Combine(RepositoryRoot(),
            "Views", "FormAttestationControl.xaml"));
        var error = Assert.Single(view.Descendants(), element =>
            element.Name.LocalName == "TextBlock" &&
            (string?)element.Attribute("Text") == "{Binding PrerequisiteError}");

        Assert.DoesNotContain(error.Ancestors(), ancestor =>
            ((string?)ancestor.Attribute("Visibility"))?.Contains(
                "IsReclassification", StringComparison.Ordinal) == true);
    }

    private static string RepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, ".."));
}
