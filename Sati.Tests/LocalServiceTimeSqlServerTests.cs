using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

[Collection("Local synthetic SQL schedule")]
public sealed class LocalServiceTimeSqlServerTests
{
    [LocalSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SimultaneousLocalCreatesOrMovesCannotDoubleReserveTime(bool update)
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = new SessionService();
        session.SetUser(fixture.Author);
        var service = new NoteService(fixture, session);
        var firstNote = Note.Create("Synthetic first interval", DateTime.Today.AddDays(-2), NoteStatus.Pending, 60, fixture.FirstPersonId);
        var secondNote = Note.Create("Synthetic second interval", DateTime.Today.AddDays(-1), NoteStatus.Pending, 60, fixture.SecondPersonId);
        firstNote.StartTime = secondNote.StartTime = 120;
        if (update)
        {
            await service.AddNoteAsync(firstNote);
            await service.AddNoteAsync(secondNote);
        }
        firstNote.EventDate = secondNote.EventDate = DateTime.Today;
        fixture.Pause.Arm();
        var first = AttemptAsync(firstNote);
        Task<Exception?>? second = null;
        try
        {
            await fixture.Pause.FirstPaused.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = AttemptAsync(secondNote);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!fixture.Pause.SecondReachedSave.Task.IsCompleted)
            {
                if (await fixture.HasWaitingLockAsync()) break;
                if (fixture.Pause.SecondReachedSave.Task.IsCompleted) break;
                Assert.False(second.IsCompleted, "The second local write must reach the old unsafe save or wait on the SQL schedule lock.");
                await Task.Delay(25, deadline.Token);
            }
        }
        finally { fixture.Pause.Release.TrySetResult(); }
        var outcomes = await Task.WhenAll(first, second!).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(outcomes, failure => failure is null);
        var rejected = Assert.Single(outcomes, failure => failure is not null);
        Assert.IsType<InvalidOperationException>(rejected);
        Assert.Contains("overlaps", rejected!.Message, StringComparison.OrdinalIgnoreCase);
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(1, await verify.Notes.CountAsync(note => note.EventDate == DateTime.Today && note.StartTime == 120));
        Assert.Equal(update ? 2 : 1, await verify.Notes.CountAsync());

        async Task<Exception?> AttemptAsync(Note note)
        {
            try
            {
                if (update) await service.UpdateNoteAsync(note);
                else await service.AddNoteAsync(note);
                return null;
            }
            catch (Exception failure) { return failure; }
        }
    }

    private sealed class Fixture : IDbContextFactory<SatiContext>, IAsyncDisposable
    {
        private readonly string _database = $"SatiSyntheticLocalPipeline_{Guid.NewGuid():N}";
        private bool _owned;
        public PauseFirstNoteSave Pause { get; } = new();
        public User Author { get; private set; } = null!;
        public int FirstPersonId { get; private set; }
        public int SecondPersonId { get; private set; }

        private string Connection(string catalog) => new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = catalog,
            IntegratedSecurity = true, Encrypt = false, ConnectTimeout = 15,
            ApplicationName = "Sati synthetic local schedule tests"
        }.ConnectionString;

        public SatiContext CreateDbContext() => new(new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer(Connection(_database)).AddInterceptors(Pause).Options);

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.ValidateName();
            try
            {
                await using (var connection = new SqlConnection(fixture.Connection("master")))
                {
                    await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = "IF DB_ID(@name) IS NOT NULL THROW 50000, 'Refusing existing test database.', 1; " +
                        $"CREATE DATABASE [{fixture._database}];";
                    command.Parameters.AddWithValue("@name", fixture._database);
                    await command.ExecuteNonQueryAsync();
                    fixture._owned = true;
                }
                await using var db = fixture.CreateDbContext();
                await db.Database.EnsureCreatedAsync();
                var agency = new Agency { Name = "Synthetic local schedule agency" };
                db.Agencies.Add(agency);
                await db.SaveChangesAsync();
                fixture.Author = User.Create(0, "synthetic-local-author", "Synthetic Author", "hash", "salt",
                    UserRole.CaseManager, null, agency.Id);
                db.Users.Add(fixture.Author);
                await db.SaveChangesAsync();
                var first = Person.CreatePerson(fixture.Author.Id, "Synthetic", "One", "", new DateTime(1990, 1, 1),
                    DateTime.Today, WaiverType.Section21, new Settings());
                var second = Person.CreatePerson(fixture.Author.Id, "Synthetic", "Two", "", new DateTime(1990, 1, 1),
                    DateTime.Today, WaiverType.Section21, new Settings());
                first.AgencyId = second.AgencyId = agency.Id;
                db.People.AddRange(first, second);
                await db.SaveChangesAsync();
                fixture.FirstPersonId = first.Id;
                fixture.SecondPersonId = second.Id;
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async Task<bool> HasWaitingLockAsync()
        {
            await using var connection = new SqlConnection(Connection(_database));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_database_id = DB_ID() " +
                "AND resource_type = 'APPLICATION' AND request_status = 'WAIT';";
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }

        private void ValidateName()
        {
            if (!Regex.IsMatch(_database, "\\ASatiSyntheticLocalPipeline_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Refusing a database outside the unique synthetic namespace.");
        }

        public async ValueTask DisposeAsync()
        {
            if (!_owned) return;
            ValidateName();
            using (var pooled = new SqlConnection(Connection(_database))) SqlConnection.ClearPool(pooled);
            await using var connection = new SqlConnection(Connection("master"));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}];";
            await command.ExecuteNonQueryAsync();
            _owned = false;
        }
    }

    private sealed class PauseFirstNoteSave : SaveChangesInterceptor
    {
        private int _armed;
        private int _attempts;
        public TaskCompletionSource FirstPaused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondReachedSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Volatile.Write(ref _armed, 1);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) != 1 || eventData.Context is not SatiContext db ||
                !db.ChangeTracker.Entries<Note>().Any(entry => entry.State is EntityState.Added or EntityState.Modified)) return result;
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                FirstPaused.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            else SecondReachedSave.TrySetResult();
            return result;
        }
    }
}

public sealed class LocalSqlTheoryAttribute : TheoryAttribute
{
    public LocalSqlTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("SATI_RUN_SQLSERVER_TESTS") != "1")
            Skip = "Set SATI_RUN_SQLSERVER_TESTS=1 on Windows to run against disposable synthetic LocalDB databases.";
    }
}

[CollectionDefinition("Local synthetic SQL schedule", DisableParallelization = true)]
public sealed class LocalSyntheticSqlScheduleCollection;
