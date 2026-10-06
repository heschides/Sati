using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

// The other suggested-follow-up tests drive the panel with a stub event service,
// so they prove the panel reacts but never that the real generator produces
// anything. It did not: for most of every cycle GenerateEvents is silent, which
// is why the row never appeared in the running app.
public sealed class SuggestedFollowUpRealServiceTests
{
    [Fact]
    public async Task CompletingQ2DoesNotSuggestItsScheduledNoteAsAnotherDeadline()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var person = await fixture.PersonOneAsync();
        person.EffectiveDate = today.AddDays(-200);
        var q2 = new Form(FormType.Q2R, today.AddDays(-20), targetEffectiveDate: person.EffectiveDate)
        { Id = 501, PersonId = person.Id };
        var q3 = new Form(FormType.Q3R, today.AddDays(70), targetEffectiveDate: person.EffectiveDate)
        { Id = 502, PersonId = person.Id };
        person.Forms.AddRange([q2, q3]);
        var scheduled = Note.Rehydrate(601);
        scheduled.PersonId = person.Id;
        scheduled.Status = NoteStatus.Scheduled;
        scheduled.EventDate = today;
        scheduled.NoteType = NoteType.Form;
        scheduled.FormType = FormType.Q2R;
        scheduled.FormId = q2.Id;
        person.Notes.Add(scheduled);
        var panel = fixture.NoteEntry();
        await panel.InitializeAsync();
        panel.SelectedPerson = person;
        Assert.Contains($"Q2 Review — {person.FullName}", panel.SuggestedFollowUpText);
        panel.PrepareReviewCompletion(person, q2, q2.DueDate.AddDays(2));
        Assert.Contains("Q3 Review", panel.SuggestedFollowUpText);
        Assert.DoesNotContain("Q2 Review", panel.SuggestedFollowUpText);
        Assert.Null(q2.CompletedDate);
        Assert.Equal(NoteStatus.Scheduled, scheduled.Status);
        Assert.Equal(today, scheduled.EventDate);
    }

    [Fact]
    public async Task ScheduledActivityIsLabelledScheduledInSuggestionAndAcceptedNarrative()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        person.EffectiveDate = DateTime.Today.AddDays(180);
        person.Notes.Add(Note.Create("Call.", DateTime.Today, NoteStatus.Scheduled, 15,
            person.Id, null, NoteType.Phone));
        var panel = fixture.NoteEntry();
        await panel.InitializeAsync();
        panel.SelectedPerson = person;
        Assert.Contains(", scheduled ", panel.SuggestedFollowUpText);
        Assert.DoesNotContain(", due ", panel.SuggestedFollowUpText);
        panel.AcceptSuggestedFollowUpCommand.Execute(null);
        Assert.Contains(" scheduled ", panel.Narrative);
        Assert.DoesNotContain(" due ", panel.Narrative);
    }

    [Fact]
    public async Task StartingAgendaReviewExcludesItsStoredScheduledCopyBeforeSubmission()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var stored = await db.People.SingleAsync(p => p.Id == fixture.PersonOneId);
            stored.EffectiveDate = today.AddDays(-200);
            var q2 = new Form(FormType.Q2R, today.AddDays(-20), targetEffectiveDate: stored.EffectiveDate)
            { PersonId = stored.Id };
            stored.Forms.Add(q2);
            stored.Forms.Add(new Form(FormType.Q3R, today.AddDays(70), targetEffectiveDate: stored.EffectiveDate)
            { PersonId = stored.Id });
            await db.SaveChangesAsync();
            formId = q2.Id;
        }
        var notes = fixture.NotesFromAnotherSession();
        var scheduled = Note.Create("Complete Q2 review.", today, NoteStatus.Scheduled, 15,
            fixture.PersonOneId, FormType.Q2R, NoteType.Form, formId);
        await notes.AddNoteAsync(scheduled);
        Person person;
        await using (var db = fixture.Factory.CreateDbContext())
            person = await db.People.AsNoTracking().Include(p => p.Forms).Include(p => p.Notes)
                .SingleAsync(p => p.Id == fixture.PersonOneId);
        var panel = fixture.NoteEntry(notes: notes);
        await panel.InitializeAsync();
        panel.SetPeople([person]);
        Assert.True(await panel.PrepareScheduledWorkAsync(scheduled));
        panel.EventDate = today.AddDays(-18); // Completed two days after the deadline.
        Assert.Equal(NoteStatus.Pending, panel.Status);
        Assert.Contains("Q3 Review", panel.SuggestedFollowUpText);
        Assert.DoesNotContain("Q2 Review", panel.SuggestedFollowUpText);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Null((await verify.Forms.SingleAsync(f => f.Id == formId)).CompletedDate);
        var unchanged = await verify.Notes.SingleAsync(n => n.Id == scheduled.Id);
        Assert.Equal(NoteStatus.Scheduled, unchanged.Status);
        Assert.Equal(today, unchanged.EventDate);
    }

    [Fact]
    public async Task NextFormSuggestionFillsTheWindowGapThatLeftTheRowBlank()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        // Put admission far enough ahead that none of its pre-service documents
        // has entered its configured preparation window yet.
        var person = await EffectiveOnAsync(fixture, today.AddDays(180));
        var settings = new Settings();
        var service = new UpcomingEventService();

        // The gap this fixes: every form is months out, so nothing is inside its
        // open window and the dashboard generator reports nothing at all.
        Assert.Empty(service.GenerateEvents([person], settings, today));

        var next = service.NextFormSuggestion(person, settings, today);

        Assert.NotNull(next);
        var earliestOutstanding = person.Forms
            .Where(form => !form.IsSatisfiedAsOf(today))
            .Min(form => form.DueDate.Date);
        Assert.Equal(earliestOutstanding, next!.Date);
        Assert.Equal(UpcomingEventKind.UpcomingForm, next.Kind);
        Assert.NotNull(next.FormType);
        Assert.NotNull(next.OpenDate);
        Assert.True(next.OpenDate <= next.Date);
        Assert.Null(next.OpenedDate);
    }

    [Fact]
    public async Task NotePanelShowsTheNextFormForAnOrdinaryClient()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var person = await EffectiveOnAsync(fixture, today);

        var panel = fixture.NoteEntry();
        await panel.InitializeAsync();
        panel.SelectedPerson = person;

        // Before the fallback existed this was false for every ordinary client.
        Assert.True(panel.IsSuggestedFollowUpVisible);
        Assert.Contains("PCP", panel.SuggestedFollowUpText);
        Assert.True(panel.AcceptSuggestedFollowUpCommand.CanExecute(null));
        // Admission today does not manufacture completion of the CA due 90 days earlier.
        Assert.StartsWith("OVERDUE:", panel.ClientWorkStatusText);
        Assert.Contains("Comprehensive Assessment", panel.ClientWorkStatusText);

        panel.AcceptSuggestedFollowUpCommand.Execute(null);

        Assert.StartsWith("Follow-up: ", panel.Narrative);
        Assert.False(panel.AcceptSuggestedFollowUpCommand.CanExecute(null));
    }

    [Fact]
    public async Task ASatisfiedFormIsNeverSuggested()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var person = await EffectiveOnAsync(fixture, today);
        var settings = new Settings();
        var service = new UpcomingEventService();

        var first = service.NextFormSuggestion(person, settings, today);
        Assert.NotNull(first);

        foreach (var form in person.Forms.Where(f => f.DueDate.Date == first!.Date))
        {
            form.Attest(FormAttestation.Attested(
                today,
                AttestationActorKind.System,
                actorUserId: null,
                recordedAtUtc: DateTime.UtcNow,
                reason: "test setup"));
        }

        var second = service.NextFormSuggestion(person, settings, today);

        Assert.NotNull(second);
        Assert.True(second!.Date > first!.Date);
    }

    [Fact]
    public async Task ScheduledFormSuggestionNamesItsRecordedFormType()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var person = await EffectiveOnAsync(fixture, today);
        person.Notes.Add(Note.Create(
            "Prepare the safety plan.", today.AddDays(2), NoteStatus.Scheduled, 30,
            person.Id, FormType.SafetyPlan, NoteType.Form));

        var item = Assert.Single(new UpcomingEventService()
            .GenerateEvents([person], new Settings(), today),
            candidate => candidate.Kind == UpcomingEventKind.ScheduledForm);

        Assert.Equal($"Safety Plan — {person.FullName}", item.Title);
        Assert.Equal(FormType.SafetyPlan, item.FormType);
    }

    [Fact]
    public async Task LegacyScheduledFormWithoutATypeSaysTheTypeWasNotRecorded()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var today = DateTime.Today;
        var person = await EffectiveOnAsync(fixture, today);
        person.Notes.Add(Note.Create(
            "Legacy scheduled form.", today.AddDays(2), NoteStatus.Scheduled, 30,
            person.Id, formType: null, NoteType.Form));

        var item = Assert.Single(new UpcomingEventService()
            .GenerateEvents([person], new Settings(), today),
            candidate => candidate.Kind == UpcomingEventKind.ScheduledForm);

        Assert.Equal($"Form (type not recorded) — {person.FullName}", item.Title);
        Assert.Null(item.FormType);
    }

    // Rebuild the person's synthetic annual obligations around the supplied
    // effective date so each test controls whether a preparation window is open.
    private static async Task<Person> EffectiveOnAsync(
        NoteEntryFixture fixture,
        DateTime effectiveOn)
    {
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var stored = await db.People.Include(p => p.Forms)
                .SingleAsync(p => p.Id == fixture.PersonOneId);
            stored.EffectiveDate = effectiveOn;
            stored.Forms = Person.CreatePerson(
                stored.UserId, "Journal", "Person", string.Empty,
                new DateTime(1990, 1, 1), effectiveOn, WaiverType.Section21, new Settings()).Forms;
            await db.SaveChangesAsync();
        }

        await using var read = fixture.Factory.CreateDbContext();
        return await read.People.AsNoTracking()
            .Include(p => p.Forms)
            .Include(p => p.Notes)
            .SingleAsync(p => p.Id == fixture.PersonOneId);
    }
}
