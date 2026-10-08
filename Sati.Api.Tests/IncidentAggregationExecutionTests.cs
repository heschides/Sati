using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

// Independent synthetic SQLite databases exercise the configured retrying strategy's
// transaction guard. They do not establish SQL Server locking or historical deduplication.
public sealed class IncidentAggregationExecutionTests
{
    [Fact]
    public async Task DirectIncidentAggregationPersistsWithAConfiguredRetryingStrategy()
    {
        await using var fixture = await IncidentFixture.CreateAsync();
        Assert.Null(ExecutionStrategy.Current);
        Assert.True(fixture.ConfiguredStrategyRetries);
        var report = Report("DIRECT_001");

        var result = await fixture.Aggregator.UpsertAsync(report);

        var persisted = Assert.Single(await fixture.LoadAsync());
        Assert.Equal(result.Id, persisted.Id);
        Assert.Equal(1, persisted.OccurrenceCount);
        Assert.Equal(report.Reference, persisted.LastReference);
        Assert.Equal(report.AgencyId, persisted.AgencyId);
        Assert.Equal(report.Scope, persisted.Scope);
        Assert.Equal(report.Fingerprint, persisted.ExceptionFingerprint);
        Assert.Null(ExecutionStrategy.Current);
        fixture.AssertAttempts(contexts: 1, starts: 1, queries: 1, saves: 1, commits: 1, completedCommits: 1);
    }

    [Fact]
    public async Task RetryingOuterScopeIsRefusedBeforeAnyContextTransactionOrWrite()
    {
        await using var fixture = await IncidentFixture.CreateAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.WithRetryingScopeAsync(() => fixture.Aggregator.UpsertAsync(Report("OUTER_001"))));

        Assert.Contains("retry", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(error.InnerException);
        fixture.AssertAttempts(contexts: 0, starts: 0, queries: 0, saves: 0, commits: 0, completedCommits: 0);
        Assert.Empty(await fixture.LoadAsync());
        Assert.Null(ExecutionStrategy.Current);

        // A refusal must neither acquire nor strand the aggregation gate.
        await fixture.WithSingleAttemptScopeAsync(() => fixture.Aggregator.UpsertAsync(Report("OUTER_002")))
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("OUTER_002", Assert.Single(await fixture.LoadAsync()).LastReference);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImmediateSameReferenceReplayKeepsCountAndANewReferenceIncrements(bool useOuterSingleAttempt)
    {
        await using var fixture = await IncidentFixture.CreateAsync();
        var report = Report("REPLAY_001");

        Task<ServerIncidentGroup> Upsert(IncidentAggregation next) => useOuterSingleAttempt
            ? fixture.WithSingleAttemptScopeAsync(() => fixture.Aggregator.UpsertAsync(next))
            : fixture.Aggregator.UpsertAsync(next);

        var first = await Upsert(report);
        var replay = await Upsert(report);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(1, Assert.Single(await fixture.LoadAsync()).OccurrenceCount);

        var next = await Upsert(report with
        {
            Reference = "REPLAY_002",
            Severity = IncidentSeverities.Critical,
            OccurredAtUtc = report.OccurredAtUtc.AddMinutes(1)
        });

        var persisted = Assert.Single(await fixture.LoadAsync());
        Assert.Equal(first.Id, next.Id);
        Assert.Equal(2, persisted.OccurrenceCount);
        Assert.Equal("REPLAY_002", persisted.LastReference);
        Assert.Equal(IncidentSeverities.Critical, persisted.Severity);
        fixture.AssertAttempts(contexts: 3, starts: 3, queries: 3, saves: 3, commits: 3, completedCommits: 3);
    }

    [Fact]
    public async Task AlreadyCancelledCallDoesNoWorkAndALaterCallCanAcquireTheGate()
    {
        await using var fixture = await IncidentFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Aggregator.UpsertAsync(Report("CANCEL_001"), cancellation.Token));

        fixture.AssertAttempts(contexts: 0, starts: 0, queries: 0, saves: 0, commits: 0, completedCommits: 0);
        Assert.Empty(await fixture.LoadAsync());
        await fixture.WithSingleAttemptScopeAsync(() => fixture.Aggregator.UpsertAsync(Report("CANCEL_002")))
            .WaitAsync(TimeSpan.FromSeconds(10));
        fixture.AssertAttempts(contexts: 1, starts: 1, queries: 1, saves: 1, commits: 1, completedCommits: 1);
    }

    [Fact]
    public async Task CancellationAfterTheDatabaseWriteRollsBackAndReleasesTheGateWithoutReplay()
    {
        await using var fixture = await IncidentFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.Saves.AfterSaveOnce = token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Aggregator.UpsertAsync(Report("MID_CANCEL_001"), cancellation.Token));

