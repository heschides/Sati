using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

[Collection("Synthetic pipeline SQL Server")]
public sealed partial class ClearinghouseDispatchApiTests
{
    [SqlServerFact]
    public async Task ClaimReleaseSqlCaseloadContentionReturnsASafeConflict()
    {
        await using var fixture = await Fixture.CreateAsync(sqlServer: true);
        await using var owner = fixture.Factory.OpenDatabase();
        HttpResponseMessage? response = null;
        await ClaimReleaseWriteScope.ExecuteOnceAsync(owner, async () =>
        {
            await using var held = await ClaimReleaseWriteScope.BeginAsync(owner, fixture.Actors.AgencyId);
            response = await fixture.Biller.GetAsync("/api/v1/caseload").WaitAsync(TimeSpan.FromSeconds(20));
        });
        Assert.False(await fixture.Database.HasGrantedClaimReleaseLockAsync());
        Assert.Equal(HttpStatusCode.Conflict, response!.StatusCode);
        Assert.Equal("claim_release_busy", (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Empty(await db.ClearinghouseDispatches.ToListAsync());
        Assert.Empty(await db.ClearinghouseDispatchAttempts.ToListAsync());
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlCancellationReleasesAdmissionAndAnotherAgencyCanProgress()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        await using var host = new SyntheticPipelineFactory(database);
        var actors = await host.SeedAsync();
        int otherAgency;
        await using (var seed = host.OpenDatabase())
        {
            var agency = new ServerAgency { Name = "Independent synthetic agency" };
            seed.Agencies.Add(agency); await seed.SaveChangesAsync(); otherAgency = agency.Id;
        }
        await using var owner = host.OpenDatabase();
        await ClaimReleaseWriteScope.ExecuteOnceAsync(owner, async () =>
        {
            await using var transaction = await ClaimReleaseWriteScope.BeginAsync(owner, actors.AgencyId);
            Assert.True(await database.HasGrantedClaimReleaseLockAsync());
            await using var waiter = host.OpenDatabase();
            using var cancellation = new CancellationTokenSource();
            var waiting = ClaimReleaseWriteScope.ExecuteOnceAsync(waiter, async () =>
            {
                await using var blocked = await ClaimReleaseWriteScope.BeginAsync(waiter, actors.AgencyId, cancellation.Token);
            });
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (!waiting.IsCompleted && !await database.HasWaitingApplicationLockAsync())
                    await Task.Delay(25, timeout.Token);
                Assert.False(waiting.IsCompleted, "The same agency bypassed admission.");
                await using var independent = host.OpenDatabase();
                await ClaimReleaseWriteScope.ExecuteOnceAsync(independent, async () =>
                {
                    await using var own = await ClaimReleaseWriteScope.BeginAsync(independent, otherAgency);
                    await own.CommitAsync();
                }).WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally { cancellation.Cancel(); }
            var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            Assert.Equal(cancellation.Token, cancelled.CancellationToken);
            // Dispose without commit models a cancelled/failed admitted decision.
        });
        Assert.False(await database.HasGrantedClaimReleaseLockAsync());
        await using var successor = host.OpenDatabase();
        await ClaimReleaseWriteScope.ExecuteOnceAsync(successor, async () =>
        {
            await using var own = await ClaimReleaseWriteScope.BeginAsync(successor, actors.AgencyId);
            await own.CommitAsync();
        }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(await database.HasGrantedClaimReleaseLockAsync());
    }

    [SqlServerTheory]
    [InlineData("generation")]
    [InlineData("queue")]
    [InlineData("worker")]
    [InlineData("mock")]
    [InlineData("manual")]
    [InlineData("status")]
    [InlineData("era")]
    [InlineData("reconcile")]
    [InlineData("correction")]
    public async Task ClaimReleaseSqlAllWritersOwnAdmission(string writer)
    {
        var observer = new AdmissionObserver();
        await using var fixture = await Fixture.CreateAsync(sqlServer: true, interceptor: observer);
        var original = await fixture.GenerateAsync(fixture.AccountId);
        var queue = new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId);
        var queuePath = "/api/v1/billing/clearinghouse/dispatches";
        if (writer is "worker" or "status" or "era" or "reconcile")
            (await fixture.Biller.PostAsJsonAsync(queuePath, queue)).EnsureSuccessStatusCode();
        if (writer is "status" or "era" or "reconcile")
            Assert.True(await fixture.Worker(new CountedOutcomeConnector(writer == "reconcile"
                ? ClearinghouseAttemptOutcome.OutcomeUnknown : ClearinghouseAttemptOutcome.Accepted)).ProcessOneAsync(CancellationToken.None));
        if (writer == "correction")
            (await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/mock-clearinghouse",
                new MockClearinghouseRequest(MockClearinghouseScenario.Accepted))).EnsureSuccessStatusCode();
        observer.Armed = true;
        switch (writer)
        {
            case "generation": await fixture.GenerateAsync(fixture.AccountId); break;
            case "queue": (await fixture.Biller.PostAsJsonAsync(queuePath, queue)).EnsureSuccessStatusCode(); break;
            case "worker": Assert.True(await fixture.Worker(new SyntheticClearinghouseConnector()).ProcessOneAsync(CancellationToken.None)); break;
            case "mock":
                (await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/mock-clearinghouse",
                    new MockClearinghouseRequest(MockClearinghouseScenario.SyntaxRejected))).EnsureSuccessStatusCode(); break;
            case "manual":
                var document = MockClearinghouse.Respond(original.Content, MockClearinghouseScenario.SyntaxRejected, DateTime.UtcNow);
                (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/responses",
                    new ClaimResponseIngestRequest(document.FunctionalAcknowledgement!))).EnsureSuccessStatusCode(); break;
            case "status":
            case "era":
                await using (var scope = fixture.Factory.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                    if (writer == "status")
                    {
                        var dispatch = await db.ClearinghouseDispatches.AsNoTracking().SingleAsync();
                        var parsed = ClaimResponseReader.ReadSubmission(original.Content);
                        var page = new ClaimMdStatusPage("1", parsed.Claims.Select((claim, index) =>
                            new ClaimMdStatusClaim((index + 1).ToString(), dispatch.ExternalFileId!, claim.ClaimReference,
                                claim.RemoteClaimId!, "A")).ToList(), "<result synthetic='true'/>");
                        Assert.True(await new ClaimMdStatusProcessor(db, scope.ServiceProvider.GetRequiredService<EnvelopeProtector>())
                            .ProcessAsync(fixture.AccountId, "0", page, CancellationToken.None));
                    }
                    else
                    {
                        var era = MockClearinghouse.Respond(original.Content, MockClearinghouseScenario.Accepted, DateTime.UtcNow).RemittanceAdvice!;
                        Assert.True(await scope.ServiceProvider.GetRequiredService<ClaimResponseIngestion>()
                            .ImportConnectorEraAsync(fixture.AccountId, "1", "0", era, CancellationToken.None));
                    }
                }
                break;
            case "reconcile":
                await using (var db = fixture.Factory.OpenDatabase())
                {
                    var dispatch = await db.ClearinghouseDispatches.AsNoTracking().SingleAsync();
                    var path = $"/api/v1/admin/clearinghouse/dispatches/{dispatch.Id}/reconciliation";
                    var manifest = (await fixture.Biller.GetFromJsonAsync<ClaimMdReconciliationManifestDto>(path))!;
                    (await fixture.Biller.PostAsJsonAsync(path, new ReconcileClaimMdDispatchRequest(manifest.Revision,
                        manifest.AccountId, "CLAIMMDTEST", manifest.EdiGenerationId, manifest.ContentSha256,
                        manifest.FileName, "ConfirmedNotReceived", "SupportCase", "CASE-SYNTHETIC", new string('B', 64),
                        DateTime.UtcNow, ClaimMdReconciliationRules.NotReceivedAttestation, null, null, null, null))).EnsureSuccessStatusCode();
                }
                break;
            case "correction":
                await using (var db = fixture.Factory.OpenDatabase())
                {
                    var line = await db.ClaimLines.FirstAsync(row => row.BillingPeriodId == fixture.PeriodId);
                    (await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/corrections",
                        new CreateClaimCorrectionRequest(line.Id, ClaimCorrectionAction.Void, "Synthetic withdrawal."))).EnsureSuccessStatusCode();
                    (await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/corrections/edi",
                        new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")) { ClearinghouseAccountId = fixture.AccountId })).EnsureSuccessStatusCode();
                }
                break;
        }
        Assert.NotEmpty(observer.Checks);
        Assert.All(observer.Checks, owned => Assert.True(owned, $"{writer} wrote authoritative history without common SQL admission."));
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlReceiptBeforeSendingHoldsTheSuccessorWithoutAnUpload()
    {
        var barrier = new ReceiptSaveBarrier();
        await using var fixture = await Fixture.CreateAsync(sqlServer: true, interceptor: barrier);
        var old = await fixture.GenerateAsync(fixture.AccountId);
        await fixture.GenerateAsync(fixture.AccountId);
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).EnsureSuccessStatusCode();
        barrier.Armed = true;
        var document = MockClearinghouse.Respond(old.Content, MockClearinghouseScenario.SyntaxRejected, DateTime.UtcNow);
        var receipt = fixture.Biller.PostAsJsonAsync("/api/v1/billing/responses", new ClaimResponseIngestRequest(document.FunctionalAcknowledgement!));
        Task<bool>? send = null;
        var connector = new CountedOutcomeConnector(ClearinghouseAttemptOutcome.Accepted);
        var decisionAttempted = false;
        var waiting = false;
        try
        {
            await barrier.Paused.Task.WaitAsync(TimeSpan.FromSeconds(15));
            send = fixture.Worker(connector).ProcessOneAsync(CancellationToken.None);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!send.IsCompleted && !barrier.DecisionAttempted.Task.IsCompleted &&
                !(waiting = await fixture.Database.HasWaitingApplicationLockAsync()))
                await Task.Delay(25, timeout.Token);
            decisionAttempted = barrier.DecisionAttempted.Task.IsCompleted;
        }
        finally { barrier.Release.TrySetResult(); }
        (await receipt.WaitAsync(TimeSpan.FromSeconds(30))).EnsureSuccessStatusCode();
        Assert.True(await send!.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.False(decisionAttempted, "The worker attempted an authoritative history read before the receipt committed.");
        Assert.True(waiting, "SQL did not confirm that the second writer waited at common admission.");
        Assert.Equal(0, connector.Calls);
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Single(await db.ClearinghouseResponseReceipts.ToListAsync());
        Assert.Empty(await db.ClearinghouseDispatchAttempts.ToListAsync());
        Assert.Equal(ClearinghouseDispatchState.CancelledBeforeSend, (await db.ClearinghouseDispatches.SingleAsync()).State);
    }

    [SqlServerFact]
    public async Task ClaimReleaseSqlSendingBeforeReceiptReleasesSqlDuringUploadAndRetainsBothFacts()
    {
        await using var fixture = await Fixture.CreateAsync(sqlServer: true);
        var old = await fixture.GenerateAsync(fixture.AccountId);
        await fixture.GenerateAsync(fixture.AccountId);
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).EnsureSuccessStatusCode();
        var connector = new PausedUploadConnector();
        var send = fixture.Worker(connector).ProcessOneAsync(CancellationToken.None);
        try
        {
            await connector.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(await fixture.Database.HasGrantedClaimReleaseLockAsync());
            var document = MockClearinghouse.Respond(old.Content, MockClearinghouseScenario.SyntaxRejected, DateTime.UtcNow);
            (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/responses", new ClaimResponseIngestRequest(document.FunctionalAcknowledgement!))
                .WaitAsync(TimeSpan.FromSeconds(15))).EnsureSuccessStatusCode();
            Assert.False(send.IsCompleted);
        }
        finally { connector.Release.TrySetResult(); }
        Assert.True(await send.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(1, connector.Calls);
        await using var db = fixture.Factory.OpenDatabase();
        Assert.Single(await db.ClearinghouseResponseReceipts.ToListAsync());
        Assert.Single(await db.ClearinghouseDispatchAttempts.ToListAsync());
        Assert.Equal(ClearinghouseDispatchState.AcceptedByClearinghouse, (await db.ClearinghouseDispatches.SingleAsync()).State);
        var fresh = await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")) { ClearinghouseAccountId = fixture.AccountId });
        Assert.Equal(HttpStatusCode.Conflict, fresh.StatusCode);
    }

    private sealed class AdmissionObserver : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public ConcurrentQueue<bool> Checks { get; } = new();
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ApiDbContext db) return result;
            var agency = db.ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified)
                .Select(entry => entry.Entity switch
                {
                    ServerEdiGeneration file => file.AgencyId,
                    ClearinghouseDispatch dispatch => dispatch.AgencyId,
                    ClearinghouseResponseReceipt receipt => receipt.AgencyId,
                    ServerBillingSubmissionEvent submission => submission.AgencyId,
                    ClaimCorrection correction => correction.AgencyId,
                    _ => 0
                }).FirstOrDefault(id => id > 0);
            if (agency == 0) return result;
            if (db.Database.CurrentTransaction is not { } transaction) { Checks.Enqueue(false); return result; }
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT APPLOCK_MODE('public', @resource, 'Transaction');";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@resource";
            parameter.DbType = DbType.String; parameter.Value = $"Sati:ClaimRelease:{agency}"; command.Parameters.Add(parameter);
            Checks.Enqueue((string?)await command.ExecuteScalarAsync(cancellationToken) == "Exclusive");
            return result;
        }
    }

    private sealed class ReceiptSaveBarrier : SaveChangesInterceptor, IDbCommandInterceptor
    {
        public bool Armed { get; set; }
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DecisionAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DbContext? _receiptContext;
        public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Paused.Task.IsCompleted && eventData.Context != _receiptContext &&
                eventData.Context?.Database.CurrentTransaction is not null && command.CommandText.Contains("FROM [BillingPeriods]", StringComparison.Ordinal))
                DecisionAttempted.TrySetResult();
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is ApiDbContext db &&
                db.ChangeTracker.Entries<ClearinghouseResponseReceipt>().Any(row => row.State == EntityState.Added))
            { _receiptContext = db; Paused.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); }
            return result;
        }
    }

    private sealed class PausedUploadConnector : IClearinghouseConnector
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ClearinghouseUploadResult> UploadAsync(ClearinghouseUpload upload, CancellationToken token)
        {
            Calls++; Started.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            return await new SyntheticClearinghouseConnector().UploadAsync(upload, token);
        }
    }
}

public sealed class SqlServerTheoryAttribute : TheoryAttribute
{
    public SqlServerTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("SATI_RUN_SQLSERVER_TESTS") != "1")
            Skip = "Run with scripts/Test-IsolatedLocalDb.ps1 in a private synthetic instance.";
    }
}
