using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class ConsumerScheduleServiceTests
{
    private static SaveConsumerScheduleEntryRequest Appointment(int revision = 0) => new(
        ConsumerScheduleKind.DoctorAppointment, "Clinic visit", null,
        new DateTime(2026, 10, 15), null, null, ScheduleWeekdays.None,
        540, 600, ModivcareRideStatus.NeedsBooking, 480, null, null, revision);

    [Fact]
    public async Task LocalServiceRejectsOtherCaseloadAndForeignRowIds()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new ConsumerScheduleService(fixture.Factory, fixture.Session);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(fixture.OtherPersonId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SaveAsync(fixture.OtherPersonId, null, Appointment()));

        await using var db = fixture.Factory.CreateDbContext();
        var foreign = new ConsumerScheduleEntry { PersonId = fixture.OtherPersonId };
        foreign.Apply(Appointment());
        db.ConsumerScheduleEntries.Add(foreign);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(fixture.PersonId, foreign.Id, Appointment(foreign.Revision)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(fixture.PersonId, foreign.Id, foreign.Revision));
        Assert.Equal(1, await db.ConsumerScheduleEntries.CountAsync());
    }

    [Fact]
    public async Task LocalServiceRefusesStaleEditAndDelete()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new ConsumerScheduleService(fixture.Factory, fixture.Session);
        var first = await service.SaveAsync(fixture.PersonId, null, Appointment());
        var updated = await service.SaveAsync(fixture.PersonId, first.Id,
            Appointment(first.Revision) with { RideStatus = ModivcareRideStatus.Requested });
        Assert.Equal(first.Revision + 1, updated.Revision);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(fixture.PersonId, first.Id, Appointment(first.Revision)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(fixture.PersonId, first.Id, first.Revision));
        Assert.Equal(ModivcareRideStatus.Requested,
            Assert.Single(await service.GetAsync(fixture.PersonId)).RideStatus);
    }

    private sealed class Fixture(SqliteConnection connection,
        IDbContextFactory<SatiContext> factory, ISessionService session,
        int personId, int otherPersonId) : IAsyncDisposable
    {
        public IDbContextFactory<SatiContext> Factory { get; } = factory;
        public ISessionService Session { get; } = session;
        public int PersonId { get; } = personId;
        public int OtherPersonId { get; } = otherPersonId;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
            var factory = new ContextFactory(options);
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            var actor = User.Create(901, "schedule-cm", "Schedule CM", "hash", "salt",
                UserRole.CaseManager, null, 1);
            var peer = User.Create(902, "schedule-peer", "Schedule Peer", "hash", "salt",
                UserRole.CaseManager, null, 1);
            db.Users.AddRange(actor, peer);
            await db.SaveChangesAsync();
            var mine = Person.Rehydrate(0, actor.Id);
            mine.FirstName = "Mine";
            mine.LastName = "Consumer";
            mine.BirthDate = new DateTime(1990, 1, 1);
            mine.AgencyId = 1;
            var other = Person.Rehydrate(0, peer.Id);
            other.FirstName = "Other";
            other.LastName = "Consumer";
            other.BirthDate = new DateTime(1990, 1, 1);
            other.AgencyId = 1;
            db.People.AddRange(mine, other);
            await db.SaveChangesAsync();
            var session = new SessionService();
            session.SetUser(actor);
            return new Fixture(connection, factory, session, mine.Id, other.Id);
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class ContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
