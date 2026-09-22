using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class AdminFormNoteCorrectionServiceTests
{
    [Fact]
    public async Task RevokedFormUsesItsLastAttestedDateAsCorrectionEvidence()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var due = DateTime.Today;
        var prior = due.AddDays(-2);
        var corrected = due.AddDays(-1);
        var admin = User.Create(78, "revoked-date-admin", "Date Administrator",
            "hash", "salt", UserRole.Admin, null, fixture.CaseManagerOne.AgencyId);
        int noteId;
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Users.Add(admin);
            var person = await db.People.SingleAsync(row => row.Id == fixture.PersonOneId);
            person.EffectiveDate = due.AddMonths(-6);
            var form = new Form(FormType.Q3R, due, targetEffectiveDate: due.AddDays(-270))
            {
                PersonId = person.Id
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            form.Attest(FormAttestation.Attested(prior, AttestationActorKind.CaseManager,
                fixture.CaseManagerOne.Id, DateTime.UtcNow));
            var note = Note.Create("Completed review.", prior, NoteStatus.Logged,
                15, person.Id, FormType.Q3R, NoteType.Form, formId: form.Id);
            note.AgencyId = admin.AgencyId;
            note.GoalProgress = GoalProgressLevel.None;
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            form.RevokeAttestation(FormAttestation.Revoked(
                AttestationActorKind.CaseManager, fixture.CaseManagerOne.Id,
                DateTime.UtcNow, "The date was incorrect."));
            await db.SaveChangesAsync();
            noteId = note.Id;
            formId = form.Id;
        }

        var session = new SessionService();
        session.SetUser(admin);
        var service = new AdminFormNoteCorrectionService(fixture.Factory, session);
        var target = await service.GetTargetAsync(noteId);
        Assert.NotNull(target);
        Assert.Null(target.CurrentCompletedOn);
        var revised = await service.CorrectAsync(noteId, target.Revision, corrected,
            "The work actually occurred the following day.", true);
        Assert.Equal(corrected, revised.EventDate);

        await using var verification = fixture.Factory.CreateDbContext();
        var ledger = await verification.FormAttestations.AsNoTracking()
            .Where(row => row.FormId == formId).OrderBy(row => row.Id).ToListAsync();
        Assert.Equal([FormAttestationKind.Attested,
            FormAttestationKind.Revoked, FormAttestationKind.Attested],
            ledger.Select(row => row.Kind).ToArray());
        var flag = await verification.FormAttestationChangeReviewFlags.AsNoTracking()
            .SingleAsync(row => row.NoteId == noteId);
        Assert.Equal(prior, flag.PreviousCompletedOn);
        Assert.Equal(corrected, flag.RevisedCompletedOn);
    }

    [Fact]
    public async Task AdminCorrectsSubmittedNoteAndAttestationInOneAuditedChange()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var due = DateTime.Today;
        var prior = due.AddDays(-2);
        var corrected = due.AddDays(-1);
        var admin = User.Create(77, "date-admin", "Date Administrator",
            "hash", "salt", UserRole.Admin, null, fixture.CaseManagerOne.AgencyId);
        int noteId;
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Users.Add(admin);
            var person = await db.People.SingleAsync(row => row.Id == fixture.PersonOneId);
            person.EffectiveDate = due.AddMonths(-6);
            var form = new Form(FormType.Q3R, due, targetEffectiveDate: due.AddDays(-270))
            {
                PersonId = person.Id
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            form.Attest(FormAttestation.Attested(prior, AttestationActorKind.CaseManager,
                fixture.CaseManagerOne.Id, DateTime.UtcNow));
            var note = Note.Create("Completed review.", prior, NoteStatus.Logged,
                15, person.Id, FormType.Q3R, NoteType.Form, formId: form.Id);
            note.AgencyId = admin.AgencyId;
            note.GoalProgress = GoalProgressLevel.None;
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            noteId = note.Id;
            formId = form.Id;
        }

        var session = new SessionService();
        session.SetUser(admin);
        var service = new AdminFormNoteCorrectionService(fixture.Factory, session);
        var target = await service.GetTargetAsync(noteId);
        Assert.NotNull(target);
        Assert.Equal(prior, target.ActivityDate);
        Assert.Equal(prior, target.CurrentCompletedOn);
        Assert.False(target.HasClaimRecord);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CorrectAsync(noteId, target.Revision, corrected,
                "The review was done the following day.", false));

        var revisedNote = await service.CorrectAsync(noteId, target.Revision,
            corrected, "The review was done the following day.", true);
        Assert.Equal(NoteStatus.Logged, revisedNote.Status);
        Assert.Equal(corrected, revisedNote.EventDate);
        Assert.Equal(target.Revision + 1, revisedNote.Revision);

        await using var verification = fixture.Factory.CreateDbContext();
        var formAfter = await verification.Forms.AsNoTracking()
            .SingleAsync(row => row.Id == formId);
        Assert.Equal(corrected, formAfter.CompletedDate);
        var ledger = await verification.FormAttestations.AsNoTracking()
            .Where(row => row.FormId == formId).OrderBy(row => row.Id).ToListAsync();
        Assert.Equal([FormAttestationKind.Attested,
            FormAttestationKind.Revoked, FormAttestationKind.Attested],
            ledger.Select(row => row.Kind).ToArray());
        Assert.Equal(noteId, ledger[^1].EvidenceNoteId);
        var flag = await verification.FormAttestationChangeReviewFlags.AsNoTracking()
            .SingleAsync(row => row.NoteId == noteId);
        Assert.True(flag.RequiresSupervisorAttention);
        Assert.False(flag.MustHoldBilling);
        Assert.True(await verification.AuditEvents.AsNoTracking().AnyAsync(row =>
            row.Action == "note.form-date-corrected" &&
            row.ResourceId == noteId.ToString()));
    }
}
