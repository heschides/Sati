using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class ClearinghouseDispatchApiTests
{
    [SqlServerTheory]
    [InlineData("status")]
    [InlineData("era")]
    [InlineData("worker")]
    public async Task ClaimReleaseSqlRetryingOuterScopeIsRefusedBeforeAnyIo(string writer)
    {
        var reads = new PreparationReadCounter();
        var wrapper = new ObservingPreparationWrapper();
        await using var fixture = await Fixture.CreateAsync(sqlServer: true, interceptor: reads, keyWrapper: wrapper);
        var file = await fixture.GenerateAsync(fixture.AccountId);
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
            new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).EnsureSuccessStatusCode();
        if (writer != "worker") Assert.True(await fixture.Worker(new SyntheticClearinghouseConnector()).ProcessOneAsync(CancellationToken.None));
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var dispatch = await db.ClearinghouseDispatches.AsNoTracking().SingleAsync();
        var claims = ClaimResponseReader.ReadSubmission(file.Content).Claims;
        var page = new ClaimMdStatusPage("1", claims.Select((row, index) => new ClaimMdStatusClaim(
            (index + 1).ToString(), dispatch.ExternalFileId ?? "synthetic", row.ClaimReference, row.RemoteClaimId!, "A")).ToList(), "<result/>");
        var era = MockClearinghouse.Respond(file.Content, MockClearinghouseScenario.Accepted, DateTime.UtcNow).RemittanceAdvice!;
        var connector = new CountedOutcomeConnector(ClearinghouseAttemptOutcome.Accepted);
        var retrying = db.Database.CreateExecutionStrategy();
        Assert.True(retrying.RetriesOnFailure);
        reads.Armed = true;
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => retrying.ExecuteAsync(async () => writer switch
        {
            "status" => await new ClaimMdStatusProcessor(db, scope.ServiceProvider.GetRequiredService<EnvelopeProtector>())
                .ProcessAsync(fixture.AccountId, "0", page, CancellationToken.None),
            "era" => await scope.ServiceProvider.GetRequiredService<ClaimResponseIngestion>()
                .ImportConnectorEraAsync(fixture.AccountId, "1", "0", era, CancellationToken.None),
            _ => await fixture.Worker(connector).ProcessOneAsync(CancellationToken.None)
        }));
        Assert.Contains("retrying execution scope", failure.Message);
        Assert.Equal(0, reads.Calls);
        Assert.Equal(0, wrapper.Calls);
        Assert.Equal(0, connector.Calls);
    }

    [SqlServerTheory]
    [InlineData("manual")]
    [InlineData("status")]
    [InlineData("era")]
    public async Task ClaimReleaseSqlPreparationRechecksAuthorityOrCursorWithoutHoldingSql(string writer)
    {
        var wrapper = new ObservingPreparationWrapper();
        await using var fixture = await Fixture.CreateAsync(sqlServer: true, keyWrapper: wrapper);
        wrapper.Database = fixture.Database;
        var file = await fixture.GenerateAsync(fixture.AccountId);
        if (writer != "manual")
        {
            (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
                new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).EnsureSuccessStatusCode();
            Assert.True(await fixture.Worker(new SyntheticClearinghouseConnector()).ProcessOneAsync(CancellationToken.None));
        }
        wrapper.OnPreparation = async () =>
        {
            await using var db = fixture.Factory.OpenDatabase();
            await ClaimReleaseWriteScope.ExecuteOnceAsync(db, async () =>
            {
                await using var transaction = await ClaimReleaseWriteScope.BeginAsync(db, fixture.Actors.AgencyId);
                if (writer == "manual")
                {
                    var actor = await db.Users.SingleAsync(row => row.Id == fixture.Actors.BillerId);
                    actor.SecurityVersion++;
                }
                else db.ClearinghouseFeedCheckpoints.Add(new ClearinghouseFeedCheckpoint
                {
                    Id = Guid.NewGuid(), AgencyId = fixture.Actors.AgencyId, AccountId = fixture.AccountId,
                    FeedKind = writer == "status" ? ClearinghouseFeedKind.Status : ClearinghouseFeedKind.Era,
                    Cursor = "2", UpdatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync(); await transaction.CommitAsync();
            });
        };
        if (writer == "manual")
        {
            var doc = MockClearinghouse.Respond(file.Content, MockClearinghouseScenario.SyntaxRejected, DateTime.UtcNow);
            var response = await fixture.Biller.PostAsJsonAsync("/api/v1/billing/responses", new ClaimResponseIngestRequest(doc.FunctionalAcknowledgement!));
            Assert.False(wrapper.HeldDuringPreparation, "Key preparation retained common SQL admission.");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        else
        {
            var committed = await ProcessPreparedFeedAsync(fixture, file, writer);
            Assert.False(wrapper.HeldDuringPreparation, "Key preparation retained common SQL admission.");
            Assert.False(committed);
        }
        Assert.Equal(1, wrapper.Calls);
        await using var retained = fixture.Factory.OpenDatabase();
        Assert.Empty(await retained.ClearinghouseResponseReceipts.ToListAsync());
        Assert.Empty(await retained.ClaimAcknowledgementOutcomes.ToListAsync());
        Assert.Empty(await retained.RemittanceClaimOutcomes.ToListAsync());
        if (writer != "manual") Assert.Equal("2", (await retained.ClearinghouseFeedCheckpoints.SingleAsync()).Cursor);
    }

    [SqlServerTheory]
    [InlineData("manual")]
    [InlineData("status")]
    [InlineData("era")]
    public async Task ClaimReleaseSqlReplayAndStaleFeedNeedNoWrappingKey(string writer)
    {
        var wrapper = new ObservingPreparationWrapper();
        await using var fixture = await Fixture.CreateAsync(sqlServer: true, keyWrapper: wrapper);
        wrapper.Database = fixture.Database;
        var file = await fixture.GenerateAsync(fixture.AccountId);
        if (writer != "manual")
        {
            (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches",
                new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId))).EnsureSuccessStatusCode();
            Assert.True(await fixture.Worker(new SyntheticClearinghouseConnector()).ProcessOneAsync(CancellationToken.None));
            Assert.True(await ProcessPreparedFeedAsync(fixture, file, writer));
        }
        else
        {
            var doc = MockClearinghouse.Respond(file.Content, MockClearinghouseScenario.SyntaxRejected, DateTime.UtcNow);
            (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/responses", new ClaimResponseIngestRequest(doc.FunctionalAcknowledgement!))).EnsureSuccessStatusCode();
            wrapper.FailWrapping = true;
            var replay = await fixture.Biller.PostAsJsonAsync("/api/v1/billing/responses", new ClaimResponseIngestRequest(doc.FunctionalAcknowledgement!));
            replay.EnsureSuccessStatusCode();
            Assert.True((await replay.Content.ReadFromJsonAsync<ClaimResponseIngestResultDto>())!.AlreadyImported);
        }
        if (writer != "manual")
        {
            wrapper.FailWrapping = true;
            Assert.False(await ProcessPreparedFeedAsync(fixture, file, writer));
        }
        Assert.False(wrapper.HeldDuringPreparation, "Receipt wrapping retained a SQL decision lock.");
        Assert.Equal(1, wrapper.Calls);
        await using var db = fixture.Factory.OpenDatabase();
        var receipt = await db.ClearinghouseResponseReceipts.SingleAsync();
        var plain = await fixture.Factory.Services.GetRequiredService<EnvelopeProtector>().UnprotectAsync(
            new ProtectedValue(receipt.Ciphertext, receipt.Nonce, receipt.Tag, receipt.WrappedDataKey, receipt.KeyId),
            ClaimResponseIngestion.Binding(receipt));
        Assert.False(string.IsNullOrEmpty(plain)); // Prepared ID and persisted AAD still agree.
    }

    private static async Task<bool> ProcessPreparedFeedAsync(Fixture fixture, EdiFileDto file, string writer)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        if (writer == "era")
        {
            var doc = MockClearinghouse.Respond(file.Content, MockClearinghouseScenario.Accepted, DateTime.UtcNow);
            return await scope.ServiceProvider.GetRequiredService<ClaimResponseIngestion>()
                .ImportConnectorEraAsync(fixture.AccountId, "1", "0", doc.RemittanceAdvice!, CancellationToken.None);
        }
        var dispatch = await db.ClearinghouseDispatches.AsNoTracking().SingleAsync();
        var claims = ClaimResponseReader.ReadSubmission(file.Content).Claims;
        var page = new ClaimMdStatusPage("1", claims.Select((row, index) => new ClaimMdStatusClaim(
            (index + 1).ToString(), dispatch.ExternalFileId!, row.ClaimReference, row.RemoteClaimId!, "A")).ToList(), "<result synthetic='true'/>");
        return await new ClaimMdStatusProcessor(db, scope.ServiceProvider.GetRequiredService<EnvelopeProtector>())
            .ProcessAsync(fixture.AccountId, "0", page, CancellationToken.None);
    }

    private sealed class ObservingPreparationWrapper : IKeyWrapper
    {
        private readonly TestKeyWrapper _inner = new();
        public SyntheticPipelineDatabase? Database { get; set; }
        public Func<Task>? OnPreparation { get; set; }
        public bool HeldDuringPreparation { get; private set; }
        public int Calls { get; private set; }
        public bool FailWrapping { get; set; }
        public async Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (FailWrapping) throw new InvalidOperationException("Synthetic wrapping key unavailable.");
            var held = Database is not null && await Database.HasGrantedClaimReleaseLockAsync();
            HeldDuringPreparation |= held;
            // The mutation is deliberately skipped for the unsafe old wrapping order;
            // otherwise its own SQL locks would make the fixture deadlock, obscuring proof.
            if (!held && OnPreparation is not null) await OnPreparation();
            return await _inner.WrapAsync(dataKey, cancellationToken);
        }
        public Task<byte[]> UnwrapAsync(byte[] wrappedKey, string keyId, CancellationToken cancellationToken = default) =>
            _inner.UnwrapAsync(wrappedKey, keyId, cancellationToken);
    }

    private sealed class PreparationReadCounter : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public int Calls { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed) Calls++;
            return ValueTask.FromResult(result);
        }
    }
}
