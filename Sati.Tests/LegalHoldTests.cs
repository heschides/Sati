using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Reporting;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Placing and releasing a legal hold, and the registry rule-3 deletion will consult.
///
/// <para>
/// Deliberately narrower than OPERATIONS.md's full record-class/scope hold model — this exists
/// only to gate consumer deletion. The one property that actually matters here is the fail-closed
/// one: <see cref="ILegalHoldRegistry.GetStatusAsync"/> must never return
/// <see cref="LegalHoldStatus.Clear"/> except after successfully confirming no active hold.
/// </para>
/// </summary>
public sealed class LegalHoldTests
{
    [Fact]
    public async Task AnAdminCanPlaceAHoldOnAConsumerInTheirAgency()
    {
        await using var fixture = await Fixture.CreateAsync();

        var hold = await fixture.AdminService.PlaceLegalHoldAsync(new PlaceLegalHoldRequest(
            fixture.PersonId, "MaineCare program integrity review", "PI-2026-014",
            "MaineCare Program Integrity", DateTime.UtcNow));

        Assert.Equal(fixture.PersonId, hold.PersonId);
        Assert.False(hold.IsReleased);
        Assert.Equal("MaineCare program integrity review", hold.Reason);
    }

    [Fact]
    public async Task ACaseManagerCannotPlaceAHold()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CaseManagerService.PlaceLegalHoldAsync(new PlaceLegalHoldRequest(
                fixture.PersonId, "Attempted forgery", null, null, DateTime.UtcNow)));
    }

    [Fact]
    public async Task AReasonIsRequiredToPlaceAHold()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.AdminService.PlaceLegalHoldAsync(new PlaceLegalHoldRequest(
                fixture.PersonId, "  ", null, null, DateTime.UtcNow)));
    }

    [Fact]
    public async Task AnAdminCannotPlaceAHoldOnAConsumerInAnotherAgency()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AdminService.PlaceLegalHoldAsync(new PlaceLegalHoldRequest(
                fixture.ForeignPersonId, "Wrong agency", null, null, DateTime.UtcNow)));
    }

    [Fact]
    public async Task AnAdminCanRequestButCannotUnilaterallyReleaseAHold()
    {
        await using var fixture = await Fixture.CreateAsync();
        var placed = await fixture.AdminService.PlaceLegalHoldAsync(new PlaceLegalHoldRequest(
            fixture.PersonId, "Under review", null, null, DateTime.UtcNow));

        var released = await fixture.AdminService.ReleaseLegalHoldAsync(placed.Id, "Review concluded.");

        Assert.False(released.IsReleased);
        Assert.Null(released.ReleasedAtUtc);
        Assert.Equal(LegalHoldStatus.Active, await new LocalLegalHoldRegistry(fixture.Factory).GetStatusAsync(fixture.AgencyId, fixture.PersonId));
    }

    // The compatibility action requests release; replay cannot bypass independent approval.
    [Fact]
    public async Task AnAlreadyReleasedHoldCannotBeReleasedAgain()
    {
        await using var fixture = await Fixture.CreateAsync();
        var placed = await fixture.AdminService.PlaceLegalHoldAsync(new PlaceLegalHoldRequest(
            fixture.PersonId, "Under review", null, null, DateTime.UtcNow));
        await fixture.AdminService.ReleaseLegalHoldAsync(placed.Id, null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.AdminService.ReleaseLegalHoldAsync(placed.Id, null));
    }

    [Fact]
    public async Task GetLegalHoldsListsBothReleasedAndActiveHoldsNewestFirst()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.AdminService.PlaceLegalHoldAsync(new PlaceLegalHoldRequest(
            fixture.PersonId, "First hold", null, null, DateTime.UtcNow));
        await fixture.AdminService.ReleaseLegalHoldAsync(first.Id, null);
        await fixture.ApprovePendingRelease();
        var second = await fixture.AdminService.PlaceLegalHoldAsync(new PlaceLegalHoldRequest(
            fixture.PersonId, "Second hold", null, null, DateTime.UtcNow));

        var holds = await fixture.AdminService.GetLegalHoldsAsync(fixture.PersonId);

        Assert.Equal(2, holds.Count);
        Assert.Equal(second.Id, holds[0].Id);
        Assert.False(holds[0].IsReleased);
        Assert.True(holds[1].IsReleased);
    }

    // ---- The registry rule-3 deletion will actually consult ----

    [Fact]
    public async Task TheRegistryReportsActiveWhileAnUnreleasedHoldExists()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AdminService.PlaceLegalHoldAsync(new PlaceLegalHoldRequest(
            fixture.PersonId, "Under review", null, null, DateTime.UtcNow));
        var registry = new LocalLegalHoldRegistry(fixture.Factory);

        var status = await registry.GetStatusAsync(fixture.AgencyId, fixture.PersonId);

        Assert.Equal(LegalHoldStatus.Active, status);
    }

    [Fact]
    public async Task TheRegistryReportsClearWhenNoHoldExists()
    {
        await using var fixture = await Fixture.CreateAsync();
        var registry = new LocalLegalHoldRegistry(fixture.Factory);

        var status = await registry.GetStatusAsync(fixture.AgencyId, fixture.PersonId);

        Assert.Equal(LegalHoldStatus.Clear, status);
    }

    [Fact]
    public async Task TheRegistryReportsClearAgainAfterTheOnlyHoldIsReleased()
    {
        await using var fixture = await Fixture.CreateAsync();
        var placed = await fixture.AdminService.PlaceLegalHoldAsync(new PlaceLegalHoldRequest(
            fixture.PersonId, "Under review", null, null, DateTime.UtcNow));
        await fixture.AdminService.ReleaseLegalHoldAsync(placed.Id, null);
        await fixture.ApprovePendingRelease();
        var registry = new LocalLegalHoldRegistry(fixture.Factory);

        var status = await registry.GetStatusAsync(fixture.AgencyId, fixture.PersonId);

        Assert.Equal(LegalHoldStatus.Clear, status);
    }

    // Fail-closed: a registry that cannot reach its data must never be mistaken for "no hold."
    [Fact]
    public async Task TheRegistryReportsUnavailableWhenTheQueryFails()
    {
        var registry = new LocalLegalHoldRegistry(new ThrowingContextFactory());

        var status = await registry.GetStatusAsync(1, 1);

        Assert.Equal(LegalHoldStatus.Unavailable, status);
    }

    private sealed class ThrowingContextFactory : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => throw new InvalidOperationException("simulated outage");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, DbContextOptions<SatiContext> options)
        {
            _connection = connection;
            Factory = new TestContextFactory(options);
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public int AgencyId { get; private set; }
        public int PersonId { get; private set; }
        public int ForeignPersonId { get; private set; }
        public AdminService AdminService { get; private set; } = null!;
        public AdminService CaseManagerService { get; private set; } = null!;
        private User reviewer = null!;
        public async Task ApprovePendingRelease()
        {
            var session = new SessionService(); session.SetUser(reviewer);
            var service = new RecordsGovernanceService(Factory, session);
            var hold = (await service.GetHoldsAsync()).Single(x => x.ReleaseRequestedById is not null);
            await service.ChangeHoldAsync(new(Guid.NewGuid(), GovernanceHoldAction.ApproveRelease, hold.Id, hold.Revision, hold.Scope, hold.RecordClass, hold.PersonId, hold.RecordId, "Independent synthetic approval"));
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
            var fixture = new Fixture(connection, options);
            await fixture.SeedAsync();
            return fixture;
        }

        private async Task SeedAsync()
        {
            await using var db = Factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();

            var agency = new Agency { Id = 301, Name = "Agency Legal Hold" };
            var foreignAgency = new Agency { Id = 302, Name = "Agency Other" };
            var admin = User.Create(1301, "admin-legal-hold", "Admin", "hash", "salt", UserRole.Admin, null, agency.Id);
            var caseManager = User.Create(
                1302, "case-manager-legal-hold", "Case Manager", "hash", "salt", UserRole.CaseManager, null, agency.Id);
            var foreignCaseManager = User.Create(
                1402, "foreign-legal-hold", "Foreign Case Manager", "hash", "salt", UserRole.CaseManager, null, foreignAgency.Id);
            db.Agencies.AddRange(agency, foreignAgency);
            reviewer = User.Create(1303, "reviewer-legal-hold", "Reviewer", "hash", "salt", UserRole.Admin, null, agency.Id);
            db.Users.AddRange(admin, reviewer, caseManager, foreignCaseManager);

            var person = Person.CreatePerson(
                caseManager.Id, "Held", "Consumer", "Synthetic record.",
                new DateTime(1990, 1, 1), null, WaiverType.None, new Settings());
            person.AgencyId = agency.Id;
            var foreignPerson = Person.CreatePerson(
                foreignCaseManager.Id, "Foreign", "Consumer", "Synthetic record.",
                new DateTime(1990, 1, 1), null, WaiverType.None, new Settings());
            foreignPerson.AgencyId = foreignAgency.Id;
            db.People.AddRange(person, foreignPerson);
            await db.SaveChangesAsync();

            AgencyId = agency.Id;
            PersonId = person.Id;
            ForeignPersonId = foreignPerson.Id;
            AdminService = Build(admin);
            CaseManagerService = Build(caseManager);
        }

        private AdminService Build(User actor)
        {
            var session = new SessionService();
            session.SetUser(actor);
            return new AdminService(
                Factory, session, new PersonAuditPdfExporter(), new LocalLegalHoldRegistry(Factory));
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

        private sealed class TestContextFactory(DbContextOptions<SatiContext> options)
            : IDbContextFactory<SatiContext>
        {
            public SatiContext CreateDbContext() => new(options);
        }
    }
}
