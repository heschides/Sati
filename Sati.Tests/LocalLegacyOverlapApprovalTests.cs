using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class LocalLegacyOverlapApprovalTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task ApprovalIncludingComplianceOverrideCannotAcceptExistingOverlappingTime(bool useOverride, bool overlaps)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
        var factory = new ContextFactory(options);
        var reviewer = User.Create(811, "synthetic-reviewer", "Synthetic Reviewer", "hash", "salt", UserRole.Supervisor, null, 81);
        var author = User.Create(812, "synthetic-author", "Synthetic Author", "hash", "salt", UserRole.CaseManager, reviewer.Id, 81);
        int noteId;
        int revision;
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Agencies.Add(new Agency { Id = 81, Name = "Synthetic overlap test" });
            db.Users.AddRange(reviewer, author);
            var person = Person.CreatePerson(author.Id, "Synthetic", "Consumer", "", new DateTime(1990, 1, 1),
                DateTime.Today, WaiverType.Section21, new Settings());
            person.AgencyId = 81;
            db.People.Add(person);
            await db.SaveChangesAsync();
            var note = Note.Create("Synthetic candidate", DateTime.Today, NoteStatus.Logged, 30, person.Id);
            note.AgencyId = 81;
            note.StartTime = 90;
            var other = Note.Create("Synthetic interval", DateTime.Today, NoteStatus.Pending, 30, person.Id);
            other.AgencyId = 81;
            other.StartTime = overlaps ? 100 : 120;
            db.Notes.AddRange(note, other);
            await db.SaveChangesAsync();
            noteId = note.Id;
            revision = note.Revision;
        }
        var session = new SessionService();
        session.SetUser(reviewer);
        var service = new SupervisorService(factory, session);
        Func<Task> approve = () => useOverride
            ? service.ApproveWithOverrideAsync(noteId, reviewer.Id, "A compliance exception cannot override time.", revision)
            : service.ApproveNoteAsync(noteId, reviewer.Id, revision);
        if (overlaps)
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(approve);
            Assert.Contains("overlaps", failure.Message, StringComparison.OrdinalIgnoreCase);
        }
        else
            await approve();
        await using var verify = factory.CreateDbContext();
        var saved = await verify.Notes.AsNoTracking().SingleAsync(row => row.Id == noteId);
        Assert.Equal(overlaps ? NoteStatus.Logged : NoteStatus.Approved, saved.Status);
        Assert.Equal(overlaps ? revision : revision + 1, saved.Revision);
        if (overlaps)
        {
            Assert.Null(saved.ApprovedById);
            Assert.False(saved.ComplianceOverride);
        }
    }

    private sealed class ContextFactory(DbContextOptions<SatiContext> options) : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