        Assert.Null(ExecutionStrategy.Current);
        fixture.AssertAttempts(contexts: 1, starts: 1, queries: 1, saves: 1, commits: 0, completedCommits: 0);
        Assert.Equal(1, fixture.Saves.CompletedSaves);
        Assert.Equal(1, fixture.Saves.LastSavedRows);
        Assert.Empty(await fixture.LoadAsync());
        await fixture.Aggregator.UpsertAsync(Report("MID_CANCEL_002")).WaitAsync(TimeSpan.FromSeconds(10));
        var persisted = Assert.Single(await fixture.LoadAsync());
        Assert.Equal("MID_CANCEL_002", persisted.LastReference);
        Assert.Equal(1, persisted.OccurrenceCount);
        fixture.AssertAttempts(contexts: 2, starts: 2, queries: 2, saves: 2, commits: 1, completedCommits: 1);
    }

    [Fact]
    public async Task AConfiguredRetriableSaveFaultIsNotReplayedAndDoesNotStrandTheGate()
    {
        await using var fixture = await IncidentFixture.CreateAsync(retrySyntheticFailures: true);
        var failure = new SyntheticRetriableFailure();
        fixture.Saves.AfterSaveOnce = _ => throw failure;

        var observed = await Assert.ThrowsAsync<SyntheticRetriableFailure>(() =>
            fixture.Aggregator.UpsertAsync(Report("SAVE_FAIL_001")));

        Assert.Same(failure, observed);
        Assert.Null(ExecutionStrategy.Current);
        fixture.AssertAttempts(contexts: 1, starts: 1, queries: 1, saves: 1, commits: 0, completedCommits: 0);
        Assert.Equal(1, fixture.Saves.CompletedSaves);
        Assert.Equal(1, fixture.Saves.LastSavedRows);
        Assert.Empty(await fixture.LoadAsync());
        await fixture.Aggregator.UpsertAsync(Report("SAVE_FAIL_002")).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, Assert.Single(await fixture.LoadAsync()).OccurrenceCount);
        fixture.AssertAttempts(contexts: 2, starts: 2, queries: 2, saves: 2, commits: 1, completedCommits: 1);
    }

    [Fact]
    public async Task APostCommitAcknowledgementFaultIsNotReplayedAndImmediateExplicitReplayKeepsCount()
    {
        await using var fixture = await IncidentFixture.CreateAsync(retrySyntheticFailures: true);
        var report = Report("COMMIT_ACK_001");
        var failure = new SyntheticRetriableFailure();
        fixture.Transactions.AfterCommitOnce = () => throw failure;

        var observed = await Assert.ThrowsAsync<SyntheticRetriableFailure>(() =>
            fixture.Aggregator.UpsertAsync(report));

        Assert.Same(failure, observed);
        Assert.Null(ExecutionStrategy.Current);
        fixture.AssertAttempts(contexts: 1, starts: 1, queries: 1, saves: 1, commits: 1, completedCommits: 1);
        var persisted = Assert.Single(await fixture.LoadAsync());
        Assert.Equal(report.Reference, persisted.LastReference);
        Assert.Equal(1, persisted.OccurrenceCount);

        // The test knows its private SQLite commit completed. A real caller must
        // reconcile an ambiguous result; it cannot infer success from the exception.
        await fixture.Aggregator.UpsertAsync(report).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, Assert.Single(await fixture.LoadAsync()).OccurrenceCount);
        fixture.AssertAttempts(contexts: 2, starts: 2, queries: 2, saves: 2, commits: 2, completedCommits: 2);
        await fixture.Aggregator.UpsertAsync(report with { Reference = "COMMIT_ACK_002" });
        Assert.Equal(2, Assert.Single(await fixture.LoadAsync()).OccurrenceCount);
        fixture.AssertAttempts(contexts: 3, starts: 3, queries: 3, saves: 3, commits: 3, completedCommits: 3);
    }

    private static IncidentAggregation Report(string reference) => new(
        1, IncidentScopes.Agency, "Api", IncidentSeverities.Error,
        "synthetic.incident.execution", "1.0.0", new string('A', 64),
        new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), reference, "Admin", null);

    private sealed class IncidentFixture : IAsyncDisposable
    {
        private readonly SyntheticPipelineDatabase _database;
        private readonly DbContextOptions<ApiDbContext> _actionOptions;
        private readonly CountingFactory _factory;
        private readonly QueryProbe _queries;
        public IncidentAggregator Aggregator { get; }
        public SaveProbe Saves { get; }
        public TransactionProbe Transactions { get; }
        public bool ConfiguredStrategyRetries
        {
            get
            {
                using var db = new ApiDbContext(_actionOptions);
                return db.Database.CreateExecutionStrategy().RetriesOnFailure;
            }
        }

        private IncidentFixture(SyntheticPipelineDatabase database, DbContextOptions<ApiDbContext> actionOptions,
            CountingFactory factory, SaveProbe saves, TransactionProbe transactions, QueryProbe queries)
        {
            _database = database;
            _actionOptions = actionOptions;
            _factory = factory;
            Saves = saves;
            Transactions = transactions;
            _queries = queries;
            Aggregator = new IncidentAggregator(factory);
        }

        public static async Task<IncidentFixture> CreateAsync(bool retrySyntheticFailures = false)
        {
            var database = new SyntheticPipelineDatabase();
            try
            {
                await database.InitializeAsync();
                await using (var db = new ApiDbContext(database.Options()))
                {
                    db.Agencies.Add(new ServerAgency { Id = 1, Name = "Synthetic incident execution agency" });
                    await db.SaveChangesAsync();
                }
                var saves = new SaveProbe();
                var transactions = new TransactionProbe();
                var queries = new QueryProbe();
                var builder = new DbContextOptionsBuilder<ApiDbContext>(database.Options(saves, transactions, queries));
                if (retrySyntheticFailures)
                    builder.ReplaceService<IExecutionStrategyFactory, SyntheticRetryingStrategyFactory>();
                else
                    builder.ReplaceService<IExecutionStrategyFactory, TestRetryingExecutionStrategyFactory>();
                var options = builder.Options;
                return new IncidentFixture(database, options, new CountingFactory(options), saves, transactions, queries);
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public async Task<List<ServerIncidentGroup>> LoadAsync()
        {
            await using var db = new ApiDbContext(_database.Options());
            return await db.IncidentGroups.AsNoTracking().ToListAsync();
        }

        public async Task<ServerIncidentGroup> WithRetryingScopeAsync(Func<Task<ServerIncidentGroup>> operation)
        {
            await using var db = new ApiDbContext(_actionOptions);
            var strategy = db.Database.CreateExecutionStrategy();
            Assert.True(strategy.RetriesOnFailure);
            return await strategy.ExecuteAsync(operation);
        }

        public async Task<ServerIncidentGroup> WithSingleAttemptScopeAsync(Func<Task<ServerIncidentGroup>> operation)
        {
            await using var db = new ApiDbContext(_actionOptions);
            return await new SingleAttempt(db).ExecuteAsync(operation);
        }

        public void AssertAttempts(int contexts, int starts, int queries, int saves, int commits, int completedCommits)
        {
            Assert.Equal(contexts, _factory.Created);
            Assert.Equal(starts, Transactions.Starts);
            Assert.Equal(queries, _queries.Reads);
            Assert.Equal(saves, Saves.Attempts);
            Assert.Equal(commits, Transactions.CommitAttempts);
            Assert.Equal(completedCommits, Transactions.CompletedCommits);
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }

    private sealed class CountingFactory(DbContextOptions<ApiDbContext> options) : IDbContextFactory<ApiDbContext>
    {
        public int Created { get; private set; }
        public ApiDbContext CreateDbContext() { Created++; return new ApiDbContext(options); }
        public ValueTask<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }

    private sealed class SaveProbe : SaveChangesInterceptor
    {
        public int Attempts { get; private set; }
        public int CompletedSaves { get; private set; }
        public int LastSavedRows { get; private set; }
        public Action<CancellationToken>? AfterSaveOnce { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            CompletedSaves++;
            LastSavedRows = result;
            var action = AfterSaveOnce;
            AfterSaveOnce = null;
            action?.Invoke(cancellationToken);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class QueryProbe : DbCommandInterceptor
    {
        public int Reads { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("\"IncidentGroups\"", StringComparison.Ordinal))
                Reads++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TransactionProbe : DbTransactionInterceptor
    {
        public int Starts { get; private set; }
        public int CommitAttempts { get; private set; }
        public int CompletedCommits { get; private set; }
        public Action? AfterCommitOnce { get; set; }
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            Starts++;
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            CommitAttempts++;
            return ValueTask.FromResult(result);
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            CompletedCommits++;
            var action = AfterCommitOnce;
            AfterCommitOnce = null;
            action?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class SingleAttempt(ApiDbContext context) : ExecutionStrategy(context, 0, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    private sealed class SyntheticRetriableFailure() : Exception("Synthetic incident persistence failure.") { }

    private sealed class SyntheticRetryingStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new Strategy(dependencies);
        private sealed class Strategy(ExecutionStrategyDependencies dependencies) : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
        {
            protected override bool ShouldRetryOn(Exception exception) => exception is SyntheticRetriableFailure;
        }
    }
}
