using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Models.Assessments;
using Xunit;

namespace Sati.Tests;

public sealed class LocalAtAndPcpPermissionTests
{
    public static TheoryData<string, bool> RevokedOperations => new()
    {
        { "queue", false }, { "person", false }, { "request", false }, { "snapshot", false },
        { "add", false }, { "update", false }, { "publish", false }, { "publish-new", false },
        { "reopen", false }, { "delete", false }, { "pcp", false },
        { "queue", true }, { "person", true }, { "request", true }, { "snapshot", true },
        { "add", true }, { "update", true }, { "publish", true }, { "publish-new", true },
        { "reopen", true }, { "delete", true }, { "pcp", true }
    };

    [Theory]
    [MemberData(nameof(RevokedOperations))]
    public async Task RetainedAssignmentCannotAuthorizeARevokedCaseManager(string operation, bool refreshedSession)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var request = await SeedAsync(fixture, operation == "reopen");
        var session = SessionFor(fixture.CaseManagerOne);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var owner = await db.Users.SingleAsync(x => x.Id == fixture.CaseManagerOne.Id);
            owner.Permissions = UserPermissions.Billing;
            await db.SaveChangesAsync();
        }
        if (refreshedSession)
            fixture.CaseManagerOne.Permissions = UserPermissions.Billing;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => InvokeAsync(fixture, session, request, operation));
        await AssertRetainedAsync(fixture, request, operation == "reopen");
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("request")]
    [InlineData("snapshot")]
    [InlineData("add")]
    [InlineData("update")]
    [InlineData("publish")]
    [InlineData("reopen")]
    [InlineData("delete")]
    [InlineData("pcp")]
    public async Task AnotherAgencyCannotUseKnownConsumerAndRequestIds(string operation)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var request = await SeedAsync(fixture, operation == "reopen");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            InvokeAsync(fixture, SessionFor(fixture.CaseManagerTwo), request, operation));
        await AssertRetainedAsync(fixture, request, operation == "reopen");
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("request")]
    [InlineData("update")]
    [InlineData("pcp")]
    public async Task UnrelatedSameAgencyCaseManagerCannotUseKnownIds(string operation)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var request = await SeedAsync(fixture);
        var unrelated = User.Create(77, "unrelated", "Unrelated", "hash", "salt", UserRole.CaseManager,
            null, fixture.CaseManagerOne.AgencyId);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Users.Add(unrelated);
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            InvokeAsync(fixture, SessionFor(unrelated), request, operation));
    }

    [Theory]
    [InlineData("person")]
    [InlineData("request")]
    [InlineData("snapshot")]
    [InlineData("update")]
    [InlineData("pcp")]
    public async Task AConflictingConsumerAgencyMarkerFailsClosed(string operation)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var request = await SeedAsync(fixture);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(x => x.Id == fixture.PersonOneId);
            person.AgencyId = fixture.CaseManagerTwo.AgencyId;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            InvokeAsync(fixture, SessionFor(fixture.CaseManagerOne), request, operation));
    }

    [Fact]
    public async Task AuthorizedOwnerStillReadsAndPublishesWithoutTrustingCallerSigner()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var request = await SeedAsync(fixture);
        var session = SessionFor(fixture.CaseManagerOne);
        var service = new ATRequestService(fixture.Factory, new StubSettingsService(), session);
        Assert.Single(await service.GetAllForUserAsync(fixture.CaseManagerOne.Id));
        Assert.Single(await service.GetAllForPersonAsync(fixture.PersonOneId));
        Assert.Equal(new byte[] { 1, 2, 3 }, await service.GetSnapshotAsync(request.Id));
        Assert.NotNull(await new PersonCenteredPlanSourceService(fixture.Factory, session)
            .GetSourceAsync(fixture.PersonOneId, fixture.CaseManagerOne.Id));
        await service.PublishAsync(request, fixture.CaseManagerTwo);
        await using var db = fixture.Factory.CreateDbContext();
        var stored = await db.ATRequests.SingleAsync(x => x.Id == request.Id);
        Assert.Equal(fixture.CaseManagerOne.Id, stored.SignedByUserId);
        Assert.Equal(fixture.CaseManagerOne.DisplayName, stored.SignedByName);
    }

    [Fact]
    public async Task CurrentSupervisorWithoutCaseManagementKeepsAssignedConsumerAccess()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var request = await SeedAsync(fixture);
        var supervisor = User.Create(77, "reviewer", "Reviewer", "hash", "salt", UserRole.Supervisor,
            null, fixture.CaseManagerOne.AgencyId);
        supervisor.Permissions = UserPermissions.Supervision;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.Users.Add(supervisor);
            var owner = await db.Users.SingleAsync(x => x.Id == fixture.CaseManagerOne.Id);
            owner.SupervisorId = supervisor.Id;
            await db.SaveChangesAsync();
        }
        var session = SessionFor(supervisor);
        var service = new ATRequestService(fixture.Factory, new StubSettingsService(), session);
        Assert.NotNull(await service.GetByIdAsync(request.Id));
        Assert.NotNull(await new PersonCenteredPlanSourceService(fixture.Factory, session)
            .GetSourceAsync(fixture.PersonOneId, fixture.CaseManagerOne.Id));
        request.VendorName = "Reviewer correction";
        await service.UpdateAsync(request);

        await using (var db = fixture.Factory.CreateDbContext())
        {
            var storedSupervisor = await db.Users.SingleAsync(x => x.Id == supervisor.Id);
            storedSupervisor.Permissions = UserPermissions.Billing;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetByIdAsync(request.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new PersonCenteredPlanSourceService(fixture.Factory, session)
                .GetSourceAsync(fixture.PersonOneId, fixture.CaseManagerOne.Id));
    }

    [Fact]
    public async Task PreferredAuthorCannotSelectAnotherUsersWorkingAssessment()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await SeedAsync(fixture);
        var service = new PersonCenteredPlanSourceService(fixture.Factory, SessionFor(fixture.CaseManagerOne));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetSourceAsync(fixture.PersonOneId, fixture.CaseManagerTwo.Id));
    }

    [Fact]
    public async Task DisabledPcpAuthoringRejectsLocalWorkspaceButLeavesEvergreenGateEnabled()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await SeedAsync(fixture);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var settings = await db.Settings.SingleAsync(x => x.AgencyId == fixture.CaseManagerOne.AgencyId);
            settings.IsPersonCenteredPlanAuthoringEnabled = false;
            settings.BillingComplianceRequirements = BillingComplianceRequirements.Pcp;
            await db.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
            new PersonCenteredPlanSourceService(
                    fixture.Factory,
                    SessionFor(fixture.CaseManagerOne))
                .GetSourceAsync(fixture.PersonOneId, fixture.CaseManagerOne.Id));

        Assert.Contains("Evergreen completion", error.Message, StringComparison.Ordinal);
        await using var verification = fixture.Factory.CreateDbContext();
        var requirements = await verification.Settings
            .Where(settings => settings.AgencyId == fixture.CaseManagerOne.AgencyId)
            .Select(settings => settings.BillingComplianceRequirements)
            .SingleAsync();
        Assert.Equal(BillingComplianceRequirements.Pcp, requirements);
    }

    [Fact]
    public async Task DetachedRequestCannotRepointAnExistingRequestToAnAccessibleConsumer()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var request = await SeedAsync(fixture);
        request.PersonId = fixture.PersonTwoId;
        var service = new ATRequestService(fixture.Factory, new StubSettingsService(), SessionFor(fixture.CaseManagerOne));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateAsync(request));
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Equal(fixture.PersonOneId, (await db.ATRequests.SingleAsync()).PersonId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinarySaveCannotForgeAnAttestation(bool existing)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var request = existing ? await SeedAsync(fixture)
            : NewRequest(await fixture.PersonOneAsync(), fixture.CaseManagerOne);
        request.Publish(fixture.CaseManagerTwo, DateTime.UtcNow, 0.5m);
        var service = new ATRequestService(fixture.Factory, new StubSettingsService(), SessionFor(fixture.CaseManagerOne));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            existing ? service.UpdateAsync(request) : service.AddAsync(request));
        await using var db = fixture.Factory.CreateDbContext();
        var stored = await db.ATRequests.SingleOrDefaultAsync();
        if (existing)
        {
            Assert.NotNull(stored);
            Assert.False(stored.IsPublished);
            Assert.Null(stored.SignedByUserId);
            Assert.Equal(1, stored.Revision);
        }
        else Assert.Null(stored);
    }

    [Fact]
    public async Task PublishedRequestCannotBeDeletedThroughLocalService()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var request = await SeedAsync(fixture, published: true);
        var service = new ATRequestService(fixture.Factory, new StubSettingsService(), SessionFor(fixture.CaseManagerOne));
        await Assert.ThrowsAsync<AtRequestLockedException>(() => service.DeleteAsync(request));
        await AssertRetainedAsync(fixture, request, published: true);
    }

    private static async Task InvokeAsync(NoteEntryFixture fixture, ISessionService session, ATRequest request,
        string operation)
    {
        var service = new ATRequestService(fixture.Factory, new StubSettingsService(), session);
        switch (operation)
        {
            case "queue": await service.GetAllForUserAsync(fixture.CaseManagerOne.Id); break;
            case "person": await service.GetAllForPersonAsync(fixture.PersonOneId); break;
            case "request": await service.GetByIdAsync(request.Id); break;
            case "snapshot": await service.GetSnapshotAsync(request.Id); break;
            case "add": await service.AddAsync(NewRequest(await fixture.PersonOneAsync(), fixture.CaseManagerOne)); break;
            case "update": request.VendorName = "Forbidden edit"; await service.UpdateAsync(request); break;
            case "publish": await service.PublishAsync(request, session.CurrentUser!); break;
            case "publish-new": await service.PublishAsync(NewRequest(await fixture.PersonOneAsync(), fixture.CaseManagerOne), session.CurrentUser!); break;
            case "reopen": await service.ReopenAsync(request); break;
            case "delete": await service.DeleteAsync(request); break;
            case "pcp": await new PersonCenteredPlanSourceService(fixture.Factory, session)
                .GetSourceAsync(fixture.PersonOneId, fixture.CaseManagerOne.Id); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static async Task<ATRequest> SeedAsync(NoteEntryFixture fixture, bool published = false)
    {
        var request = NewRequest(await fixture.PersonOneAsync(), fixture.CaseManagerOne);
        request.AttachSnapshot([1, 2, 3]);
        if (published) request.Publish(fixture.CaseManagerOne, DateTime.UtcNow, 0.1m);
        await using var db = fixture.Factory.CreateDbContext();
        if (!await db.Settings.AnyAsync(x => x.AgencyId == fixture.CaseManagerOne.AgencyId))
        {
            db.Settings.Add(new Settings
            {
                AgencyId = fixture.CaseManagerOne.AgencyId,
                IsPersonCenteredPlanAuthoringEnabled = true
            });
        }
        db.ATRequests.Add(request);
        db.ComprehensiveAssessments.Add(new ComprehensiveAssessment
        {
            PersonId = fixture.PersonOneId, AuthorUserId = fixture.CaseManagerOne.Id,
            Status = AssessmentStatus.Approved, DocumentJson = "{\"answers\":{\"synthetic\":{\"narrative\":\"Synthetic only\"}}}"
        });
        await db.SaveChangesAsync();
        return request;
    }

    private static ATRequest NewRequest(Person person, User owner)
    {
        var request = ATRequest.CreateForClient(person, owner);
        request.VendorName = "Synthetic vendor";
        request.VendorBillingLocation = "Synthetic location";
        request.Items.Add(new ATRequestItem { Name = "Synthetic item", ItemCost = 10, Quantity = 1 });
        return request;
    }

    private static async Task AssertRetainedAsync(NoteEntryFixture fixture, ATRequest request, bool published)
    {
        await using var db = fixture.Factory.CreateDbContext();
        var stored = await db.ATRequests.SingleAsync(x => x.Id == request.Id);
        Assert.Equal("Synthetic vendor", stored.VendorName);
        Assert.Equal(1, stored.Revision);
        Assert.Equal(published, stored.IsPublished);
        Assert.Equal(fixture.CaseManagerOne.Id,
            (await db.People.SingleAsync(x => x.Id == fixture.PersonOneId)).UserId);
        Assert.Single(await db.ATRequests.ToListAsync());
    }

    private static SessionService SessionFor(User user)
    {
        var session = new SessionService();
        session.SetUser(user);
        return session;
    }
}
