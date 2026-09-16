using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class ExactDateNoteComplianceTests
{
    private static readonly DateTime EnforcementDate = new(2026, 8, 15);
    private static readonly DateTime BeforeEnforcement = new(2026, 8, 10);
    private static readonly DateTime AfterEnforcement = new(2026, 8, 20);

    [Fact]
    public async Task NoteEntryUsesTheServiceDatePolicyAndHoldsOnlyTheBlockedNote()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var settings = new EffectiveDateSettingsService();
        var panel = fixture.NoteEntry(settings: settings);
        await panel.InitializeAsync();
        var person = await fixture.PersonOneAsync();
        person.Forms.Add(new Form(
            FormType.PCP,
            new DateTime(2026, 8, 1),
            targetEffectiveDate: new DateTime(2026, 8, 1)));
        panel.SetPeople([person]);

        FillNote(panel, person, BeforeEnforcement, "Before policy enforcement.");
        await panel.SubmitNoteCommand.ExecuteAsync(null);

        Assert.False(panel.IsComplianceDialogVisible);
        await using (var firstRead = fixture.Factory.CreateDbContext())
        {
            var saved = Assert.Single(await firstRead.Notes.AsNoTracking().ToListAsync());
            Assert.Equal(NoteStatus.Logged, saved.Status);
        }

        FillNote(panel, person, AfterEnforcement, "After policy enforcement.");
        await panel.SubmitNoteCommand.ExecuteAsync(null);

        Assert.True(panel.IsComplianceDialogVisible);
        Assert.Contains(panel.ComplianceFailureReasons,
            reason => reason.Contains("PCP", StringComparison.Ordinal));
        await panel.HoldForComplianceCommand.ExecuteAsync(null);

        await using var finalRead = fixture.Factory.CreateDbContext();
        var statuses = await finalRead.Notes.AsNoTracking()
            .OrderBy(note => note.EventDate)
            .Select(note => note.Status)
            .ToListAsync();
        Assert.Equal([NoteStatus.Logged, NoteStatus.ComplianceBlocked], statuses);
        Assert.Equal([BeforeEnforcement, AfterEnforcement], settings.ResolvedDates);
    }

    [Fact]
    public async Task NotesLogUsesTheServiceDatePolicyBeforeChangingStatus()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var settings = new EffectiveDateSettingsService();
        var notes = fixture.NotesFromAnotherSession();
        await notes.AddNoteAsync(Note.Create(
            "Pending before enforcement.",
            BeforeEnforcement,
            NoteStatus.Pending,
            15,
            fixture.PersonOneId,
            noteType: NoteType.Contact));
        await notes.AddNoteAsync(Note.Create(
            "Pending after enforcement.",
            AfterEnforcement,
            NoteStatus.Pending,
            15,
            fixture.PersonOneId,
            noteType: NoteType.Contact));

        var log = fixture.NotesWindow(settings: settings);
        await log.NoteEntry.InitializeAsync();
        await log.ReloadAsync();
        var rows = log.NotesView.Cast<Note>()
            .OrderBy(note => note.EventDate)
            .ToArray();
        rows[0].Person.Forms.Add(new Form(
            FormType.PCP,
            new DateTime(2026, 8, 1),
            targetEffectiveDate: new DateTime(2026, 8, 1)));
        // Submission also requires goal progress; this test is about the policy date.
        foreach (var row in rows) row.GoalProgress = GoalProgressLevel.Moderate;

        log.SelectedNote = rows[0];
        await log.MarkNoteLoggedCommand.ExecuteAsync(null);

        Assert.False(log.IsComplianceDialogVisible);
        Assert.Equal(NoteStatus.Logged, rows[0].Status);

        log.SelectedNote = rows[1];
        await log.MarkNoteLoggedCommand.ExecuteAsync(null);

        Assert.True(log.IsComplianceDialogVisible);
        Assert.Equal(NoteStatus.Pending, rows[1].Status);
        await log.HoldForComplianceCommand.ExecuteAsync(null);

        Assert.Equal(NoteStatus.ComplianceBlocked, rows[1].Status);
        Assert.Equal([BeforeEnforcement, AfterEnforcement], settings.ResolvedDates);
    }

    private static void FillNote(
        NoteEntryViewModel panel,
        Person person,
        DateTime eventDate,
        string narrative)
    {
        panel.SelectedPerson = person;
        panel.SelectedNoteType = NoteType.Contact;
        panel.Status = NoteStatus.Logged;
        panel.EventDate = eventDate;
        panel.Minutes = 15;
        panel.GoalProgress = GoalProgressLevel.Moderate;
        panel.Narrative = narrative;
    }

    private sealed class EffectiveDateSettingsService : ISettingsService
    {
        public List<DateTime> ResolvedDates { get; } = [];

        public Task<Settings> LoadAsync() => Task.FromResult(new Settings
        {
            // This is the current-date projection. It intentionally disagrees
            // with dates before enforcement so the regression cannot pass by
            // continuing to read today's mask.
            BillingComplianceRequirements = BillingComplianceRequirements.Pcp
        });

        public Task<BillingComplianceRequirements>
            ResolveBillingComplianceRequirementsAsync(DateTime serviceDate)
        {
            ResolvedDates.Add(serviceDate.Date);
            return Task.FromResult(serviceDate.Date < EnforcementDate
                ? BillingComplianceRequirements.None
                : BillingComplianceRequirements.Pcp);
        }

        public Task SaveAsync(Settings settings) => Task.CompletedTask;
    }
}
