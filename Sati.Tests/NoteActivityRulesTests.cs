using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class NoteActivityRulesTests
{
    [Fact]
    public async Task TelehealthActivityPersistsThroughTheLocalNoteService()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var note = Sati.Models.Note.Create("Synthetic telehealth activity.", DateTime.Today,
            NoteStatus.Pending, 15, fixture.PersonOneId, null, NoteType.Phone);
        note.Activities = NoteActivity.Telehealth;
        note.GoalProgress = GoalProgressLevel.None;
        await fixture.NotesFromAnotherSession().AddNoteAsync(note);
        var saved = Assert.Single(await fixture.NotesFromAnotherSession().GetAllByPersonAsync(fixture.PersonOneId));
        Assert.Equal(NoteActivity.Telehealth, saved.Activities);
        Assert.Equal("Telehealth", saved.ActivityLabel);
    }

    [Fact]
    public void TelehealthIsDistinctRemoteContactAndCanCombineWithFormWork()
    {
        var activity = NoteActivity.Telehealth | NoteActivity.Form;
        Assert.Null(NoteActivityRules.Validate((int)activity, "Form"));
        Assert.Equal("Telehealth + Form", NoteActivityRules.DisplayLabel((int)activity, "Form"));
        Assert.Equal("Phone", NoteActivityRules.PrimaryLegacyType(NoteActivity.Telehealth));
        Assert.NotNull(MonthlyContactRules.ToFact("Form", "Logged", DateTime.Today, 42, (int)activity));
        Assert.Null(MonthlyContactRules.ToFact("Phone", "Scheduled", DateTime.Today, 42, (int)NoteActivity.Telehealth));
        Assert.False(NoteActivityRules.Has((int)NoteActivity.Telehealth, "Phone", NoteActivity.Visit));
        Assert.NotNull(NoteActivityRules.Validate((int)NoteActivity.Telehealth, "Reminder"));
    }

    [Fact]
    public void LegacyOrdinalsRemainSingleActivities()
    {
        Assert.Equal(NoteActivity.Form, NoteActivityRules.Effective(null, "Form"));
        Assert.Equal(NoteActivity.Phone, NoteActivityRules.Effective(null, "Contact"));
        Assert.Equal(NoteActivity.None, NoteActivityRules.Effective(null, "Reminder"));
        Assert.Null(NoteActivityRules.Validate((int)NoteActivity.Phone, "Contact"));
    }

    [Fact]
    public void FormAndPhoneAreBothRetainedAndDisplayed()
    {
        var activities = (int)(NoteActivity.Form | NoteActivity.Phone);

        Assert.Null(NoteActivityRules.Validate(activities, "Form"));
        Assert.True(NoteActivityRules.Has(activities, "Form", NoteActivity.Phone));
        Assert.True(NoteActivityRules.Has(activities, "Form", NoteActivity.Form));
        Assert.Equal("Phone + Form", NoteActivityRules.DisplayLabel(activities, "Form"));
    }

    [Fact]
    public void ReminderCannotCarryWorkAndUnknownFlagsAreRejected()
    {
        Assert.NotNull(NoteActivityRules.Validate((int)NoteActivity.Phone, "Reminder"));
        Assert.NotNull(NoteActivityRules.Validate(1 << 10, "Other"));
        Assert.NotNull(NoteActivityRules.Validate((int)NoteActivity.Form, "Phone"));
    }
}
