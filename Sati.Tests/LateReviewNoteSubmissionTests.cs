using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Views;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class LateReviewNoteSubmissionTests
{
    [Fact]
    public async Task OlderAssessmentSelectionRequiresDisambiguationWhenRenewalWindowIsOpen()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var activityDate = DateTime.Today;
        var renewalTarget = activityDate.AddDays(90);
        var olderTarget = renewalTarget.AddYears(-1);
        int olderFormId;
        int renewalFormId;
        await using (var seed = fixture.Factory.CreateDbContext())
        {
            var storedPerson = await seed.People.SingleAsync(candidate =>
                candidate.Id == fixture.PersonOneId);
            storedPerson.EffectiveDate = olderTarget;
            var older = new Form(
                FormType.ComprehensiveAssessment,
                olderTarget.AddDays(-90),
                targetEffectiveDate: olderTarget)
            {
                PersonId = storedPerson.Id
            };
            var renewal = new Form(
                FormType.ComprehensiveAssessment,
                activityDate,
                targetEffectiveDate: renewalTarget)
            {
                PersonId = storedPerson.Id
            };
            seed.Forms.AddRange(older, renewal);
            seed.Settings.Add(new Settings
            {
                AgencyId = fixture.CaseManagerOne.AgencyId,
                BillingComplianceRequirements =
                    BillingComplianceRequirements.ComprehensiveAssessment
            });
            await seed.SaveChangesAsync();
            olderFormId = older.Id;
            renewalFormId = renewal.Id;
        }

        Person person;
        await using (var read = fixture.Factory.CreateDbContext())
        {
            person = await read.People.AsNoTracking()
                .Include(candidate => candidate.Forms)
                .SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
        }
        var panel = fixture.NoteEntry(settings: new AssessmentOnlySettingsService());
        FillAssessmentNote(panel, person, olderFormId, activityDate);

        await panel.SubmitNoteCommand.ExecuteAsync(null);

        Assert.True(panel.IsComplianceDialogVisible);
        var reason = Assert.Single(panel.ComplianceFailureReasons);
        Assert.Contains(olderTarget.ToString("MMMM d, yyyy"), reason, StringComparison.Ordinal);
        Assert.Contains(renewalTarget.ToString("MMMM d, yyyy"), reason, StringComparison.Ordinal);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Empty(await verify.Notes.AsNoTracking().ToListAsync());
        Assert.Null((await verify.Forms.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == olderFormId)).CompletedDate);
        Assert.Null((await verify.Forms.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == renewalFormId)).CompletedDate);
    }

    [Fact]
    public async Task SelectingTheIncompleteRenewalAttestsThatExactForm()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var activityDate = DateTime.Today;
        var renewalTarget = activityDate.AddDays(90);
        var olderTarget = renewalTarget.AddYears(-1);
        int olderFormId;
        int renewalFormId;
        await using (var seed = fixture.Factory.CreateDbContext())
        {
            var storedPerson = await seed.People.SingleAsync(candidate =>
                candidate.Id == fixture.PersonOneId);
            storedPerson.EffectiveDate = olderTarget;
            var older = new Form(
                FormType.ComprehensiveAssessment,
                olderTarget.AddDays(-90),
                completedOn: olderTarget.AddDays(-90),
                targetEffectiveDate: olderTarget)
            {
                PersonId = storedPerson.Id
            };
            var renewal = new Form(
                FormType.ComprehensiveAssessment,
                activityDate,
                targetEffectiveDate: renewalTarget)
            {
                PersonId = storedPerson.Id
            };
            seed.Forms.AddRange(older, renewal);
            seed.Settings.Add(new Settings
            {
                AgencyId = fixture.CaseManagerOne.AgencyId,
                BillingComplianceRequirements =
                    BillingComplianceRequirements.ComprehensiveAssessment
            });
            await seed.SaveChangesAsync();
            olderFormId = older.Id;
            renewalFormId = renewal.Id;
        }

        Person person;
        await using (var read = fixture.Factory.CreateDbContext())
        {
            person = await read.People.AsNoTracking()
                .Include(candidate => candidate.Forms)
                .SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
        }
        var panel = fixture.NoteEntry(settings: new AssessmentOnlySettingsService());
        FillAssessmentNote(panel, person, renewalFormId, activityDate);

        await panel.SubmitNoteCommand.ExecuteAsync(null);

        Assert.False(panel.IsComplianceDialogVisible);
        Assert.Null(panel.SubmissionFailureMessage);
        await using var verify = fixture.Factory.CreateDbContext();
        var note = Assert.Single(await verify.Notes.AsNoTracking().ToListAsync());
        Assert.Equal(renewalFormId, note.FormId);
        Assert.Equal(olderTarget.AddDays(-90), (await verify.Forms.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == olderFormId)).CompletedDate);
        var renewalAfter = await verify.Forms.AsNoTracking()
            .Include(candidate => candidate.Attestations)
            .SingleAsync(candidate => candidate.Id == renewalFormId);
        Assert.Equal(activityDate, renewalAfter.CompletedDate);
        Assert.Equal(note.Id, Assert.Single(renewalAfter.Attestations).EvidenceNoteId);
    }

    [Fact]
    public async Task OlderAssessmentCanProceedOnlyWithWrittenJustificationAndIsAudited()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var activityDate = DateTime.Today;
        var renewalTarget = activityDate.AddDays(90);
        var olderTarget = renewalTarget.AddYears(-1);
        int olderFormId;
        int renewalFormId;
        await using (var seed = fixture.Factory.CreateDbContext())
        {
            var storedPerson = await seed.People.SingleAsync(candidate =>
                candidate.Id == fixture.PersonOneId);
            storedPerson.EffectiveDate = olderTarget;
            var older = new Form(
                FormType.ComprehensiveAssessment,
                olderTarget.AddDays(-90),
                targetEffectiveDate: olderTarget)
            {
                PersonId = storedPerson.Id
            };
            var renewal = new Form(
                FormType.ComprehensiveAssessment,
                activityDate,
                targetEffectiveDate: renewalTarget)
            {
                PersonId = storedPerson.Id
            };
            seed.Forms.AddRange(older, renewal);
            seed.Settings.Add(new Settings
            {
                AgencyId = fixture.CaseManagerOne.AgencyId,
                BillingComplianceRequirements =
                    BillingComplianceRequirements.ComprehensiveAssessment
            });
            await seed.SaveChangesAsync();
            olderFormId = older.Id;
            renewalFormId = renewal.Id;
        }

        const string justification =
            "Source evidence confirms this was genuinely late work for the older plan.";
        var note = Note.Create(
            "Completed the older-plan Comprehensive Assessment.",
            activityDate,
            NoteStatus.Logged,
            30,
            fixture.PersonOneId,
            FormType.ComprehensiveAssessment,
            NoteType.Form,
            olderFormId);
        note.Activities = NoteActivity.Form;
        note.GoalProgress = GoalProgressLevel.Moderate;
        note.CaseManagerJustification = justification;

        await fixture.NotesFromAnotherSession().AddNoteAsync(note);

        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Equal(activityDate, (await verify.Forms.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == olderFormId)).CompletedDate);
        Assert.Null((await verify.Forms.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == renewalFormId)).CompletedDate);
        var audit = await verify.AuditEvents.AsNoTracking().SingleAsync(candidate =>
            candidate.Action == LocalAuditActions.NoteOlderCycleJustified);
        Assert.Equal(note.Id.ToString(), audit.ResourceId);
        Assert.Contains($"\"selectedFormId\":{olderFormId}", audit.MetadataJson,
            StringComparison.Ordinal);
        Assert.Contains($"\"renewalFormId\":{renewalFormId}", audit.MetadataJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(justification, audit.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactLinkedFormNoteCanCompleteItsOwnOverdueObligation()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var targetEffectiveDate = today.AddDays(89);
        int formId;
        await using (var seed = fixture.Factory.CreateDbContext())
        {
            var storedPerson = await seed.People.SingleAsync(candidate =>
                candidate.Id == fixture.PersonOneId);
            storedPerson.EffectiveDate = targetEffectiveDate.AddYears(-1);
            var form = new Form(
                FormType.ComprehensiveAssessment,
                today.AddDays(-1),
                targetEffectiveDate: targetEffectiveDate)
            {
                PersonId = storedPerson.Id
            };
            var priorForm = new Form(
                FormType.ComprehensiveAssessment,
                today.AddYears(-1).AddDays(-1),
                completedOn: today.AddYears(-1).AddDays(-1),
                targetEffectiveDate: targetEffectiveDate.AddYears(-1))
            {
                PersonId = storedPerson.Id
            };
            seed.Forms.AddRange(form, priorForm);
            seed.Settings.Add(new Settings
            {
                AgencyId = fixture.CaseManagerOne.AgencyId,
                BillingComplianceRequirements =
                    BillingComplianceRequirements.ComprehensiveAssessment
            });
            await seed.SaveChangesAsync();
            formId = form.Id;
        }

        Person person;
        await using (var read = fixture.Factory.CreateDbContext())
        {
            person = await read.People.AsNoTracking()
                .Include(candidate => candidate.Forms)
                .SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
        }
        var panel = fixture.NoteEntry(settings: new AssessmentOnlySettingsService());
        FillAssessmentNote(panel, person, formId, today);

        await panel.SubmitNoteCommand.ExecuteAsync(null);

        Assert.False(panel.IsComplianceDialogVisible);
        Assert.Null(panel.SubmissionFailureMessage);
        await using var verify = fixture.Factory.CreateDbContext();
        var note = Assert.Single(await verify.Notes.AsNoTracking().ToListAsync());
        Assert.Equal(NoteStatus.Logged, note.Status);
        Assert.Equal(formId, note.FormId);
        var completedForm = await verify.Forms.AsNoTracking()
            .Include(candidate => candidate.Attestations)
            .SingleAsync(candidate => candidate.Id == formId);
        Assert.Equal(today, completedForm.CompletedDate);
        Assert.Equal(note.Id, Assert.Single(completedForm.Attestations).EvidenceNoteId);

        // Passing the ordinary billing-window preflight only lets the note and its
        // attestation reach clinical review. The independent form-work rule still
        // keeps work completed after its own due date out of billing.
        var formWork = FormWorkBillingRules.Evaluate(
            new FormWorkNoteFact(
                note.PersonId,
                note.FormType!.Value.ToString(),
                note.EventDate,
                note.FormId),
            new FormWorkObligationFact(
                completedForm.Id,
                completedForm.PersonId,
                completedForm.Type.ToString(),
                completedForm.DueDate,
                completedForm.CompletedDate));
        Assert.False(formWork.Passed);
        Assert.Contains(formWork.Reasons,
            reason => reason.Contains("completed after", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExactLinkedFormNoteDoesNotExcuseAnotherOverdueAssessment()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var person = await fixture.PersonOneAsync();
        var selected = new Form(
            FormType.ComprehensiveAssessment,
            today.AddDays(-1),
            targetEffectiveDate: today.AddDays(89))
        {
            Id = 701,
            PersonId = person.Id
        };
        person.Forms.Add(selected);
        person.Forms.Add(new Form(
            FormType.ComprehensiveAssessment,
            today.AddDays(-2),
            targetEffectiveDate: today.AddDays(-276))
        {
            Id = 702,
            PersonId = person.Id
        });
        var panel = fixture.NoteEntry(settings: new AssessmentOnlySettingsService());
        FillAssessmentNote(panel, person, selected.Id, today);

        await panel.SubmitNoteCommand.ExecuteAsync(null);

        Assert.True(panel.IsComplianceDialogVisible);
        Assert.Single(panel.ComplianceFailureReasons);
        Assert.Contains("Comprehensive Assessment", panel.ComplianceFailureReasons[0],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotesLogCanSubmitAnExactLinkedFormNoteThatCompletesItsOwnBlocker()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var targetEffectiveDate = today.AddDays(89);
        int formId;
        await using (var seed = fixture.Factory.CreateDbContext())
        {
            var person = await seed.People.SingleAsync(candidate =>
                candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = targetEffectiveDate.AddYears(-1);
            var form = new Form(
                FormType.ComprehensiveAssessment,
                today.AddDays(-1),
                targetEffectiveDate: targetEffectiveDate)
            {
                PersonId = person.Id
            };
            seed.Forms.AddRange(
                form,
                new Form(
                    FormType.ComprehensiveAssessment,
                    today.AddYears(-1).AddDays(-1),
                    completedOn: today.AddYears(-1).AddDays(-1),
                    targetEffectiveDate: targetEffectiveDate.AddYears(-1))
                {
                    PersonId = person.Id
                });
            seed.Settings.Add(new Settings
            {
                AgencyId = fixture.CaseManagerOne.AgencyId,
                BillingComplianceRequirements =
                    BillingComplianceRequirements.ComprehensiveAssessment
            });
            await seed.SaveChangesAsync();
            formId = form.Id;
        }

        var pending = Note.Create(
            "Completed the Comprehensive Assessment.",
            today,
            NoteStatus.Pending,
            30,
            fixture.PersonOneId,
            FormType.ComprehensiveAssessment,
            NoteType.Form,
            formId);
        pending.Activities = NoteActivity.Form;
        pending.GoalProgress = GoalProgressLevel.Moderate;
        await fixture.NotesFromAnotherSession().AddNoteAsync(pending);
        var log = fixture.NotesWindow(settings: new AssessmentOnlySettingsService());
        await log.ReloadAsync();
        log.SelectedNote = Assert.Single(log.NotesView.Cast<Note>());

        await log.MarkNoteLoggedCommand.ExecuteAsync(null);

        Assert.False(log.IsComplianceDialogVisible);
        Assert.Equal(NoteStatus.Logged, log.SelectedNote.Status);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Equal(today, (await verify.Forms.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == formId)).CompletedDate);
    }

    [Fact]
    public async Task LateReviewEvidenceCanBeSavedOnceAsNonbillable()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        var serviceDate = new DateTime(2026, 8, 20);
        person.Forms.Add(new Form(FormType.Q4R, serviceDate.AddDays(-1),
            targetEffectiveDate: serviceDate.AddDays(-1).Date));
        panel.SetPeople([person]);
        panel.SelectedPerson = person;
        panel.SelectedNoteType = NoteType.Form;
        panel.SelectedFormType = FormType.Q4R;
        panel.Status = NoteStatus.Logged;
        panel.EventDate = serviceDate;
        panel.Minutes = 30;
        panel.GoalProgress = GoalProgressLevel.Moderate;
        panel.Narrative = "Completed the Q4 90-day review meeting.";

        await panel.SubmitNoteCommand.ExecuteAsync(null);

        Assert.True(panel.IsComplianceDialogVisible);
        var reason = Assert.Single(panel.ComplianceFailureReasons);
        Assert.Contains("Q4 Review", reason, StringComparison.Ordinal);

        await panel.HoldForComplianceCommand.ExecuteAsync(null);

        await using var db = fixture.Factory.CreateDbContext();
        var saved = Assert.Single(await db.Notes.AsNoTracking().ToListAsync());
        Assert.Equal(NoteStatus.ComplianceBlocked, saved.Status);
        Assert.Equal(NoteType.Form, saved.NoteType);
        Assert.Equal(FormType.Q4R, saved.FormType);
        Assert.Equal(serviceDate, saved.EventDate);
    }

    [Fact]
    public async Task BillingGapDialogSaysTheNoteCanBeSavedAsNonbillable()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        panel.IsComplianceDialogVisible = true;

        WpfUiHarness.Run(() =>
        {
            var view = new NoteEntryView { DataContext = panel };
            WpfUiHarness.Realize(view);
            var saveNonbillable = WpfUiHarness.FindByAutomationName<Button>(
                view, "Save this note as nonbillable");
            Assert.Equal("Save as Nonbillable", saveNonbillable.Content);
        });
    }

    private static void FillAssessmentNote(
        ViewModels.Children.NoteEntryViewModel panel,
        Person person,
        int formId,
        DateTime eventDate)
    {
        panel.SetPeople([person]);
        panel.SelectedPerson = person;
        panel.IsFormSelected = true;
        panel.SelectedFormType = FormType.ComprehensiveAssessment;
        panel.SelectedFormObligation = Assert.Single(
            panel.FormObligations, candidate => candidate.FormId == formId);
        panel.Status = NoteStatus.Logged;
        panel.EventDate = eventDate;
        panel.Minutes = 30;
        panel.GoalProgress = GoalProgressLevel.Moderate;
        panel.Narrative = "Completed the Comprehensive Assessment.";
    }

    private sealed class AssessmentOnlySettingsService : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(new Settings
        {
            BillingComplianceRequirements =
                BillingComplianceRequirements.ComprehensiveAssessment
        });

        public Task<BillingComplianceRequirements> ResolveBillingComplianceRequirementsAsync(
            DateTime serviceDate) => Task.FromResult(
                BillingComplianceRequirements.ComprehensiveAssessment);

        public Task SaveAsync(Settings settings) => Task.CompletedTask;
    }
}
