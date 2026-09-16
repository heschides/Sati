using Microsoft.EntityFrameworkCore;
using Sati.Models;
using Sati.Views;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class LateReviewNoteSubmissionTests
{
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
}
