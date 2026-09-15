using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Models.Assessments;
using Xunit;

namespace Sati.Tests;

public sealed class LocalConsumerPermissionRevocationTests
{
    public static TheoryData<string> Operations => new()
    {
        "journal-read", "journal-write", "journal-reminder", "caseload", "caseload-summary",
        "person-create", "person-status", "duplicate-lookup", "notes-person", "notes-month",
        "notes-year", "notes-day", "note-create", "note-update", "note-delete", "notes-abandon",
        "form-open", "form-prerequisite", "providers", "check-list", "check-create",
        "assessment-read", "assessment-create", "assessment-save", "assessment-submit", "ssn-status"
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task RevokedCaseManagementStopsPreviouslyAuthorizedLocalOperations(string operation)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var actor = fixture.CaseManagerOne;
        var session = new SessionService();
        session.SetUser(actor);
        var note = Note.Create("Synthetic retained note", DateTime.Today.AddDays(-60), NoteStatus.Pending, null, fixture.PersonOneId);
        note.AgencyId = actor.AgencyId;
        var formDue = DateTime.Today.AddDays(1);
        var form = new Form(
            FormType.PCP,
            formDue,
            targetEffectiveDate: formDue)
        {
            PersonId = fixture.PersonOneId
        };
        var assessment = new ComprehensiveAssessment { PersonId = fixture.PersonOneId, AuthorUserId = actor.Id, DocumentJson = "{}" };
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Notes.Add(note);
            db.Forms.Add(form);
            db.ComprehensiveAssessments.Add(assessment);
            var person = await db.People.SingleAsync(item => item.Id == fixture.PersonOneId);
            person.Journal = "Synthetic private journal";
            person.EffectiveDate = DateTime.Today.AddYears(-1);
            person.CredibleClientId = "synthetic-lookup";
            var persisted = await db.Users.SingleAsync(item => item.Id == actor.Id);
            persisted.Permissions = UserPermissions.Billing;
            await db.SaveChangesAsync();
        }

