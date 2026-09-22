using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class FormAttestationChangeReviewReadTests
{
    [Fact]
    public async Task ReadersAreLimitedToTheirAudienceAgencyAndAssignedCaseload()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var due = DateTime.Today.AddDays(-2);
        Form form;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(row => row.Id == fixture.PersonOneId);
            person.EffectiveDate = DateTime.Today.AddMonths(-6);
            form = new Form(FormType.Q3R, due, targetEffectiveDate: due.AddDays(-270))
            {
                PersonId = person.Id
            };
            db.Forms.Add(form);
            await db.SaveChangesAsync();
            var note = Note.Create("Completed review.", due, NoteStatus.Approved,
                15, person.Id, FormType.Q3R, NoteType.Form, formId: form.Id);
            note.AgencyId = fixture.CaseManagerOne.AgencyId;
            note.GoalProgress = GoalProgressLevel.None;
            db.Notes.Add(note);
            await db.SaveChangesAsync();
        }

        var caseManagerSession = SessionFor(fixture.CaseManagerOne);
        var forms = new FormService(fixture.Factory, caseManagerSession);
        await forms.AttestAsync(form, due);
        await forms.RevokeAttestationAsync(form, "Actual activity date needs review.");

        var assignedSupervisor = User.Create(77, "assigned-supervisor", "Assigned Supervisor",
            "hash", "salt", UserRole.Supervisor, null, fixture.CaseManagerOne.AgencyId);
        var unrelatedSupervisor = User.Create(78, "unrelated-supervisor", "Unrelated Supervisor",
            "hash", "salt", UserRole.Supervisor, null, fixture.CaseManagerOne.AgencyId);
        var agencyBiller = User.Create(79, "agency-biller", "Agency Biller",
            "hash", "salt", UserRole.Finance, null, fixture.CaseManagerOne.AgencyId);
        var foreignBiller = User.Create(80, "foreign-biller", "Foreign Biller",
            "hash", "salt", UserRole.Finance, null, fixture.CaseManagerTwo.AgencyId);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Users.AddRange(assignedSupervisor, unrelatedSupervisor, agencyBiller, foreignBiller);
            var owner = await db.Users.SingleAsync(row => row.Id == fixture.CaseManagerOne.Id);
            owner.SupervisorId = assignedSupervisor.Id;
            await db.SaveChangesAsync();
        }

        Assert.Single(await ReaderFor(fixture, assignedSupervisor).GetForSupervisorAsync());
        Assert.Empty(await ReaderFor(fixture, unrelatedSupervisor).GetForSupervisorAsync());
        Assert.Single(await ReaderFor(fixture, agencyBiller).GetForBillingAsync());
        Assert.Empty(await ReaderFor(fixture, foreignBiller).GetForBillingAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ReaderFor(fixture, fixture.CaseManagerOne).GetForSupervisorAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ReaderFor(fixture, assignedSupervisor).GetForBillingAsync());
    }

    private static FormAttestationChangeReviewService ReaderFor(NoteEntryFixture fixture, User actor) =>
        new(fixture.Factory, SessionFor(actor));

    private static SessionService SessionFor(User actor)
    {
        var session = new SessionService();
        session.SetUser(actor);
        return session;
    }
}
