using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class ScheduledNoteMoveServiceTests
{
    [Fact]
    public async Task ScheduledMovesFreezeOldUnitsDeduplicateReturnsAndRejectStaleWrites()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var session = SessionFor(fixture.CaseManagerOne);
        var notes = new NoteService(fixture.Factory, session);
        var history = new ScheduledNoteMoveService(fixture.Factory, session);
        var dayA = new DateTime(2197, 4, 2);
        var dayB = dayA.AddDays(1);
        var dayC = dayB.AddDays(1);
        var saved = await notes.AddNoteAsync(Note.Create(
            "Scheduled work", dayA, NoteStatus.Scheduled, 20,
            fixture.PersonOneId, noteType: NoteType.Contact));
        var stale = await DetachedAsync(fixture, saved.Id);

        var edit = await DetachedAsync(fixture, saved.Id);
        edit.EventDate = dayB;
        edit.Minutes = 45;
        await notes.UpdateNoteAsync(edit);
        var first = Assert.Single(await history.GetByYearAsync(2197));
        Assert.Equal(dayA, first.FromDate);
        Assert.Equal(dayB, first.ToDate);
        Assert.Equal(20, first.ScheduledMinutes);
        Assert.Equal(2, first.ScheduledUnits);

        stale.EventDate = dayC;
        await Assert.ThrowsAsync<NoteConcurrencyException>(() => notes.UpdateNoteAsync(stale));
        Assert.Single(await history.GetByYearAsync(2197));

        edit = await DetachedAsync(fixture, saved.Id);
        edit.EventDate = dayA;
        edit.Minutes = 40;
        await notes.UpdateNoteAsync(edit);
        var returned = await history.GetByYearAsync(2197);
        Assert.DoesNotContain(returned, row => row.FromDate == dayA);
        Assert.Single(returned, row => row.FromDate == dayB);

        edit = await DetachedAsync(fixture, saved.Id);
        edit.EventDate = dayC;
        await notes.UpdateNoteAsync(edit);
        var final = await history.GetByYearAsync(2197);
        Assert.Equal(2, final.Count);
        var latestA = Assert.Single(final, row => row.FromDate == dayA);
        Assert.Equal(40, latestA.ScheduledMinutes);
        Assert.Equal(3, latestA.ScheduledUnits);
        Assert.Equal(dayC, latestA.ToDate);
        Assert.Single(final, row => row.FromDate == dayB);

        var foreignHistory = new ScheduledNoteMoveService(
            fixture.Factory, SessionFor(fixture.CaseManagerTwo));
        Assert.Empty(await foreignHistory.GetByYearAsync(2197));

        await using var db = fixture.Factory.CreateDbContext();
        Assert.Equal(3, await db.ScheduledNoteMoves.CountAsync(row => row.NoteId == saved.Id));
    }

    [Fact]
    public void ScheduleMoveRuleRequiresAnOldScheduledServiceDate()
    {
        var first = new DateTime(2197, 4, 2);
        var second = first.AddDays(1);
        Assert.True(NoteScheduleMoveRules.ShouldRecord("Scheduled", first, second, "Contact"));
        Assert.True(NoteScheduleMoveRules.ShouldRecord("Scheduled", first, second, "Form"));
        Assert.False(NoteScheduleMoveRules.ShouldRecord("Pending", first, second, "Contact"));
        Assert.False(NoteScheduleMoveRules.ShouldRecord("Scheduled", first, first, "Contact"));
        Assert.False(NoteScheduleMoveRules.ShouldRecord("Scheduled", first, second, "Reminder"));
        Assert.False(NoteScheduleMoveRules.ShouldRecord("Scheduled", null, second, "Contact"));
    }

    [Fact]
    public async Task ConfirmedManualFormConversionRecordsTheOriginalPlan()
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
                NoteStatus.Scheduled, 30, person.Id,
                FormType.SafetyPlan, NoteType.Form, form.Id);
            note.Activities = NoteActivity.Form;
            note.AgencyId = fixture.CaseManagerOne.AgencyId;
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            noteId = note.Id;
        }

        var session = SessionFor(fixture.CaseManagerOne);
        var forms = new FormService(fixture.Factory, session);
        var prompt = await Assert.ThrowsAsync<ScheduledFormNoteConversionRequiredException>(
            () => forms.AttestAsync(form, completedOn));
        var history = new ScheduledNoteMoveService(fixture.Factory, session);
        Assert.Empty(await history.GetByYearAsync(plannedOn.Year));

        await forms.AttestAsync(form, completedOn, noteId,
            confirmScheduledNoteConversion: true, prompt.ConfirmationToken);
        var moved = Assert.Single(await history.GetByYearAsync(plannedOn.Year));
        Assert.Equal(noteId, moved.NoteId);
        Assert.Equal(plannedOn, moved.FromDate);
        Assert.Equal(completedOn, moved.ToDate);
        Assert.Equal(30, moved.ScheduledMinutes);
        Assert.Equal(2, moved.ScheduledUnits);

        await using var verification = fixture.Factory.CreateDbContext();
        var updatedNote = await verification.Notes.SingleAsync(row => row.Id == noteId);
        Assert.Equal(NoteStatus.Pending, updatedNote.Status);
        Assert.Equal(2, updatedNote.Revision);
    }

    [Fact]
    public async Task CalendarYearAndMoveHistoryExcludeGhostConsumers()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var date = new DateTime(2197, 6, 2);
        int noteId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var ghost = await db.People.SingleAsync(row => row.Id == fixture.PersonTwoId);
            ghost.Status = PersonStatus.Ghost;
            var note = Note.Create("Hidden test note", date.AddDays(1),
                NoteStatus.Scheduled, 15, ghost.Id, noteType: NoteType.Contact);
            note.AgencyId = fixture.CaseManagerOne.AgencyId;
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            noteId = note.Id;
            db.ScheduledNoteMoves.Add(new ScheduledNoteMove
            {
                NoteId = noteId,
                PersonId = ghost.Id,
                AgencyId = fixture.CaseManagerOne.AgencyId,
                UserId = fixture.CaseManagerOne.Id,
                FromDate = date,
                ToDate = date.AddDays(1),
                ScheduledMinutes = 15,
                ScheduledUnits = 1,
                NoteRevision = 2,
                MovedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var session = SessionFor(fixture.CaseManagerOne);
        Assert.DoesNotContain(await new NoteService(fixture.Factory, session)
            .GetByYearAsync(fixture.CaseManagerOne.Id, 2197), row => row.Id == noteId);
        Assert.DoesNotContain(await new ScheduledNoteMoveService(fixture.Factory, session)
            .GetByYearAsync(2197), row => row.NoteId == noteId);
    }

    private static SessionService SessionFor(User user)
    {
        var session = new SessionService();
        session.SetUser(user);
        return session;
    }

    private static async Task<Note> DetachedAsync(NoteEntryFixture fixture, int noteId)
    {
        await using var db = fixture.Factory.CreateDbContext();
        return await db.Notes.AsNoTracking().SingleAsync(note => note.Id == noteId);
    }
}
