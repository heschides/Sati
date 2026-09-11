using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Reporting;
using Sati.Services.LocalAi;
using Xunit;

namespace Sati.Tests;

public sealed class LocalReviewPermissionRevocationTests
{
    private static readonly DateTime Today = new(2026, 9, 11);

    [Theory]
    [InlineData("caseload")]
    [InlineData("person")]
    [InlineData("appointments")]
    [InlineData("stage")]
    [InlineData("appointment-write")]
    [InlineData("ensure")]
    public async Task ReviewsRejectRevokedCaseManagementWithAssignmentsRetained(string operation)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var review = await SeedReviewAsync(fixture);
        var service = new ReviewItemService(fixture.Factory, Session(fixture.CaseManagerOne));
        await RevokeAsync(fixture, fixture.CaseManagerOne.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => RunReviewAsync(service, operation, fixture, review));

        await AssertReviewUnchangedAsync(fixture, review.Id);
    }

    [Theory]
    [InlineData("caseload")]
    [InlineData("person")]
    [InlineData("appointments")]
    [InlineData("stage")]
    [InlineData("appointment-write")]
    [InlineData("ensure")]
    public async Task ReviewsRejectAnUnrelatedAgency(string operation)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var review = await SeedReviewAsync(fixture);
        var service = new ReviewItemService(fixture.Factory, Session(fixture.CaseManagerTwo));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => RunReviewAsync(service, operation, fixture, review));

        await AssertReviewUnchangedAsync(fixture, review.Id);
    }

    [Fact]
    public async Task AuthorizedReviewWritesAndReadsStillWork()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var review = await SeedReviewAsync(fixture);
        var service = new ReviewItemService(fixture.Factory, Session(fixture.CaseManagerOne));

        await service.SetStageDateAsync(review.Id, ReviewStage.Requested, Today);
        await service.SetAppointmentAsync(review.Id, Today, "Synthetic provider");

        var saved = Assert.Single(await service.GetForPersonAsync(fixture.PersonOneId));
        Assert.Equal(Today, saved.RequestedDate);
        Assert.Equal(Today, (await service.GetLatestAppointmentsAsync(fixture.PersonOneId)).Medical?.Date);
        Assert.Single(await service.GetForCaseloadAsync(fixture.CaseManagerOne.Id));
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("filters")]
    [InlineData("pending")]
    [InlineData("noncompliant")]
    [InlineData("approve")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task SupervisorRevocationStopsReadsAndTransitionsWithoutRemovingAssignments(string operation)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var reviewer = User.Create(99, "synthetic-reviewer", "Synthetic Reviewer", "hash", "salt",
            UserRole.Supervisor, null, fixture.CaseManagerOne.AgencyId);
        reviewer.Permissions = UserPermissions.Supervision | UserPermissions.Billing;
        var note = Note.Create("Synthetic review test", Today, NoteStatus.Logged, 15, fixture.PersonOneId);
        note.AgencyId = reviewer.AgencyId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Users.Add(reviewer);
            (await db.Users.SingleAsync(user => user.Id == fixture.CaseManagerOne.Id)).SupervisorId = reviewer.Id;
            db.Notes.Add(note);
            await db.SaveChangesAsync();
        }
        var service = new SupervisorService(fixture.Factory, Session(reviewer));
        Assert.Single((await service.GetReviewPageAsync(reviewer.Id)).Notes);
        await RevokeAsync(fixture, reviewer.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            switch (operation)
            {
                case "queue": await service.GetReviewPageAsync(reviewer.Id); break;
                case "filters": await service.GetReviewFilterOptionsAsync(reviewer.Id); break;
                case "pending": await service.GetPendingNotesAsync(reviewer.Id); break;
                case "noncompliant": await service.GetNonCompliantNotesAsync(reviewer.Id); break;
                case "approve": await service.ApproveNoteAsync(note.Id, reviewer.Id, note.Revision); break;
                case "override": await service.ApproveWithOverrideAsync(note.Id, reviewer.Id, "Synthetic reason", note.Revision); break;
                case "return": await service.ReturnNoteAsync(note.Id, reviewer.Id, "Synthetic reason", note.Revision); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        });

        await using var verify = fixture.Factory.CreateDbContext();
        var stored = await verify.Notes.AsNoTracking().SingleAsync(item => item.Id == note.Id);
        Assert.Equal(NoteStatus.Logged, stored.Status);
        Assert.Equal(1, stored.Revision);
        Assert.Null(stored.ApprovedById);
        Assert.Null(stored.ReturnedById);
        Assert.Empty(await verify.AuditEvents.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnCaseworkReportsRejectRevokedPermission(bool lossReport)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var session = Session(fixture.CaseManagerOne);
        await RevokeAsync(fixture, fixture.CaseManagerOne.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            if (lossReport)
                await new ConsumerBillingLossReportService(fixture.Factory, session)
                    .GetAsync(fixture.CaseManagerOne.Id, Today.AddDays(-10), Today);
            else
                await new ProductivityReportService(fixture.Factory, session).GetUnitsAsync(Today.AddDays(-10), Today);
        });
    }

    private static async Task RunReviewAsync(ReviewItemService service, string operation,
        NoteEntryFixture fixture, ReviewItem review)
    {
        switch (operation)
        {
            case "caseload": await service.GetForCaseloadAsync(fixture.CaseManagerOne.Id); break;
            case "person": await service.GetForPersonAsync(fixture.PersonOneId); break;
            case "appointments": await service.GetLatestAppointmentsAsync(fixture.PersonOneId); break;
            case "stage": await service.SetStageDateAsync(review.Id, ReviewStage.Requested, Today); break;
            case "appointment-write": await service.SetAppointmentAsync(review.Id, Today, "Synthetic provider"); break;
            case "ensure": await service.EnsureCurrentCycleItemsAsync([await fixture.PersonOneAsync()], Today); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    [Fact]
    public async Task AdministrationCannotChangeStatusAcrossAnInconsistentPersonOwnerAgency()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var actor = fixture.CaseManagerOne;
        actor.Permissions = UserPermissions.Administration;
        var invalid = Person.CreatePerson(fixture.CaseManagerTwo.Id, "Synthetic", "Mismatched", "",
            new DateTime(1990, 1, 1), null, WaiverType.Section21, new Settings());
        invalid.AgencyId = actor.AgencyId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(user => user.Id == actor.Id)).Permissions = actor.Permissions;
            db.People.Add(invalid);
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.PeopleAs(actor)
            .SetPersonStatusAsync(invalid.Id, "NoLongerServed", "Synthetic marker test", 1));

        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Equal(PersonStatus.Active, (await verify.People.SingleAsync(person => person.Id == invalid.Id)).Status);
        Assert.Empty(await verify.AuditEvents.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(202)]
    public async Task CaseworkReportsDoNotCountNotesWithMissingOrConflictingAgency(int? noteAgency)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var good = Note.Create("Synthetic valid note", Today, NoteStatus.Logged, 15, fixture.PersonOneId);
        good.AgencyId = fixture.CaseManagerOne.AgencyId;
        var invalid = Note.Create("Synthetic invalid marker", Today, NoteStatus.Logged, 60, fixture.PersonOneId);
        invalid.AgencyId = noteAgency;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Notes.AddRange(good, invalid);
            await db.SaveChangesAsync();
        }
        var session = Session(fixture.CaseManagerOne);

        var units = Assert.Single(await new ProductivityReportService(fixture.Factory, session).GetUnitsAsync(Today, Today));
        var losses = await new ConsumerBillingLossReportService(fixture.Factory, session)
            .GetAsync(fixture.CaseManagerOne.Id, Today, Today);

        Assert.Equal(1, units.Units);
        Assert.Equal(1, losses.TotalBillableUnits);
        Assert.Equal(0, losses.TotalNonBillableUnits);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(202)]
    public async Task SupervisorCannotReadOrApproveANoteWithAnUnreconciledAgency(int? noteAgency)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var reviewer = User.Create(99, "marker-reviewer", "Synthetic Reviewer", "hash", "salt",
            UserRole.Supervisor, null, fixture.CaseManagerOne.AgencyId);
        var note = Note.Create("Synthetic invalid marker", Today, NoteStatus.Logged, 15, fixture.PersonOneId);
        note.AgencyId = noteAgency;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Users.Add(reviewer);
            (await db.Users.SingleAsync(user => user.Id == fixture.CaseManagerOne.Id)).SupervisorId = reviewer.Id;
            db.Notes.Add(note);
            await db.SaveChangesAsync();
        }
        var service = new SupervisorService(fixture.Factory, Session(reviewer));

        Assert.Empty((await service.GetReviewPageAsync(reviewer.Id)).Notes);
        Assert.Empty(await service.GetPendingNotesAsync(reviewer.Id));
        Assert.Empty(await service.GetNonCompliantNotesAsync(reviewer.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveNoteAsync(note.Id, reviewer.Id, 1));

        await using var verify = fixture.Factory.CreateDbContext();
        Assert.Equal(NoteStatus.Logged, (await verify.Notes.SingleAsync(item => item.Id == note.Id)).Status);
    }

    [Fact]
    public async Task AiContextDoesNotReleaseConsumerIdentityAfterCaseManagementRevocation()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var service = new ClientAiContextService(fixture.Factory, Session(fixture.CaseManagerOne));
        Assert.Equal(fixture.PersonOneId, (await service.BuildAsync(fixture.PersonOneId)).PersonId);
        await RevokeAsync(fixture, fixture.CaseManagerOne.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.BuildAsync(fixture.PersonOneId));
    }

    [Theory]
    [InlineData("people")]
    [InlineData("history")]
    [InlineData("holds")]
    public async Task RevokedAdministrationCannotReadConsumerListsOrHistory(string operation)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var actor = fixture.CaseManagerOne;
        actor.Permissions = UserPermissions.Administration;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(user => user.Id == actor.Id)).Permissions = actor.Permissions;
            await db.SaveChangesAsync();
        }
        var service = new AdminService(fixture.Factory, Session(actor), new PersonAuditPdfExporter(),
            new LocalLegalHoldRegistry(fixture.Factory));
        Assert.Equal(2, (await service.GetPeopleAsync()).Count);
        await RevokeAsync(fixture, actor.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            switch (operation)
            {
                case "people": await service.GetPeopleAsync(); break;
                case "history": await service.GetPersonHistoryAsync(fixture.PersonOneId); break;
                case "holds": await service.GetLegalHoldsAsync(fixture.PersonOneId); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        });
    }

    private static async Task<ReviewItem> SeedReviewAsync(NoteEntryFixture fixture)
    {
        await using var db = fixture.Factory.CreateDbContext();
        var person = await db.People.SingleAsync(item => item.Id == fixture.PersonOneId);
        person.EffectiveDate = Today.AddMonths(-6);
        var review = new ReviewItem(person.Id, person.EffectiveDate.Value, 1, ReviewCategory.Medical);
        db.ReviewItems.Add(review);
        await db.SaveChangesAsync();
        return review;
    }

    private static async Task AssertReviewUnchangedAsync(NoteEntryFixture fixture, int reviewId)
    {
        await using var db = fixture.Factory.CreateDbContext();
        var stored = await db.ReviewItems.AsNoTracking().SingleAsync(item => item.Id == reviewId);
        Assert.Null(stored.RequestedDate);
        Assert.Empty(await db.Appointments.ToListAsync());
        Assert.Single(await db.ReviewItems.ToListAsync());
    }

    private static async Task RevokeAsync(NoteEntryFixture fixture, int userId)
    {
        await using var db = fixture.Factory.CreateDbContext();
        (await db.Users.SingleAsync(user => user.Id == userId)).Permissions = UserPermissions.Billing;
        await db.SaveChangesAsync();
    }

    private static SessionService Session(User user)
    {
        var session = new SessionService();
        session.SetUser(user);
        return session;
    }
}
