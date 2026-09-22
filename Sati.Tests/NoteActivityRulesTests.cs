using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class NoteActivityRulesTests
{
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
