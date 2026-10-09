using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Api.Tests;

public sealed partial class ClearinghouseDispatchApiTests
{
    [Theory]
    [InlineData("generation")]
    [InlineData("queue")]
    [InlineData("receipt")]
    public async Task FailedClaimReleaseCommitRetainsNoPartialRecordOrAudit(string writer)
    {
        var fault = new FailReleaseCommit();
        await using var fixture = await Fixture.CreateAsync(interceptor: fault);
        EdiFileDto? file = writer == "generation" ? null : await fixture.GenerateAsync(fixture.AccountId);
        await using var db = fixture.Factory.OpenDatabase();
        var before = await ReleaseCountsAsync(db);
        fault.Armed = true;
        HttpResponseMessage response;
        if (writer == "generation") response = await fixture.Biller.PostAsJsonAsync(
            $"/api/v1/billing/periods/{fixture.PeriodId}/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")) { ClearinghouseAccountId = fixture.AccountId });
        else if (writer == "queue") response = await fixture.Biller.PostAsJsonAsync(
            "/api/v1/billing/clearinghouse/dispatches", new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId));
        else response = await fixture.Biller.PostAsJsonAsync("/api/v1/billing/responses", new ClaimResponseIngestRequest(
            MockClearinghouse.Respond(file!.Content, MockClearinghouseScenario.Accepted, DateTime.UtcNow).RemittanceAdvice!));
        Assert.Equal(writer == "receipt" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(fault.Fired);
        Assert.Equal(before, await ReleaseCountsAsync(db));
    }

    [Theory]
    [InlineData("sending")]
    [InlineData("hold")]
    [InlineData("result")]
    public async Task FailedWorkerCommitPreservesTheDurableSendingBoundary(string phase)
    {
        var fault = new FailReleaseCommit();
        await using var fixture = await Fixture.CreateAsync(interceptor: fault);
        var prior = await fixture.GenerateAsync(fixture.AccountId);
        if (phase == "hold") await fixture.GenerateAsync(fixture.AccountId);
        var request = new QueueClearinghouseDispatchRequest(fixture.GenerationId, fixture.AccountId);
        (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches", request)).EnsureSuccessStatusCode();
        if (phase == "hold") (await fixture.Biller.PostAsJsonAsync("/api/v1/billing/responses",
            new ClaimResponseIngestRequest(MockClearinghouse.Respond(prior.Content, MockClearinghouseScenario.SyntaxRejected,
                DateTime.UtcNow).FunctionalAcknowledgement))).EnsureSuccessStatusCode();
        fault.Target = phase == "result" ? ClearinghouseDispatchState.AcceptedByClearinghouse :
            phase == "hold" ? ClearinghouseDispatchState.CancelledBeforeSend : ClearinghouseDispatchState.Sending;
        fault.Armed = true;
        var connector = new CountedOutcomeConnector(ClearinghouseAttemptOutcome.Accepted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Worker(connector).ProcessOneAsync(CancellationToken.None));
        Assert.True(fault.Fired);
        await using var db = fixture.Factory.OpenDatabase();
        var retained = await db.ClearinghouseDispatches.AsNoTracking().SingleAsync();
        Assert.Equal(phase == "result" ? ClearinghouseDispatchState.Sending : ClearinghouseDispatchState.Queued, retained.State);
        Assert.Empty(await db.ClearinghouseDispatchAttempts.ToListAsync());
        Assert.Equal(phase == "result" ? 1 : 0, connector.Calls);
        if (phase == "result")
        {
            // A restarted process sees durable uncertainty and never uploads again.
            Assert.False(await fixture.Worker(connector).ProcessOneAsync(CancellationToken.None));
            var replay = await fixture.Biller.PostAsJsonAsync("/api/v1/billing/clearinghouse/dispatches", request);
            replay.EnsureSuccessStatusCode();
            Assert.Equal("Sending", (await replay.Content.ReadFromJsonAsync<ClearinghouseDispatchDto>())!.State);
            var fresh = await fixture.Biller.PostAsJsonAsync($"/api/v1/billing/periods/{fixture.PeriodId}/edi",
                new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")) { ClearinghouseAccountId = fixture.AccountId });
            Assert.Equal(HttpStatusCode.Conflict, fresh.StatusCode);
            Assert.Equal(1, connector.Calls);
        }
    }

    private static async Task<(int Files, int Intents, int Attempts, int Receipts, int Events, int Acks, int Payments, int Audits)>
        ReleaseCountsAsync(ApiDbContext db) => (await db.EdiGenerations.CountAsync(), await db.ClearinghouseDispatches.CountAsync(),
            await db.ClearinghouseDispatchAttempts.CountAsync(), await db.ClearinghouseResponseReceipts.CountAsync(),
            await db.BillingSubmissionEvents.CountAsync(), await db.ClaimAcknowledgementOutcomes.CountAsync(),
            await db.RemittanceClaimOutcomes.CountAsync(), await db.AuditEvents.CountAsync());

    private sealed class FailReleaseCommit : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public bool Fired { get; private set; }
        public ClearinghouseDispatchState? Target { get; set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed && (Target is null || eventData.Context!.ChangeTracker.Entries<ClearinghouseDispatch>()
                .Any(row => row.Entity.State == Target)))
            { Armed = false; Fired = true; throw new InvalidOperationException("Synthetic failure before claim release commit."); }
            return ValueTask.FromResult(result);
        }
    }
}