        // The session is deliberately NOT refreshed; an assignment is not permission.
        Assert.True(actor.HasCaseManagerPermissions);
        var people = fixture.PeopleAs(actor);
        var notes = new NoteService(fixture.Factory, session);
        var forms = new FormService(fixture.Factory, session);
        var checks = new CheckRequestService(fixture.Factory, session);
        var assessments = new ComprehensiveAssessmentService(fixture.Factory, session);
        Func<Task> action = operation switch
        {
            "journal-read" => () => people.GetJournalAsync(fixture.PersonOneId),
            "journal-write" => () => people.SaveJournalAsync(fixture.PersonOneId, "Should not save"),
            "journal-reminder" => () => people.AddJournalReminderAsync(fixture.PersonOneId, "Should not save"),
            "caseload" => () => people.GetAllPeopleAsync(actor.Id),
            "caseload-summary" => () => people.GetPeopleForSummaryAsync(actor.Id),
            "person-create" => () => people.AddPersonAsync(Person.CreatePerson(actor.Id, "New", "Synthetic", "Synthetic biography", new DateTime(1990, 1, 1), null, WaiverType.Section21, new Settings())),
            "person-status" => () => people.SetPersonStatusAsync(fixture.PersonOneId, "NoLongerServed", "Should not save", 1),
            "duplicate-lookup" => () => people.FindCredibleMatchesAsync(["synthetic-lookup"]),
            "notes-person" => () => notes.GetAllByPersonAsync(fixture.PersonOneId),
            "notes-month" => () => notes.GetMonthlyNotesAsync(actor.Id),
            "notes-year" => () => notes.GetByYearAsync(actor.Id, DateTime.Today.Year),
            "notes-day" => () => notes.GetDayScheduleAsync(actor.Id, DateTime.Today),
            "note-create" => () => notes.AddNoteAsync(Note.Create("Should not save", DateTime.Today, NoteStatus.Pending, null, fixture.PersonOneId)),
            "note-update" => () => notes.UpdateNoteAsync(note),
            "note-delete" => () => notes.DeleteNoteAsync(note),
            "notes-abandon" => () => notes.UpdateAbandonedNotesAsync(30),
            "form-open" => () => forms.UpdateFormAsync(form),
            "form-prerequisite" => () => forms.GetPrerequisiteStatusAsync(form),
            "providers" => () => new ConsumerProviderService(fixture.Factory, session).GetByPersonAsync(fixture.PersonOneId),
            "check-list" => () => checks.GetAllForPersonAsync(fixture.PersonOneId),
            "check-create" => () => checks.CreateDraftAsync(fixture.PersonOneId),
            "assessment-read" => () => assessments.GetLatestForAgendaAsync(fixture.PersonOneId),
            "assessment-create" => () => assessments.GetOrCreateDraftAsync(fixture.PersonOneId, actor.Id),
            "assessment-save" => () => assessments.SaveDocumentAsync(assessment, new AssessmentDocument()),
            "assessment-submit" => () => assessments.SubmitForReviewAsync(assessment),
            "ssn-status" => () => new DhhsFormService(fixture.Factory, session,
                new LocalSsnStore(new EnvelopeProtector(new DpapiKeyWrapper()))).GetSsnStatusAsync(fixture.PersonOneId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(action);
        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Equal("Synthetic private journal", await verification.People.Where(item => item.Id == fixture.PersonOneId).Select(item => item.Journal).SingleAsync());
        Assert.Equal(actor.Id, await verification.People.Where(item => item.Id == fixture.PersonOneId).Select(item => item.UserId).SingleAsync());
        Assert.Equal(NoteStatus.Pending, (await verification.Notes.SingleAsync(item => item.Id == note.Id)).Status);
        Assert.Equal(AssessmentStatus.Draft, (await verification.ComprehensiveAssessments.SingleAsync(item => item.Id == assessment.Id)).Status);
        Assert.Empty(await verification.AuditEvents.ToListAsync());
    }

    [Theory]
    [InlineData("permissions")]
    [InlineData("agency")]
    [InlineData("role")]
    public async Task CachedSupervisorCannotKeepReadingAfterActorFactsChange(string change)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var supervisor = User.Create(40, "supervisor", "Synthetic Supervisor", "", "", UserRole.Supervisor, null, fixture.CaseManagerOne.AgencyId);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Users.Add(supervisor);
            var owner = await db.Users.SingleAsync(item => item.Id == fixture.CaseManagerOne.Id);
            owner.SupervisorId = supervisor.Id;
            await db.SaveChangesAsync();
        }
        var service = fixture.PeopleAs(supervisor);
        Assert.NotEmpty(await service.GetPeopleForSummaryAsync(fixture.CaseManagerOne.Id));
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var persisted = await db.Users.SingleAsync(item => item.Id == supervisor.Id);
            if (change == "permissions") persisted.Permissions = UserPermissions.Billing;
            if (change == "agency") persisted.AgencyId = fixture.CaseManagerTwo.AgencyId;
            if (change == "role") persisted.Role = UserRole.CaseManager;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetPeopleForSummaryAsync(fixture.CaseManagerOne.Id));
    }

    [Fact]
    public async Task RetainedAssignmentsDoNotGiveAFreshBillingOnlySessionClinicalAccess()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var persisted = await db.Users.SingleAsync(item => item.Id == fixture.CaseManagerOne.Id);
            persisted.Permissions = UserPermissions.Billing;
            await db.SaveChangesAsync();
        }
        fixture.CaseManagerOne.Permissions = UserPermissions.Billing;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.PeopleAs(fixture.CaseManagerOne).GetJournalAsync(fixture.PersonOneId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.PeopleAs(fixture.CaseManagerOne).GetAllPeopleAsync(fixture.CaseManagerOne.Id));
    }

    [Fact]
    public async Task JournalAccessCannotUseAnotherPersonsIdentifierOrAnInconsistentTenantOwner()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.PeopleAs(fixture.CaseManagerTwo).GetJournalAsync(fixture.PersonOneId));
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(item => item.Id == fixture.PersonOneId);
            person.AgencyId = fixture.CaseManagerTwo.AgencyId;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.PeopleAs(fixture.CaseManagerOne).GetJournalAsync(fixture.PersonOneId));
        var remaining = await fixture.PeopleAs(fixture.CaseManagerOne).GetAllPeopleAsync(fixture.CaseManagerOne.Id);
        Assert.DoesNotContain(remaining, item => item.Id == fixture.PersonOneId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(202)]
    public async Task NotesWithUnresolvedOrConflictingTenantMarkersAreNotReturnedOrChanged(int? noteAgencyId)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var note = Note.Create("Unresolved synthetic note", DateTime.Today.AddDays(-2), NoteStatus.Pending, null, fixture.PersonOneId);
        note.AgencyId = noteAgencyId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Notes.Add(note);
            await db.SaveChangesAsync();
        }
        var notes = new NoteService(fixture.Factory, session);
        var people = fixture.PeopleAs(fixture.CaseManagerOne);

        Assert.Empty(await notes.GetAllByPersonAsync(fixture.PersonOneId));
        Assert.Empty(await notes.GetByYearAsync(fixture.CaseManagerOne.Id, DateTime.Today.Year));
        Assert.Empty(await notes.GetMonthlyNotesAsync(fixture.CaseManagerOne.Id));
        Assert.Empty(await notes.GetDayScheduleAsync(fixture.CaseManagerOne.Id, DateTime.Today.AddDays(-2)));
        Assert.Empty((await people.GetAllPeopleAsync(fixture.CaseManagerOne.Id)).Single(person => person.Id == fixture.PersonOneId).Notes);
        Assert.Empty((await people.GetPeopleForSummaryAsync(fixture.CaseManagerOne.Id)).Single(person => person.Id == fixture.PersonOneId).NoteSummaries);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => notes.UpdateNoteAsync(note));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => notes.DeleteNoteAsync(note));
        await notes.UpdateAbandonedNotesAsync(1);

        await using var verification = fixture.Factory.CreateDbContext();
        var retained = await verification.Notes.SingleAsync(item => item.Id == note.Id);
        Assert.Equal(NoteStatus.Pending, retained.Status);
        Assert.Equal(noteAgencyId, retained.AgencyId);
        Assert.Empty(await verification.AuditEvents.ToListAsync());
    }
}
