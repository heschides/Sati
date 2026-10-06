using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class FormProgressTests
{
    [Theory]
    [InlineData(FormType.PCP)]
    [InlineData(FormType.ComprehensiveAssessment)]
    public async Task LoggedLeaveUnchangedDoesNotAutoAttest(FormType type)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        int id;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var form = new Form(type, today, targetEffectiveDate: today)
            { PersonId = fixture.PersonOneId };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            id = form.Id;
        }
        var note = Note.Create("Synthetic unchanged work.", today, NoteStatus.Logged,
            15, fixture.PersonOneId, type, NoteType.Form, id);
        note.GoalProgress = GoalProgressLevel.None;
        note.IsUnbilled = true;
        note.FormProgress = new(AnnualPcpProgressAction.None, null, null, null, null, today, today);
        await fixture.NotesFromAnotherSession().AddNoteAsync(note);
        await using var verify = fixture.Factory.CreateDbContext();
        var unchanged = await verify.Forms.SingleAsync(f => f.Id == id);
        Assert.Null(unchanged.OpenedDate);
        Assert.Null(unchanged.CompletedDate);
        Assert.False(await verify.FormAttestations.AnyAsync(a => a.FormId == id));
    }

    [Fact]
    public async Task ProfileRefreshPreservesDemographicDraftAndUpdatesAnnualEvidence()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var person = await fixture.PersonOneAsync();
        person.EffectiveDate = today.AddYears(-1);
        person.FirstName = "Unsaved draft name";
        person.Forms = [new Form(FormType.PCP, today, targetEffectiveDate: today)
            { Id = 42, PersonId = person.Id }];
        var profile = fixture.ClientsPage();
        profile.ReplacePeople([person]);
        profile.SelectedPerson = person;
        profile.IsClientEditorOpen = true;
        var refreshed = await fixture.PersonOneAsync();
        refreshed.Forms = [new Form(FormType.PCP, today, completedOn: today, targetEffectiveDate: today)
            { Id = 42, PersonId = person.Id, OpenedDate = today.AddDays(-10) }];
        profile.ReplacePeople([refreshed]);
        Assert.Same(person, profile.SelectedPerson);
        Assert.Equal("Unsaved draft name", profile.SelectedPerson!.FirstName);
        Assert.Equal(today, Assert.Single(profile.SelectedPerson.Forms).CompletedDate);
        var row = profile.AnnualFormRow(FormType.PCP);
        Assert.True(row.Current?.IsComplete == true || row.Renewal?.IsComplete == true);
    }

    [Theory]
    [InlineData(FormType.PCP)]
    [InlineData(FormType.ComprehensiveAssessment)]
    public async Task CompleteUnopenedDocumentThenSaveWithoutAdvancing(FormType type)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var due = today.AddDays(10);
        var target = type == FormType.PCP ? due : due.AddDays(90);
        int id;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(p => p.Id == fixture.PersonOneId);
            person.EffectiveDate = target.AddYears(-1);
            var form = new Form(type, due, targetEffectiveDate: target) { PersonId = person.Id };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            id = form.Id;
        }
        var note = Note.Create("Synthetic document work.", today.AddDays(-1), NoteStatus.Pending,
            15, fixture.PersonOneId, type, NoteType.Form, id);
        note.GoalProgress = GoalProgressLevel.None;
        note.FormProgress = new(AnnualPcpProgressAction.Complete, today.AddDays(-5), today.AddDays(-2),
            null, null, due, target);
        await fixture.NotesFromAnotherSession().AddNoteAsync(note);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var form = await db.Forms.Include(f => f.Attestations).SingleAsync(f => f.Id == id);
            Assert.Equal(today.AddDays(-5), form.OpenedDate);
            Assert.Equal(today.AddDays(-2), form.CompletedDate);
            Assert.Equal(note.Id, Assert.Single(form.Attestations).EvidenceNoteId);
        }
        note.FormProgress = new(AnnualPcpProgressAction.None, null, null,
            today.AddDays(-5), today.AddDays(-2), due, target);
        await fixture.NotesFromAnotherSession().UpdateNoteAsync(note);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Single(await verify.FormAttestations.Where(a => a.FormId == id).ToListAsync());
        Assert.False((await verify.Notes.SingleAsync(n => n.Id == note.Id)).IsUnbilled);
    }

    [Theory]
    [InlineData(FormType.PCP)]
    [InlineData(FormType.ComprehensiveAssessment)]
    public async Task StaleDisplayedStateRefusesEntireNoteSave(FormType type)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var due = today.AddDays(5);
        var target = type == FormType.PCP ? due : due.AddDays(90);
        int id;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var form = new Form(type, due, targetEffectiveDate: target)
            { PersonId = fixture.PersonOneId, OpenedDate = today.AddDays(-2) };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            id = form.Id;
        }
        var note = Note.Create("Synthetic stale document work.", today, NoteStatus.Pending,
            15, fixture.PersonOneId, type, NoteType.Form, id);
        note.GoalProgress = GoalProgressLevel.None;
        // Modal displayed unopened; another writer has since opened it.
        note.FormProgress = new(AnnualPcpProgressAction.None, null, null, null, null, due, target);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.NotesFromAnotherSession().AddNoteAsync(note));
        Assert.Contains("changed since", exception.Message);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.False(await verify.Notes.AnyAsync(n => n.FormId == id));
        Assert.Equal(today.AddDays(-2), (await verify.Forms.SingleAsync(f => f.Id == id)).OpenedDate);
    }

    [Fact]
    public void CompletionRequiresOpeningAndOrderedDates()
    {
        var due = new DateTime(2026, 10, 6);
        var request = new FormProgressRequest(AnnualPcpProgressAction.Complete, null,
            due.AddDays(-5), null, null, due, due);
        string? Validate(FormProgressRequest r) => FormProgressRules.Validate(r, "PCP", "Pending",
            due.AddDays(-90), due, due, due, null, null);
        Assert.Contains("opening date", Validate(request));
        Assert.Contains("cannot follow", Validate(request with { OpenedOn = due.AddDays(-4) }));
        Assert.Contains("future", Validate(request with { OpenedOn = due, CompletedOn = due.AddDays(1) }));
        Assert.Null(Validate(request with { OpenedOn = due.AddDays(-10) }));
    }
}
