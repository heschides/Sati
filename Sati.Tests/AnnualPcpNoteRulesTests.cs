using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class AnnualPcpNoteRulesTests
{
    [Fact]
    public void AnnualPcpProgressionDistinguishesWindowOpenCompleteAndLate()
    {
        var due = new DateTime(2026, 10, 1);
        var available = due.AddDays(-90);

        var early = AnnualPcpNoteRules.Evaluate(true, "PCP", 42, "Pending",
            available.AddDays(-1), available, due, null, null);
        var opening = AnnualPcpNoteRules.Evaluate(true, "PCP", 42, "Pending",
            available, available, due, null, null);
        var completing = AnnualPcpNoteRules.Evaluate(true, "PCP", 42, "Logged",
            due, available, due, available, null);
        var late = AnnualPcpNoteRules.Evaluate(true, "PCP", 42, "Logged",
            due.AddDays(1), available, due, available, null);

        Assert.True(early.IsBeforeAvailableWindow);
        Assert.Equal(AnnualPcpProgressAction.Open, opening.RequiredAction);
        Assert.Equal(AnnualPcpProgressAction.Complete, completing.RequiredAction);
        Assert.True(late.MustBeUnbilled);
    }

    [Fact]
    public async Task PendingAnnualNotesOpenThenCompleteTheExactPlan()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var serviceDate = DateTime.Today;
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(row => row.Id == fixture.PersonOneId);
            person.EffectiveDate = serviceDate.AddYears(-1).AddDays(30);
            var form = new Form(FormType.PCP, serviceDate.AddDays(30),
                targetEffectiveDate: serviceDate.AddDays(30))
            {
                PersonId = person.Id
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
        }

        var note = Note.Create("Opened the annual PCP.", serviceDate,
            NoteStatus.Pending, 15, fixture.PersonOneId, FormType.PCP,
            NoteType.Form, formId);
        note.GoalProgress = GoalProgressLevel.None;
        note.IsAnnualPlan = true;
        note.AnnualPcpAction = AnnualPcpProgressAction.Open;
        await fixture.NotesFromAnotherSession().AddNoteAsync(note);

        await using (var db = fixture.Factory.CreateDbContext())
        {
            var opened = await db.Forms.SingleAsync(row => row.Id == formId);
            Assert.Equal(serviceDate, opened.OpenedDate);
            Assert.Null(opened.CompletedDate);
        }

        note.Narrative = "Completed the annual PCP.";
        note.AnnualPcpAction = AnnualPcpProgressAction.Complete;
        await fixture.NotesFromAnotherSession().UpdateNoteAsync(note);

        await using var verification = fixture.Factory.CreateDbContext();
        var completed = await verification.Forms.Include(row => row.Attestations)
            .SingleAsync(row => row.Id == formId);
        Assert.Equal(serviceDate, completed.CompletedDate);
        Assert.Equal(note.Id, Assert.Single(completed.Attestations).EvidenceNoteId);
    }

    [Fact]
    public async Task LateAnnualPcpIsForcedUnbilled()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var serviceDate = DateTime.Today;
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(row => row.Id == fixture.PersonOneId);
            person.EffectiveDate = serviceDate.AddYears(-1).AddDays(-1);
            var form = new Form(FormType.PCP, serviceDate.AddDays(-1),
                targetEffectiveDate: serviceDate.AddDays(-1))
            {
                PersonId = person.Id
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
        }

        var note = Note.Create("Opened the late annual PCP.", serviceDate,
            NoteStatus.Pending, 15, fixture.PersonOneId, FormType.PCP,
            NoteType.Form, formId);
        note.GoalProgress = GoalProgressLevel.None;
        note.IsAnnualPlan = true;
        note.AnnualPcpAction = AnnualPcpProgressAction.Open;

        await fixture.NotesFromAnotherSession().AddNoteAsync(note);

        await using var verification = fixture.Factory.CreateDbContext();
        Assert.True((await verification.Notes.SingleAsync(row => row.Id == note.Id)).IsUnbilled);
    }
}
