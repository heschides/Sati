using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Marking a note Logged from the notes log when the note service refuses it. In 1.3.14 a
/// pending note without goal progress threw out of the command into the crash dialog
/// (production reference D47EFC73EBA0) and the grid kept showing Logged.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class NotesLogStatusRefusalTests
{
    // Before this date the agency gates billing on nothing; from it, on the PCP.
    private static readonly DateTime EnforcementDate = new(2026, 8, 15);
    private static readonly DateTime Ungated = new(2026, 8, 10);
    private static readonly DateTime Gated = new(2026, 8, 20);

    [Fact]
    public async Task MarkingANoteWithoutGoalProgressLoggedIsRefusedInPlace()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = await OpenLogWithPendingNoteAsync(fixture, Ungated);
        var note = log.NotesView.Cast<Note>().Single();

        log.SelectedNote = note;
        await log.MarkNoteLoggedCommand.ExecuteAsync(null);

        Assert.Equal(NoteStatus.Pending, note.Status);
        Assert.True(log.HasLoadError);
        Assert.Equal(
            "This note's status was not changed. Goal progress is required before a note can be submitted for review.",
            log.LoadErrorMessage);
        Assert.Equal(NoteStatus.Pending, await StoredStatusAsync(fixture));
    }

    [Fact]
    public async Task SendingARefusedNoteToASupervisorRestoresItsStatusAndJustification()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = await OpenLogWithPendingNoteAsync(fixture, Gated);
        var note = log.NotesView.Cast<Note>().Single();
        note.Person.Forms.Add(new Form(
            FormType.PCP,
            new DateTime(2026, 8, 1),
            targetEffectiveDate: new DateTime(2026, 8, 1)));

        log.SelectedNote = note;
        await log.MarkNoteLoggedCommand.ExecuteAsync(null);
        Assert.True(log.IsComplianceDialogVisible);
        log.PendingJustification = "Plan meeting was rescheduled by the guardian.";
        await log.SendToSupervisorCommand.ExecuteAsync(null);

        Assert.Equal(NoteStatus.Pending, note.Status);
        Assert.Null(note.CaseManagerJustification);
        Assert.False(log.IsComplianceDialogVisible);
        Assert.StartsWith("This note's status was not changed. Goal progress is required", log.LoadErrorMessage);
        Assert.Equal(NoteStatus.Pending, await StoredStatusAsync(fixture));
    }

    [Fact]
    public async Task AStatusChangeThatSavesClearsAnEarlierRefusal()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = await OpenLogWithPendingNoteAsync(fixture, Ungated);
        var note = log.NotesView.Cast<Note>().Single();
        log.SelectedNote = note;
        await log.MarkNoteLoggedCommand.ExecuteAsync(null);
        Assert.True(log.HasLoadError);

        note.GoalProgress = GoalProgressLevel.Moderate;
        await log.MarkNoteLoggedCommand.ExecuteAsync(null);

        Assert.Equal(NoteStatus.Logged, note.Status);
        Assert.False(log.HasLoadError);
        Assert.Equal(NoteStatus.Logged, await StoredStatusAsync(fixture));
    }

    private static async Task<ViewModels.NotesWindowViewModel> OpenLogWithPendingNoteAsync(
        NoteEntryFixture fixture, DateTime serviceDate)
    {
        await fixture.NotesFromAnotherSession().AddNoteAsync(Note.Create(
            "Pending without goal progress.",
            serviceDate,
            NoteStatus.Pending,
            15,
            fixture.PersonOneId,
            noteType: NoteType.Contact));

        var log = fixture.NotesWindow(settings: new DatedPolicySettings());
        await log.NoteEntry.InitializeAsync();
        await log.ReloadAsync();
        return log;
    }

    private static async Task<NoteStatus?> StoredStatusAsync(NoteEntryFixture fixture)
    {
        await using var db = fixture.Factory.CreateDbContext();
        return (await db.Notes.AsNoTracking().SingleAsync()).Status;
    }

    private sealed class DatedPolicySettings : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(new Settings
        {
            BillingComplianceRequirements = BillingComplianceRequirements.Pcp
        });

        public Task<BillingComplianceRequirements>
            ResolveBillingComplianceRequirementsAsync(DateTime serviceDate) =>
            Task.FromResult(serviceDate.Date < EnforcementDate
                ? BillingComplianceRequirements.None
                : BillingComplianceRequirements.Pcp);

        public Task SaveAsync(Settings settings) => Task.CompletedTask;
    }
}
