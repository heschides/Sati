using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Views;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class ReviewCompletionWorkflowTests
{
    [Theory]
    [InlineData(FormType.Q1R)]
    [InlineData(FormType.Q2R)]
    [InlineData(FormType.Q3R)]
    [InlineData(FormType.Q4R)]
    public async Task OpeningEditorDoesNotCompleteReviewAndUsesExactQuarter(FormType type)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        person.EffectiveDate = DateTime.Today.AddDays(-300);
        var form = new Form(type, DateTime.Today.AddDays(7), targetEffectiveDate: person.EffectiveDate)
        { Id = 123, PersonId = person.Id };
        person.Forms.Add(form);
        var editor = fixture.NoteEntry();
        editor.PrepareReviewCompletion(person, form, DateTime.Today.AddDays(-1));

        Assert.Null(form.CompletedDate);
        Assert.Equal(form.Id, editor.SelectedFormObligation?.FormId);
        Assert.Equal(type, editor.SelectedFormType);
        Assert.Equal(NoteStatus.Logged, editor.Status);
        Assert.False(editor.IsStatusEnabled);
        Assert.False(editor.IsUnbilled);
        Assert.Contains(type.ToString()[..2], editor.Narrative);
        Assert.Null(editor.Minutes);
        Assert.Null(editor.SelectedStartTime);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Empty(await verify.Notes.ToListAsync());
    }

    [Fact]
    public async Task LateCompletionStaysUnbilledAndEditingDateReevaluatesDeadline()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        person.EffectiveDate = DateTime.Today.AddDays(-300);
        var form = new Form(FormType.Q4R, DateTime.Today.AddDays(-2), targetEffectiveDate: person.EffectiveDate)
        { Id = 123, PersonId = person.Id };
        person.Forms.Add(form);
        var editor = fixture.NoteEntry();
        editor.PrepareReviewCompletion(person, form, DateTime.Today.AddDays(-1));
        Assert.True(editor.IsUnbilled);
        editor.IsUnbilled = false;
        Assert.True(editor.IsUnbilled);
        editor.EventDate = form.DueDate;
        Assert.False(editor.IsUnbilled);
        Assert.Null(form.CompletedDate);
    }

    [Fact]
    public async Task ClearingEditorCannotTurnCompletionIntoAnUnrelatedReminder()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        person.EffectiveDate = DateTime.Today.AddDays(-300);
        var form = new Form(FormType.Q4R, DateTime.Today.AddDays(7), targetEffectiveDate: person.EffectiveDate)
        { Id = 123, PersonId = person.Id };
        person.Forms.Add(form);
        var editor = fixture.NoteEntry(discardAnswer: true);
        editor.PrepareReviewCompletion(person, form, DateTime.Today.AddDays(-1));
        editor.Clear();
        editor.SelectedPerson = person;
        editor.SelectedNoteType = NoteType.Reminder;
        editor.Narrative = "An unrelated reminder.";
        await editor.SubmitNoteCommand.ExecuteAsync(null);
        Assert.Contains("submitted Form note", editor.SubmissionFailureMessage);
        Assert.Null(form.CompletedDate);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Empty(await verify.Notes.ToListAsync());
        Assert.Equal(string.Empty, (await verify.People.SingleAsync(p => p.Id == person.Id)).Journal ?? string.Empty);
    }

    [Fact]
    public async Task SubmittingEditorCreatesNoteAndEvidenceTogetherOnActualCompletionDate()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var completedOn = DateTime.Today.AddDays(-1);
        int formId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(p => p.Id == fixture.PersonOneId);
            person.EffectiveDate = DateTime.Today.AddDays(-300);
            var form = new Form(FormType.Q4R, DateTime.Today.AddDays(-2), targetEffectiveDate: person.EffectiveDate)
            { PersonId = person.Id };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            formId = form.Id;
        }
        Person shown;
        await using (var db = fixture.Factory.CreateDbContext())
            shown = await db.People.AsNoTracking().Include(p => p.Forms)
                .SingleAsync(p => p.Id == fixture.PersonOneId);
        var review = shown.Forms.Single(f => f.Id == formId);
        var editor = fixture.NoteEntry();
        editor.PrepareReviewCompletion(shown, review, completedOn);
        editor.Minutes = 15;
        editor.GoalProgress = GoalProgressLevel.None;
        var refreshed = false;
        editor.RefreshAfterNoteSavedAsync = () => { refreshed = true; return Task.CompletedTask; };
        await editor.SubmitNoteCommand.ExecuteAsync(null);

        await using var verify = fixture.Factory.CreateDbContext();
        var saved = Assert.Single(await verify.Notes.ToListAsync());
        var stored = await verify.Forms.Include(f => f.Attestations).SingleAsync(f => f.Id == formId);
        Assert.Equal(NoteStatus.Logged, saved.Status);
        Assert.True(saved.IsUnbilled);
        Assert.Equal(completedOn, saved.EventDate);
        Assert.Equal(completedOn, stored.CompletedDate);
        Assert.Equal(saved.Id, Assert.Single(stored.Attestations).EvidenceNoteId);
        Assert.True(refreshed);
    }

    [Fact]
    public async Task PromptUsesCompletionDateRatherThanEntryDateAndRejectsFutureDate()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        person.EffectiveDate = DateTime.Today.AddDays(-300);
        var form = new Form(FormType.Q4R, DateTime.Today.AddDays(-2), targetEffectiveDate: person.EffectiveDate)
        { Id = 123, PersonId = person.Id };
        WpfUiHarness.Run(() =>
        {
            var window = new ReviewCompletionPromptWindow(person, form);
            var date = (DatePicker)window.FindName("CompletionDate");
            var button = (Button)window.FindName("GenerateButton");
            Assert.Null(date.SelectedDate);
            Assert.False(button.IsEnabled);
            date.SelectedDate = form.DueDate;
            Assert.True(button.IsEnabled);
            Assert.Contains("a billable case note", ((TextBlock)window.FindName("Question")).Text);
            date.SelectedDate = DateTime.Today.AddDays(-1);
            Assert.Contains("a non-billable case note", ((TextBlock)window.FindName("Question")).Text);
            date.SelectedDate = DateTime.Today.AddDays(1);
            Assert.False(button.IsEnabled);
            window.Close();
        });
    }

    [Fact]
    public async Task AnnualFormsActionRequestsExactReviewAndCancelDoesNotWrite()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var person = await fixture.PersonOneAsync();
        person.EffectiveDate = DateTime.Today.AddDays(-300);
        var selected = new Form(FormType.Q4R, DateTime.Today.AddDays(7), targetEffectiveDate: person.EffectiveDate)
        { Id = 123, PersonId = person.Id };
        var other = new Form(FormType.Q4R, selected.DueDate.AddYears(-1), targetEffectiveDate: person.EffectiveDate.Value.AddYears(-1))
        { Id = 124, PersonId = person.Id };
        person.Forms.AddRange([selected, other]);
        var clients = fixture.ClientsPage();
        clients.SelectedPerson = person;
        Form? requested = null;
        clients.ReviewCompletionRequestedAsync = (p, form) =>
        {
            Assert.Same(person, p);
            requested = form;
            return Task.CompletedTask; // User declines the generation prompt.
        };
        var item = new PlanYearItem("form:124", "Q4 90-day review", PlanYearGroup.CheckIns,
            PlanYearItemState.Overdue, other.DueDate, null, null, "Overdue", "", "",
            "Record completion", PlanYearWorkspace.ClientOverview);
        await clients.OpenPlanYearItemCommand.ExecuteAsync(item);
        Assert.Same(other, requested);
        Assert.Null(selected.CompletedDate);
        Assert.Null(other.CompletedDate);
        Assert.False(clients.Attestation.IsVisible);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Empty(await verify.Notes.ToListAsync());
    }
}
