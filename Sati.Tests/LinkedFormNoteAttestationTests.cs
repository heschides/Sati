using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class LinkedFormNoteAttestationTests
{
    [Fact]
    public async Task SubmittingLinkedReviewNoteAttestsExactFormOnActivityDate()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = today.AddDays(-90);
            var form = new Form(FormType.Q1R, today, targetEffectiveDate: person.EffectiveDate)
            {
                PersonId = person.Id
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
        }

        var note = Note.Create("Completed the review.", today, NoteStatus.Logged, 15,
            fixture.PersonOneId, FormType.Q1R, NoteType.Form, formId);
        note.GoalProgress = GoalProgressLevel.None;
        note.CaseManagerJustification = "Review work was completed on the documented date.";

        await fixture.NotesFromAnotherSession().AddNoteAsync(note);

        await using var verification = fixture.Factory.CreateDbContext();
        var formAfter = await verification.Forms.Include(candidate => candidate.Attestations)
            .SingleAsync(candidate => candidate.Id == formId);
        Assert.Equal(today, formAfter.CompletedDate);
        var attestation = Assert.Single(formAfter.Attestations);
        Assert.Equal(today, attestation.CompletedOn);
        Assert.Equal(note.Id, attestation.EvidenceNoteId);
        Assert.Equal(formId, (await verification.Notes.SingleAsync(candidate => candidate.Id == note.Id)).FormId);
    }

    [Fact]
    public async Task LateReviewNoteCanBeLoggedButItsOwnWorkIsNotBillable()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var activityDate = DateTime.Today;
        var dueDate = activityDate.AddDays(-1);
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = dueDate.AddDays(-90);
            var form = new Form(FormType.Q1R, dueDate, targetEffectiveDate: person.EffectiveDate)
            {
                PersonId = person.Id
            };
            db.Forms.Add(form);
            db.Settings.Add(new Settings
            {
                AgencyId = fixture.CaseManagerOne.AgencyId,
                BillingComplianceRequirements = BillingComplianceRequirements.QuarterlyReviews
            });
            await db.SaveChangesAsync();
            formId = form.Id;
        }

        var note = Note.Create("Completed the review one day late.", activityDate,
            NoteStatus.Logged, 15, fixture.PersonOneId, FormType.Q1R, NoteType.Form, formId);
        note.GoalProgress = GoalProgressLevel.None;

        await fixture.NotesFromAnotherSession().AddNoteAsync(note);

        await using var verification = fixture.Factory.CreateDbContext();
        var formAfter = await verification.Forms.Include(candidate => candidate.Attestations)
            .SingleAsync(candidate => candidate.Id == formId);
        var savedNote = await verification.Notes.SingleAsync(candidate => candidate.Id == note.Id);
        Assert.Equal(NoteStatus.Logged, savedNote.Status);
        Assert.Null(savedNote.CaseManagerJustification);
        Assert.Equal(activityDate, formAfter.CompletedDate);
        Assert.Equal(note.Id, Assert.Single(formAfter.Attestations).EvidenceNoteId);

        var billing = FormWorkBillingRules.Evaluate(
            new FormWorkNoteFact(savedNote.PersonId, savedNote.FormType!.Value.ToString(),
                savedNote.EventDate, savedNote.FormId),
            new FormWorkObligationFact(formAfter.Id, formAfter.PersonId,
                formAfter.Type.ToString(), formAfter.DueDate, formAfter.CompletedDate));
        Assert.False(billing.Passed);
        Assert.Contains(billing.Reasons, reason => reason.Contains("completed after", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SameDateCompletionAddsNoteEvidenceWithoutChangingCompletionOrFlagging()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = today.AddDays(-90);
            var form = new Form(FormType.Q1R, today, targetEffectiveDate: person.EffectiveDate)
            {
                PersonId = person.Id
            };
            form.Attest(FormAttestation.Attested(today,
                AttestationActorKind.CaseManager, fixture.CaseManagerOne.Id,
                DateTime.UtcNow));
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
        }

        var note = Note.Create("The review was completed today.", today,
            NoteStatus.Logged, 15, fixture.PersonOneId, FormType.Q1R, NoteType.Form, formId);
        note.GoalProgress = GoalProgressLevel.None;
        note.CaseManagerJustification = "Review the unrelated annual compliance gap.";

        await fixture.NotesFromAnotherSession().AddNoteAsync(note);

        await using var verification = fixture.Factory.CreateDbContext();
        var formAfter = await verification.Forms.Include(candidate => candidate.Attestations)
            .SingleAsync(candidate => candidate.Id == formId);
        Assert.Equal(today, formAfter.CompletedDate);
        Assert.Equal(2, formAfter.Attestations.Count);
        var latest = formAfter.Attestations.OrderByDescending(candidate => candidate.Id).First();
        Assert.Equal(FormAttestationKind.Attested, latest.Kind);
        Assert.Equal(today, latest.CompletedOn);
        Assert.Equal(note.Id, latest.EvidenceNoteId);
        Assert.Empty(await verification.FormAttestationChangeReviewFlags.ToListAsync());
    }

    [Fact]
    public async Task ConflictingExistingDateRejectsNoteAndRollsBackNoteSave()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = today.AddDays(-90);
            var form = new Form(FormType.Q1R, today, targetEffectiveDate: person.EffectiveDate)
            {
                PersonId = person.Id
            };
            form.Attest(FormAttestation.Attested(today.AddDays(-1),
                AttestationActorKind.CaseManager, fixture.CaseManagerOne.Id,
                DateTime.UtcNow));
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
        }

        var note = Note.Create("Completed the review.", today, NoteStatus.Logged, 15,
            fixture.PersonOneId, FormType.Q1R, NoteType.Form, formId);
        note.GoalProgress = GoalProgressLevel.None;
        note.CaseManagerJustification = "The work occurred today.";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.NotesFromAnotherSession().AddNoteAsync(note));
        Assert.Contains("Enter a reason", error.Message);

        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Empty(await verification.Notes.Where(candidate => candidate.PersonId == fixture.PersonOneId).ToListAsync());
        var formAfter = await verification.Forms.SingleAsync(candidate => candidate.Id == formId);
        Assert.Equal(today.AddDays(-1), formAfter.CompletedDate);
    }

    [Fact]
    public async Task ReasonedLateDateCorrectionAppendsAuditAndSupervisorFlag()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var due = DateTime.Today.AddDays(-10);
        var actual = due.AddDays(1);
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = due.AddDays(-90);
            var form = new Form(FormType.Q1R, due, targetEffectiveDate: person.EffectiveDate)
            {
                PersonId = person.Id
            };
            form.Attest(FormAttestation.Attested(due,
                AttestationActorKind.CaseManager, fixture.CaseManagerOne.Id,
                DateTime.UtcNow));
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
        }

        var note = Note.Create("The review was completed the following day.", actual,
            NoteStatus.Logged, 15, fixture.PersonOneId, FormType.Q1R, NoteType.Form, formId);
        note.GoalProgress = GoalProgressLevel.None;
        note.FormDateCorrectionReason = "The original date was entered one day early.";
        note.CaseManagerJustification = "The actual work date is documented.";

        await fixture.NotesFromAnotherSession().AddNoteAsync(note);

        await using var verification = fixture.Factory.CreateDbContext();
        var formAfter = await verification.Forms.Include(candidate => candidate.Attestations)
            .SingleAsync(candidate => candidate.Id == formId);
        Assert.Equal(actual, formAfter.CompletedDate);
        Assert.Collection(formAfter.Attestations.OrderBy(candidate => candidate.Id),
            first => Assert.Equal(FormAttestationKind.Attested, first.Kind),
            revoke => Assert.Equal(FormAttestationKind.Revoked, revoke.Kind),
            replacement =>
            {
                Assert.Equal(FormAttestationKind.Attested, replacement.Kind);
                Assert.Equal(actual, replacement.CompletedOn);
                Assert.Equal(note.Id, replacement.EvidenceNoteId);
            });
        var flag = Assert.Single(await verification.FormAttestationChangeReviewFlags.ToListAsync());
        Assert.Equal(note.Id, flag.NoteId);
        Assert.True(flag.RequiresSupervisorAttention);
        Assert.True(flag.MustHoldBilling);
    }

    [Fact]
    public void NewSubmittedReviewNoteRequiresExactObligationWhileReleaseIsExempt()
    {
        Assert.NotNull(FormNoteLinkRules.Validate("Form", "Q1R", "Logged", null, null));
        Assert.Null(FormNoteLinkRules.Validate("Form", "Q1R", "Pending", null, null));
        Assert.Null(FormNoteLinkRules.Validate("Form", "Release_DHHS", "Logged", null, null));
        Assert.NotNull(FormNoteLinkRules.Validate("Visit", "Q1R", "Logged", null, null,
            (int)(NoteActivity.Visit | NoteActivity.Form)));
    }
}
