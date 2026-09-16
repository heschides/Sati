using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The desktop's local services load contact history themselves and enforce
/// monthly contact the same way the API does.
/// </summary>
public sealed class LocalMonthlyContactTests
{
    private static readonly DateTime Effective = DateTime.Today.AddDays(-100);

    [Fact]
    public async Task TheCaseloadCarriesContactHistoryAndSubmissionEnforcesIt()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var agencyId = fixture.CaseManagerOne.AgencyId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(item => item.Id == fixture.PersonOneId);
            person.EffectiveDate = Effective;
            db.BillingCompliancePolicyVersions.Add(BillingCompliancePolicyVersion.Create(
                agencyId,
                BillingComplianceRequirements.MonthlyContact,
                Effective.AddDays(-100),
                Effective.AddDays(-100),
                fixture.CaseManagerOne.Id,
                DateTime.UtcNow));
            var visit = Note.Create("Visit.", Effective.AddDays(10), NoteStatus.Logged, 30,
                fixture.PersonOneId, noteType: NoteType.Visit);
            visit.AgencyId = agencyId;
            var scheduled = Note.Create("Planned.", Effective.AddDays(20), NoteStatus.Scheduled, 30,
                fixture.PersonOneId, noteType: NoteType.Visit);
            scheduled.AgencyId = agencyId;
            db.Notes.AddRange(visit, scheduled);
            await db.SaveChangesAsync();
        }

        var caseload = await fixture.PeopleAs(fixture.CaseManagerOne)
            .GetAllPeopleAsync(fixture.CaseManagerOne.Id);
        var loaded = caseload.Single(item => item.Id == fixture.PersonOneId);
        var status = loaded.GetMonthlyContactStatus(DateTime.Today);
        Assert.NotNull(status);
        Assert.Equal(Effective.AddDays(10), status.LastContactOn);
        Assert.True(status.IsOverdue);

        var notes = fixture.NotesFromAnotherSession();
        await Assert.ThrowsAsync<NoteSubmissionException>(() =>
            notes.AddNoteAsync(Logged(fixture.PersonOneId, Effective.AddDays(60), NoteType.Other)));

        // The call is its own contact, and it restarts the clock for later work.
        await notes.AddNoteAsync(Logged(fixture.PersonOneId, Effective.AddDays(61), NoteType.Phone));
        await notes.AddNoteAsync(Logged(fixture.PersonOneId, Effective.AddDays(62), NoteType.Other));
    }

    private static Note Logged(int personId, DateTime date, NoteType type)
    {
        var note = Note.Create("Synthetic contact test.", date, NoteStatus.Logged, 15, personId,
            noteType: type);
        note.GoalProgress = GoalProgressLevel.None;
        return note;
    }
}
