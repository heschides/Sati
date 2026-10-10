using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Microsoft.Data.SqlClient;
using System.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Persistence.Migrations;

namespace Sati.Api.Tests;

public sealed partial class ClearinghouseDispatchApiTests
{
    [Fact]
    public async Task FairnessStaleCandidateSpendsOfferAndResetMissDoesNot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pair = await QueueFairnessPairAsync(fixture);
        var connector = new CountedOutcomeConnector(ClearinghouseAttemptOutcome.Accepted);
        var resetMiss = fixture.Worker(connector, new FairnessAdmissionChange(fixture.Factory, pair.FirstDispatch, resetMiss: true));
        Assert.Equal(DispatchTurnKind.Contended, (await resetMiss.ProcessTurnAsync(CancellationToken.None)).Kind);
        await using (var db = fixture.Factory.OpenDatabase())
        { Assert.Equal(1, (await db.ClearinghouseDispatchRotation.SingleAsync()).Revision); Assert.Empty(await db.ClearinghouseAgencyDispatchRotation.ToListAsync()); }
        var worker = fixture.Worker(connector, new FairnessAdmissionChange(fixture.Factory, pair.FirstDispatch, resetMiss: false));
        Assert.Equal(DispatchTurnKind.LaneSkipped, (await worker.ProcessTurnAsync(CancellationToken.None)).Kind);
        Assert.Equal(DispatchTurnKind.Processed, (await worker.ProcessTurnAsync(CancellationToken.None)).Kind);
        Assert.Equal(1, connector.Calls);
        await using var verify = fixture.Factory.OpenDatabase();
        Assert.Equal(pair.OtherDispatch, (await verify.ClearinghouseDispatchAttempts.SingleAsync()).DispatchId);
        Assert.Empty(await verify.ClearinghouseDispatchReadiness.ToListAsync());
    }

    private sealed class FairnessAdmissionChange(SyntheticPipelineFactory factory, Guid stale, bool resetMiss) : IDemoWorkerResetCoordination
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, T unavailableResult, CancellationToken token) =>
            resetMiss ? Task.FromResult(unavailableResult) : operation(token);
        public Task<T> RunAccountPreflightAsync<T>(int agencyId, Guid accountId,
            Func<IAccountPreflightLease, CancellationToken, Task<T>> operation, T unavailableResult, CancellationToken token) =>
            operation(new UncoordinatedAccountPreflightLease(), token);
        public async Task<T> RunDispatchAsync<T>(Guid id, Func<CancellationToken, Task<T>> operation, T unavailableResult, CancellationToken token)
        {
            if (id == stale)
            {
                await using var db = factory.OpenDatabase();
                var row = await db.ClearinghouseDispatches.SingleAsync(x => x.Id == stale, token);
                row.State = ClearinghouseDispatchState.CancelledBeforeSend; row.Revision++; await db.SaveChangesAsync(token);
            }
            return await operation(token);
        }
    }

    [Fact]
    public async Task FairnessCursorRejectsDeletionMissingRevisionAndConcurrentOverwrite()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var invalid = fixture.Factory.OpenDatabase())
        {
            var row = await invalid.ClearinghouseDispatchRotation.SingleAsync();
            row.LastAgencyId = fixture.Actors.AgencyId;
            await Assert.ThrowsAsync<InvalidOperationException>(() => invalid.SaveChangesAsync());
        }
        await using (var invalid = fixture.Factory.OpenDatabase())
        {
            invalid.ClearinghouseDispatchRotation.Remove(await invalid.ClearinghouseDispatchRotation.SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => invalid.SaveChangesAsync());
        }
        await using var first = fixture.Factory.OpenDatabase(); await using var stale = fixture.Factory.OpenDatabase();
        var left = await first.ClearinghouseDispatchRotation.SingleAsync(); var right = await stale.ClearinghouseDispatchRotation.SingleAsync();
        left.LastAgencyId = fixture.Actors.AgencyId; left.Revision++; await first.SaveChangesAsync();
        right.LastAgencyId = fixture.Actors.AgencyId; right.Revision++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
    }

    [Fact]
    public void FairnessBothModelsHaveSeparateConcurrencyAndPreservedIndexes()
    {
        using var api = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer("Server=unused;Database=SatiSyntheticDesignOnly;Integrated Security=true").Options);
        using var local = new Sati.Data.SatiContext(new DbContextOptionsBuilder<Sati.Data.SatiContext>().UseSqlServer("Server=unused;Database=SatiSyntheticDesignOnly;Integrated Security=true").Options);
        foreach (var model in new[] { api.Model, local.Model })
        {
            var global = model.FindEntityType(typeof(ClearinghouseDispatchRotation))!;
            var agency = model.FindEntityType(typeof(ClearinghouseAgencyDispatchRotation))!;
            Assert.True(global.FindProperty("Revision")!.IsConcurrencyToken);
            Assert.True(agency.FindProperty("Revision")!.IsConcurrencyToken);
            Assert.Single(global.FindPrimaryKey()!.Properties); Assert.Single(agency.FindPrimaryKey()!.Properties);
            Assert.Equal(DeleteBehavior.Restrict, Assert.Single(agency.GetForeignKeys()).DeleteBehavior);
            var indexes = model.FindEntityType(typeof(ClearinghouseDispatch))!.GetIndexes().ToArray();
            Assert.Contains(indexes, index => index.Properties.Select(x => x.Name).SequenceEqual(new[] { "AgencyId", "AccountId" }) && index.GetFilter() is null);
            var queued = Assert.Single(indexes, index => index.GetDatabaseName() == "IX_ClearinghouseDispatches_QueuedLane");
            Assert.Equal(new[] { "AgencyId", "AccountId", "RequestedAtUtc", "Id" }, queued.Properties.Select(x => x.Name));
            Assert.Equal("[State] = 1", queued.GetFilter());
        }
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlFairnessAdditiveMigrationAndRollbackGuardsPreserveEvidence()
    {
        await using var fixture = await Fixture.CreateAsync(sqlServer: true);
        var pair = await QueueFairnessPairAsync(fixture);
        await using var db = fixture.Factory.OpenDatabase();
        var bytes = await db.EdiGenerations.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Content).ToListAsync();
        var migration = new AddClearinghouseDispatchRotation { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
        async Task Apply(IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations)
        { foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model)) await db.Database.ExecuteSqlRawAsync(command.CommandText); }
        Assert.DoesNotContain(migration.UpOperations, x => x is Microsoft.EntityFrameworkCore.Migrations.Operations.DropIndexOperation);
        await Apply(migration.DownOperations); await Apply(migration.UpOperations);
        Assert.Equal(1, (await db.ClearinghouseDispatchRotation.SingleAsync()).Revision);
        var badId = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("INSERT dbo.ClearinghouseDispatchRotation (Id,LastAgencyId,Revision) VALUES (2,NULL,1)"));
        Assert.Equal(547, badId.Number);
        var badAgency = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("INSERT dbo.ClearinghouseAgencyDispatchRotation (AgencyId,LastAccountId,Revision) VALUES (2147483647,NULL,1)"));
        Assert.Equal(547, badAgency.Number);
        await fixture.Factory.Services.GetRequiredService<ClearinghouseDispatchSelector>().SelectAsync(CancellationToken.None);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => Apply(migration.DownOperations)); Assert.Equal(51045, rollback.Number);
        Assert.Equal(bytes, await db.EdiGenerations.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Content).ToListAsync());
        Assert.Equal(2, (await db.ClearinghouseDispatchRotation.AsNoTracking().SingleAsync()).Revision);
        Assert.All(await db.ClearinghouseDispatches.ToListAsync(), x => Assert.Equal(ClearinghouseDispatchState.Queued, x.State));
        Assert.Empty(await db.ClearinghouseDispatchAttempts.ToListAsync());
    }
    [Fact]
    public Task FairnessHealthyAgencyProgressesBeforeFiniteOlderBacklogDrains() => VerifyFiniteBacklogAsync(false);

    [SqlServerFact]
    public Task ClaimReleaseSqlFairnessHealthyAgencyProgressesBeforeFiniteOlderBacklogDrains() => VerifyFiniteBacklogAsync(true);

    private static async Task VerifyFiniteBacklogAsync(bool sqlServer)
    {
        await using var fixture = await Fixture.CreateAsync(sqlServer: sqlServer);
        var pair = await QueueFairnessPairAsync(fixture, async () =>
        {
            await QueueAdditionalFairnessPeriodAsync(fixture, fixture.AccountId);
            await QueueAdditionalFairnessPeriodAsync(fixture, fixture.AccountId, 2);
        });
        var connector = new CountedOutcomeConnector(ClearinghouseAttemptOutcome.Accepted);
        var worker = fixture.Worker(connector, sqlServer ? SqlPreflightCoordination(fixture.Factory) : null);
        Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
        await using (var firstTurn = fixture.Factory.OpenDatabase())
            Assert.Equal(pair.FirstDispatch, (await firstTurn.ClearinghouseDispatchAttempts.SingleAsync()).DispatchId);
        Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
        await using (var db = fixture.Factory.OpenDatabase())
        {
            Assert.Equal(ClearinghouseDispatchState.AcceptedByClearinghouse,
                (await db.ClearinghouseDispatches.SingleAsync(x => x.Id == pair.OtherDispatch)).State);
            Assert.Equal(2, await db.ClearinghouseDispatches.CountAsync(x => x.AgencyId == fixture.Actors.AgencyId && x.State == ClearinghouseDispatchState.Queued));
        }
        Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
        Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
        Assert.False(await worker.ProcessOneAsync(CancellationToken.None));
        Assert.Equal(4, connector.Calls);
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlFairnessLostSelectorLeaseRollsBackBeforeAnyUpload()
    {
        var loss = new LoseOfferLease();
        await using var fixture = await Fixture.CreateAsync(sqlServer: true, interceptor: loss);
        var pair = await QueueFairnessPairAsync(fixture);
        var connector = new CountedOutcomeConnector(ClearinghouseAttemptOutcome.Accepted);
        loss.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Worker(connector).ProcessOneAsync(CancellationToken.None));
        Assert.True(loss.Fired); Assert.Equal(0, connector.Calls);
        await using var verify = fixture.Factory.OpenDatabase();
        Assert.Equal(1, (await verify.ClearinghouseDispatchRotation.SingleAsync()).Revision);
        Assert.Empty(await verify.ClearinghouseAgencyDispatchRotation.ToListAsync());
        Assert.Empty(await verify.ClearinghouseDispatchAttempts.ToListAsync());
        Assert.All(await verify.ClearinghouseDispatches.ToListAsync(), x => Assert.Equal(ClearinghouseDispatchState.Queued, x.State));
        Assert.Equal(pair.FirstDispatch, (await fixture.Factory.Services.GetRequiredService<ClearinghouseDispatchSelector>().SelectAsync(CancellationToken.None)).DispatchId);
    }

    private sealed class LoseOfferLease : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool Fired { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<ClearinghouseDispatchRotation>().Any())
            {
                Armed = false; Fired = true;
                await eventData.Context.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource = {ClearinghouseDispatchSelector.LockResource}, @LockOwner = 'Transaction';", cancellationToken);
            }
            return result;
        }
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlFairnessBusyLeaseSpendsOnlyItsOffer()
    {
        foreach (var busy in new[] { "dispatch", "account" })
        {
            await using var fixture = await Fixture.CreateAsync(sqlServer: true);
            var pair = await QueueFairnessPairAsync(fixture);
            await using var held = fixture.Factory.OpenDatabase();
            await held.Database.OpenConnectionAsync();
            var resource = busy == "dispatch" ? "Sati.ClearinghouseDispatch:" + pair.FirstDispatch.ToString("N") :
                $"Sati.ClearinghousePreflight:{fixture.Actors.AgencyId}:{fixture.AccountId:N}";
            await held.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;");
            try
            {
                var connector = new CountedOutcomeConnector(ClearinghouseAttemptOutcome.Accepted);
                var worker = fixture.Worker(connector, SqlPreflightCoordination(fixture.Factory));
                Assert.Equal(DispatchTurnKind.LaneSkipped, (await worker.ProcessTurnAsync(CancellationToken.None)).Kind);
                Assert.Equal(DispatchTurnKind.Processed, (await worker.ProcessTurnAsync(CancellationToken.None)).Kind);
                Assert.Equal(1, connector.Calls);
                await using var verify = fixture.Factory.OpenDatabase();
                Assert.Equal(pair.OtherDispatch, (await verify.ClearinghouseDispatchAttempts.SingleAsync()).DispatchId);
                Assert.Empty(await verify.ClearinghouseDispatchReadiness.ToListAsync());
                Assert.Equal(3, (await verify.ClearinghouseDispatchRotation.SingleAsync()).Revision);
                Assert.Equal(ClearinghouseDispatchState.Queued,
                    (await verify.ClearinghouseDispatches.SingleAsync(x => x.Id == pair.FirstDispatch)).State);
            }
            finally { await held.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource = {resource}, @LockOwner = 'Session';"); }
        }
    }

    [Fact]
    public Task FairnessQueryShapeAndFiniteLaneChangesRemainBounded() => VerifyQueryShapeAsync(false);

    [SqlServerFact]
    public Task ClaimReleaseSqlFairnessQueryShapeAndFiniteLaneChangesRemainBounded() => VerifyQueryShapeAsync(true);

    private static async Task VerifyQueryShapeAsync(bool sqlServer)
    {
        var probe = new SelectionCommands();
        await using var fixture = await Fixture.CreateAsync(sqlServer: sqlServer, interceptor: probe);
        var pair = await QueueFairnessPairAsync(fixture);
        var selector = fixture.Factory.Services.GetRequiredService<ClearinghouseDispatchSelector>();
        for (var i = 0; i < 5; i++)
        {
            probe.Armed = true; probe.Reads.Clear(); probe.Writes = 0;
            var selection = await selector.SelectAsync(CancellationToken.None); probe.Armed = false;
            Assert.Equal(DispatchSelectionKind.Selected, selection.Kind);
            Assert.InRange(probe.Reads.Count, 5, 7);
            Assert.All(probe.Reads, sql => Assert.Contains(sqlServer ? "TOP(" : "LIMIT", sql));
            Assert.InRange(probe.Writes, 1, 2); // SQL may batch the two guarded cursor writes in one command.
        }
        // Remove a lane from eligibility while its pivot is retained; wrap safely over that pivot.
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var first = await db.ClearinghouseDispatches.SingleAsync(x => x.Id == pair.FirstDispatch);
            first.State = ClearinghouseDispatchState.CancelledBeforeSend; first.Revision++;
            await db.SaveChangesAsync();
        }
        Assert.Equal(pair.OtherDispatch, (await selector.SelectAsync(CancellationToken.None)).DispatchId);
        await using (var db = fixture.Factory.OpenDatabase())
        {
            var other = await db.ClearinghouseDispatches.SingleAsync(x => x.Id == pair.OtherDispatch);
            other.State = ClearinghouseDispatchState.CancelledBeforeSend; other.Revision++; await db.SaveChangesAsync();
        }
        await using var before = fixture.Factory.OpenDatabase();
        var revision = (await before.ClearinghouseDispatchRotation.SingleAsync()).Revision;
        probe.Armed = true; probe.Reads.Clear(); probe.Writes = 0;
        Assert.Equal(DispatchSelectionKind.Empty, (await selector.SelectAsync(CancellationToken.None)).Kind);
        probe.Armed = false; Assert.InRange(probe.Reads.Count, 3, 4); Assert.Equal(0, probe.Writes);
        await using var after = fixture.Factory.OpenDatabase();
        Assert.Equal(revision, (await after.ClearinghouseDispatchRotation.SingleAsync()).Revision);
        Assert.Equal(2, await after.ClearinghouseAgencyDispatchRotation.CountAsync());
    }

    private sealed class SelectionCommands : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public List<string> Reads { get; } = [];
        public int Writes { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) Reads.Add(command.CommandText);
                else Writes++;
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (Armed) Writes++; return ValueTask.FromResult(result); }
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlFairnessZeroWaitSerializationAndUploadReleaseAcrossHosts()
    {
        await using var fixture = await Fixture.CreateAsync(sqlServer: true);
        var pair = await QueueFairnessPairAsync(fixture);
        await using var second = new SyntheticPipelineFactory(fixture.Database, fixture.Factory.Vault)
            { EnableSyntheticDispatch = true, DisableDispatchWorker = true };
        var other = second.Services.GetRequiredService<ClearinghouseDispatchSelector>();
        await using (var held = fixture.Factory.OpenDatabase())
        {
            await held.Database.OpenConnectionAsync();
            await using var transaction = await held.Database.BeginTransactionAsync();
            await held.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_getapplock @Resource = {ClearinghouseDispatchSelector.LockResource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;");
            Assert.Equal(DispatchSelectionKind.Contended, (await other.SelectAsync(CancellationToken.None)).Kind);
            await using var verify = second.OpenDatabase();
            Assert.Equal(1, (await verify.ClearinghouseDispatchRotation.SingleAsync()).Revision);
            Assert.Empty(await verify.ClearinghouseAgencyDispatchRotation.ToListAsync());
        }
        var contexts = new SelectionContextProbe(fixture.Database);
        var selector = new ClearinghouseDispatchSelector(contexts,
            fixture.Factory.Services.GetRequiredService<ClearinghouseDispatchGate>(), TimeProvider.System);
        var offers = await Task.WhenAll(selector.SelectAsync(CancellationToken.None), other.SelectAsync(CancellationToken.None));
        var selected = offers.Where(x => x.Kind == DispatchSelectionKind.Selected).ToArray();
        Assert.NotEmpty(selected);
        Assert.All(offers, x => Assert.True(x.Kind is DispatchSelectionKind.Selected or DispatchSelectionKind.Contended));
        Assert.Equal(selected.Length, selected.Select(x => x.AgencyId).Distinct().Count());
        Assert.Equal(ConnectionState.Closed, contexts.Connection!.State);
        // Physical upload deliberately remains blocked while the other host can commit another offer.
        var blocked = new FairnessBlockedConnector();
        var working = fixture.Worker(blocked, SqlPreflightCoordination(fixture.Factory)).ProcessOneAsync(CancellationToken.None);
        await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            var following = await other.SelectAsync(CancellationToken.None);
            Assert.Equal(DispatchSelectionKind.Selected, following.Kind);
            Assert.NotEqual(blocked.DispatchId, following.DispatchId);
            Assert.True(following.DispatchId == pair.FirstDispatch || following.DispatchId == pair.OtherDispatch);
        }
        finally { blocked.Release.TrySetResult(); }
        Assert.True(await working.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    private sealed class SelectionContextProbe(SyntheticPipelineDatabase database) : IDbContextFactory<ApiDbContext>
    {
        public DbConnection? Connection { get; private set; }
        public ApiDbContext CreateDbContext()
        { var db = new ApiDbContext(database.Options()); Connection = db.Database.GetDbConnection(); return db; }
    }

    private sealed class FairnessBlockedConnector : IClearinghouseConnector
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid DispatchId { get; private set; }
        public async Task<ClearinghouseUploadResult> UploadAsync(ClearinghouseUpload upload, CancellationToken token)
        {
            DispatchId = upload.DispatchId; Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            return await new SyntheticClearinghouseConnector().UploadAsync(upload, token);
        }
    }

    [Fact]
    public Task FairnessPersistentOffersRotateAgenciesAndAccountsWithoutBusinessWrites() => VerifyFairnessRotationAsync(false);

    [SqlServerFact]
    public Task ClaimReleaseSqlFairnessPersistentOffersRotateAgenciesAndAccountsWithoutBusinessWrites() => VerifyFairnessRotationAsync(true);

    private static async Task VerifyFairnessRotationAsync(bool sqlServer)
    {
        await using var fixture = await Fixture.CreateAsync(sqlServer: sqlServer);
        var pair = await QueueFairnessPairAsync(fixture);
        var secondAccount = await fixture.AddAccountAsync("FAIRALLY", "", partner: TradingPartnerKind.OfficeAlly);
        var extra = await QueueAdditionalFairnessPeriodAsync(fixture, secondAccount);
        await using var before = fixture.Factory.OpenDatabase();
        var accountsBefore = await before.ClearinghouseAccounts.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var intentsBefore = await before.ClearinghouseDispatches.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var counts = await ReleaseCountsAsync(before);
        var firstHost = fixture.Factory.Services.GetRequiredService<ClearinghouseDispatchSelector>();
        var offers = new List<DispatchSelection>();
        offers.Add(await firstHost.SelectAsync(CancellationToken.None));
        // Recreated host must inherit scheduling position, rather than reset to the oldest agency.
        await using var restarted = new SyntheticPipelineFactory(fixture.Database, fixture.Factory.Vault)
            { EnableSyntheticDispatch = true, DisableDispatchWorker = true };
        var selector = restarted.Services.GetRequiredService<ClearinghouseDispatchSelector>();
        for (var i = 0; i < 7; i++) offers.Add(await selector.SelectAsync(CancellationToken.None));
        Assert.All(offers, offer => Assert.Equal(DispatchSelectionKind.Selected, offer.Kind));
        Assert.Equal(Enumerable.Range(0, 8).Select(i => i % 2 == 0 ? fixture.Actors.AgencyId : pair.OtherAgency), offers.Select(x => x.AgencyId));
        var a = offers.Where(x => x.AgencyId == fixture.Actors.AgencyId).ToArray();
        Assert.Equal(2, a.Select(x => x.AccountId).Distinct().Count());
        Assert.NotEqual(a[0].AccountId, a[1].AccountId);
        Assert.Equal(a[0].AccountId, a[2].AccountId);
        Assert.Equal(a[1].AccountId, a[3].AccountId);
        Assert.All(a, offer => Assert.Equal(offer.AccountId == fixture.AccountId ? pair.FirstDispatch : extra, offer.DispatchId));
        await using var after = restarted.OpenDatabase();
        Assert.Equal(counts, await ReleaseCountsAsync(after));
        Assert.Equal(accountsBefore.Select(x => (x.Id, x.Revision)),
            (await after.ClearinghouseAccounts.OrderBy(x => x.Id).ToListAsync()).Select(x => (x.Id, x.Revision)));
        Assert.Equal(intentsBefore.Select(x => (x.Id, x.State, x.Revision)),
            (await after.ClearinghouseDispatches.OrderBy(x => x.Id).ToListAsync()).Select(x => (x.Id, x.State, x.Revision)));
        Assert.Empty(await after.ClearinghouseDispatchReadiness.ToListAsync());
        Assert.Equal(2, await after.ClearinghouseAgencyDispatchRotation.CountAsync());
        Assert.Equal(9, (await after.ClearinghouseDispatchRotation.SingleAsync()).Revision);
        Assert.All(await after.ClearinghouseAgencyDispatchRotation.ToListAsync(), row => Assert.Equal(4, row.Revision));
    }

    private static async Task<Guid> QueueAdditionalFairnessPeriodAsync(Fixture fixture, Guid account, int monthsAgo = 1)
    {
        // Independent claims in an earlier period, admitted by the real API; no duplicated originals.
        using var author = await fixture.Factory.SignInAsync("synthetic-author");
        using var supervisor = await fixture.Factory.SignInAsync("synthetic-supervisor");
        var request = new SaveNoteRequest("Synthetic fairness period contact.", DateTime.Today.AddMonths(-monthsAgo),
            "Pending", 60, 300, fixture.Actors.FirstPersonId, null, "Contact", null, null);
        using var created = await author.PostAsJsonAsync("/api/v1/notes", request); created.EnsureSuccessStatusCode();
        var draft = (await created.Content.ReadFromJsonAsync<NoteDto>())!;
        using var submitted = await author.PutAsJsonAsync($"/api/v1/notes/{draft.Id}",
            request with { Status = "Logged", ExpectedRevision = draft.Revision, GoalProgress = "Moderate" });
        submitted.EnsureSuccessStatusCode();
        var logged = (await submitted.Content.ReadFromJsonAsync<NoteDto>())!;
        using var approved = await supervisor.PostAsJsonAsync($"/api/v1/supervisor/notes/{draft.Id}/approve",
            new SupervisorNoteActionRequest(null, logged.Revision)); approved.EnsureSuccessStatusCode();
        using var claimed = await fixture.Biller.PostAsJsonAsync("/api/v1/billing/claim-lines", new CreateClaimLineRequest(draft.Id, false, null));
        claimed.EnsureSuccessStatusCode();
        var line = (await claimed.Content.ReadFromJsonAsync<ClaimLineDto>())!;
        Assert.NotEqual(fixture.PeriodId, line.BillingPeriodId);
        using var locked = await fixture.Biller.PostAsync($"/api/v1/billing/periods/{line.BillingPeriodId}/submit", null); locked.EnsureSuccessStatusCode();
        using var generated = await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{line.BillingPeriodId}/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")) { ClearinghouseAccountId = account }); generated.EnsureSuccessStatusCode();
        await using var db = fixture.Factory.OpenDatabase();
        var generation = await db.EdiGenerations.Where(x => x.BillingPeriodId == line.BillingPeriodId).Select(x => x.Id).SingleAsync();
        using var queued = await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches", new QueueClearinghouseDispatchRequest(generation, account));
        queued.EnsureSuccessStatusCode();
        return (await queued.Content.ReadFromJsonAsync<ClearinghouseDispatchDto>())!.Id;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FairnessFailedOrAmbiguousOfferNeverUploadsOrRetries(bool afterCommit) => VerifyOfferFaultAsync(false, afterCommit);

    [SqlServerFact]
    public async Task ClaimReleaseSqlFairnessFailedOrAmbiguousOfferNeverUploadsOrRetries()
    { await VerifyOfferFaultAsync(true, false); await VerifyOfferFaultAsync(true, true); }

    private static async Task VerifyOfferFaultAsync(bool sqlServer, bool afterCommit)
    {
        var fault = new OfferCommitFault(afterCommit);
        await using var fixture = await Fixture.CreateAsync(sqlServer: sqlServer, interceptor: fault);
        var pair = await QueueFairnessPairAsync(fixture);
        var connector = new CountedOutcomeConnector(ClearinghouseAttemptOutcome.Accepted);
        fault.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Worker(connector).ProcessOneAsync(CancellationToken.None));
        Assert.Equal(1, fault.Fired); Assert.Equal(0, connector.Calls);
        await using var db = fixture.Factory.OpenDatabase();
        var cursor = await db.ClearinghouseDispatchRotation.SingleAsync();
        Assert.Equal(afterCommit ? 2 : 1, cursor.Revision);
        Assert.Equal(afterCommit ? fixture.Actors.AgencyId : (int?)null, cursor.LastAgencyId);
        Assert.Equal(afterCommit ? 1 : 0, await db.ClearinghouseAgencyDispatchRotation.CountAsync());
        Assert.All(await db.ClearinghouseDispatches.ToListAsync(), row => Assert.Equal(ClearinghouseDispatchState.Queued, row.State));
        Assert.Empty(await db.ClearinghouseDispatchAttempts.ToListAsync());
        var recovered = await fixture.Factory.Services.GetRequiredService<ClearinghouseDispatchSelector>().SelectAsync(CancellationToken.None);
        Assert.Equal(afterCommit ? pair.OtherDispatch : pair.FirstDispatch, recovered.DispatchId);
    }

    private sealed class OfferCommitFault(bool afterCommit) : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public int Fired { get; private set; }
        private void Fail(DbContext? context, bool committed)
        {
            if (!Armed || afterCommit != committed || context?.ChangeTracker.Entries<ClearinghouseDispatchRotation>().Any() != true) return;
            Armed = false; Fired++;
            throw new InvalidOperationException("Synthetic scheduling commit acknowledgement failure.");
        }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        { Fail(eventData.Context, false); return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        { Fail(eventData.Context, true); return Task.CompletedTask; }
    }

    [Fact]
    public async Task FairnessEmptyAndMissingStateFailClosedWithoutBusinessWrites()
    {
        await using var fixture = await Fixture.CreateAsync();
        var selector = fixture.Factory.Services.GetRequiredService<ClearinghouseDispatchSelector>();
        Assert.Equal(DispatchSelectionKind.Empty, (await selector.SelectAsync(CancellationToken.None)).Kind);
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Equal(1, (await db.ClearinghouseDispatchRotation.SingleAsync()).Revision);
        Assert.Empty(await db.ClearinghouseAgencyDispatchRotation.ToListAsync());
        await QueueFairnessPairAsync(fixture);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ClearinghouseDispatchRotation");
        var connector = new CountedOutcomeConnector(ClearinghouseAttemptOutcome.Accepted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Worker(connector).ProcessOneAsync(CancellationToken.None));
        Assert.Equal(0, connector.Calls);
        Assert.All(await db.ClearinghouseDispatches.ToListAsync(), row => Assert.Equal(ClearinghouseDispatchState.Queued, row.State));
    }
    [Theory]
    [InlineData("dispatch")]
    [InlineData("account")]
    public async Task FairnessBusyOldestAgencyAllowsHealthyAgencyOnFollowingTurn(string busy)
    {
        await using var fixture = await Fixture.CreateAsync();
        var other = await QueueFairnessPairAsync(fixture);
        var leases = new BusyFairnessLane(fixture.Actors.AgencyId, fixture.AccountId, other.FirstDispatch, busy);
        var connector = new CountedOutcomeConnector(ClearinghouseAttemptOutcome.Accepted);
        var worker = fixture.Worker(connector, leases);
        Assert.False(await worker.ProcessOneAsync(CancellationToken.None));
        Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Equal(ClearinghouseDispatchState.Queued,
            (await db.ClearinghouseDispatches.SingleAsync(x => x.Id == other.FirstDispatch)).State);
        Assert.Equal(ClearinghouseDispatchState.AcceptedByClearinghouse,
            (await db.ClearinghouseDispatches.SingleAsync(x => x.Id == other.OtherDispatch)).State);
        Assert.Equal(1, connector.Calls);
        Assert.Empty(await db.ClearinghouseDispatchReadiness.ToListAsync());
        Assert.Equal(other.OtherDispatch, (await db.ClearinghouseDispatchAttempts.SingleAsync()).DispatchId);
    }

    private sealed record FairnessPair(Guid FirstDispatch, Guid OtherDispatch, int OtherAgency, Guid OtherAccount);

    private static async Task<FairnessPair> QueueFairnessPairAsync(Fixture fixture, Func<Task>? beforeOther = null)
    {
        await fixture.GenerateAsync(fixture.AccountId);
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).EnsureSuccessStatusCode();
        if (beforeOther is not null) await beforeOther();
        const string prefix = "fairness-b-";
        var actors = await fixture.Factory.SeedAsync(prefix);
        var period = await JoinedBillingPipelineAcceptanceTests.PrepareSubmittedPeriodAsync(fixture.Factory, actors, prefix);
        var account = new ClearinghouseAccount
        {
            Id = Guid.NewGuid(), AgencyId = actors.AgencyId, ConnectorKind = TradingPartnerKind.ClaimMd,
            IsTest = true, IsEnabled = true, ExternalAccountNumber = "FAIRNESSB", ClaimNamespace = "FAIRB",
            TradingPartnerProfileVersion = TradingPartnerProfile.CurrentVersion,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        await using (var db = fixture.Factory.OpenDatabase())
        { db.ClearinghouseAccounts.Add(account); await db.SaveChangesAsync(); }
        using var biller = await fixture.Factory.SignInAsync(prefix + "synthetic-biller");
        using var generated = await biller.PostAsJsonAsync($"/api/v1/billing/periods/{period}/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")) { ClearinghouseAccountId = account.Id });
        generated.EnsureSuccessStatusCode();
        long generationId;
        await using (var db = fixture.Factory.OpenDatabase())
            generationId = await db.EdiGenerations.Where(x => x.AgencyId == actors.AgencyId).Select(x => x.Id).SingleAsync();
        (await biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(generationId, account.Id))).EnsureSuccessStatusCode();
        await using var verify = fixture.Factory.OpenDatabase();
        var first = await verify.ClearinghouseDispatches.Where(x => x.AgencyId == fixture.Actors.AgencyId).OrderBy(x => x.RequestedAtUtc).ThenBy(x => x.Id).FirstAsync();
        var second = await verify.ClearinghouseDispatches.SingleAsync(x => x.AgencyId == actors.AgencyId);
        Assert.True(await verify.ClearinghouseDispatches.Where(x => x.AgencyId == fixture.Actors.AgencyId).AllAsync(x => x.RequestedAtUtc < second.RequestedAtUtc));
        return new(first.Id, second.Id, actors.AgencyId, account.Id);
    }

    private sealed class BusyFairnessLane(int agency, Guid account, Guid dispatch, string kind) : IDemoWorkerResetCoordination
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, T unavailableResult, CancellationToken token) => operation(token);
        public Task<T> RunDispatchAsync<T>(Guid id, Func<CancellationToken, Task<T>> operation, T unavailableResult, CancellationToken token) =>
            kind == "dispatch" && id == dispatch ? Task.FromResult(unavailableResult) : operation(token);
        public Task<T> RunAccountPreflightAsync<T>(int agencyId, Guid accountId,
            Func<IAccountPreflightLease, CancellationToken, Task<T>> operation, T unavailableResult, CancellationToken token) =>
            kind == "account" && agencyId == agency && accountId == account ? Task.FromResult(unavailableResult) :
                operation(new UncoordinatedAccountPreflightLease(), token);
    }
}
