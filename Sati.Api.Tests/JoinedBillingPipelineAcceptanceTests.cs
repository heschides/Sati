using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// Joins previously separate workflow and exchange tests. Only identities, profiles and
/// compliance evidence are seeded: every note, approval, claim, period submission, 837,
/// response receipt, claim outcome and deposit is produced by the actual HTTP pipeline.
/// The external clearinghouse and bank are deliberately not represented as real.
/// </summary>
public sealed class JoinedBillingPipelineAcceptanceTests
{
    [Theory]
    [InlineData(MockClearinghouseScenario.Accepted, "Paid", "Paid", 2)]
    [InlineData(MockClearinghouseScenario.SyntaxRejected, "FunctionalRejected", null, 0)]
    [InlineData(MockClearinghouseScenario.ClaimsRejected, "ClaimRejected", null, 0)]
    [InlineData(MockClearinghouseScenario.PartiallyAccepted, "RemittanceReceived", "Paid", 1)]
    [InlineData(MockClearinghouseScenario.PartialPayment, "RemittanceNeedsReview", "PartiallyPaid", 2)]
    [InlineData(MockClearinghouseScenario.Denied, "RemittanceNeedsReview", "Denied", 2)]
    [InlineData(MockClearinghouseScenario.ProviderLevelAdjustment, "Paid", "Paid", 2)]
    [InlineData(MockClearinghouseScenario.Reversal, "RemittanceNeedsReview", "Reversed", 2)]
    public async Task ActualDraftsReachTheExpectedSyntheticFinancialOutcome(
        MockClearinghouseScenario scenario, string finalStage, string? outcomeStatus, int outcomeCount)
    {
        await using var database = new SyntheticPipelineDatabase();
        await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database);
        var actors = await factory.SeedAsync();
        var periodId = await PrepareSubmittedPeriodAsync(factory, actors);
        using var biller = await factory.SignInAsync("synthetic-biller");
        var generation = await GenerateAsync(biller, periodId, Guid.NewGuid().ToString("N"));
        Assert.Contains("ST*837*", generation.Content, StringComparison.Ordinal);
        Assert.True(ClaimResponseReader.ReadEnvelope(generation.Content).IsTestInterchange);
        using var simulation = await biller.PostAsJsonAsync(
            $"/api/v1/billing/periods/{periodId}/mock-clearinghouse", new MockClearinghouseRequest(scenario));
        await RequireSuccessAsync(simulation);
        var result = (await simulation.Content.ReadFromJsonAsync<MockClearinghouseResultDto>())!;
        Assert.Equal(finalStage, result.StagesRecorded[^1]);
        Assert.Equal(outcomeCount, result.ClaimOutcomesRecorded);

        var history = (await biller.GetFromJsonAsync<List<BillingSubmissionHistoryDto>>("/api/v1/billing/submissions"))!;
        Assert.Equal(finalStage, BillingSubmissionProgressRules.Current(history)!.Stage);
        Assert.All(history, row => Assert.True(row.IsSynthetic));
        var outcomes = (await biller.GetFromJsonAsync<List<RemittanceClaimOutcomeDto>>("/api/v1/billing/remittances"))!;
        Assert.Equal(outcomeCount, outcomes.Count);
        Assert.All(outcomes, row =>
        {
            Assert.Equal(periodId, row.BillingPeriodId);
            Assert.Equal(outcomeStatus, row.Status);
            Assert.True(row.IsSynthetic);
        });
        var deposits = (await biller.GetFromJsonAsync<List<RemittanceDepositDto>>("/api/v1/billing/remittance-deposits"))!;
        Assert.Equal(outcomeCount == 0 ? 0 : 1, deposits.Count);
        Assert.All(deposits, row =>
        {
            Assert.True(row.IsSynthetic);
            Assert.Null(row.EftDepositAmount);
            Assert.Equal(nameof(DepositReconciliationStatus.AwaitingEft), row.Status);
            Assert.Equal(row.ClaimPaymentAmount + row.ProviderLevelAdjustmentAmount, row.RemittancePaymentAmount);
        });
        if (scenario == MockClearinghouseScenario.ProviderLevelAdjustment)
            Assert.Equal(-25m, Assert.Single(deposits).ProviderLevelAdjustmentAmount);

        // Exact response replays leave all persisted financial effects unchanged.
        await using var before = factory.OpenDatabase();
        var counts = await CountsAsync(before);
        foreach (var document in new[] { result.FunctionalAcknowledgement, result.ClaimAcknowledgement, result.RemittanceAdvice })
        {
            if (document is null) continue;
            using var replay = await biller.PostAsJsonAsync("/api/v1/billing/responses", new ClaimResponseIngestRequest(document));
            await RequireSuccessAsync(replay);
            Assert.True((await replay.Content.ReadFromJsonAsync<ClaimResponseIngestResultDto>())!.AlreadyImported);
        }
        await using var after = factory.OpenDatabase();
        Assert.Equal(counts, await CountsAsync(after));
    }

    [Fact]
    public Task ALostSuccessfulResponseAndHostRestartReplayWithoutDuplicatingFinancialEffects()
        => LostResponseRecoveryAsync(sqlServer: false);

    internal static async Task LostResponseRecoveryAsync(bool sqlServer)
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer);
        await database.InitializeAsync();
        var vault = new TestKeyWrapper();
        var key = Guid.NewGuid().ToString("N");
        int periodId;
        using (var host = new SyntheticPipelineFactory(database, vault))
        {
            var actors = await host.SeedAsync();
            periodId = await PrepareSubmittedPeriodAsync(host, actors);
            using var biller = await host.SignInAsync("synthetic-biller");
            using var lost = LoseSuccessfulResponse(host, biller);
            await Assert.ThrowsAsync<HttpRequestException>(() => lost.PostAsJsonAsync(
                $"/api/v1/billing/periods/{periodId}/edi", new GenerateEdiRequest(true, key)));
            await using var db = host.OpenDatabase();
            Assert.Single(await db.EdiGenerations.ToListAsync());
        }

        string remittance;
        EdiFileDto original;
        using (var restarted = new SyntheticPipelineFactory(database, vault))
        {
            using var biller = await restarted.SignInAsync("synthetic-biller");
            original = await GenerateAsync(biller, periodId, key);
            var documents = MockClearinghouse.Respond(original.Content, MockClearinghouseScenario.Accepted, DateTime.UtcNow);
            remittance = documents.RemittanceAdvice!;
            using var lost = LoseSuccessfulResponse(restarted, biller);
            await Assert.ThrowsAsync<HttpRequestException>(() => lost.PostAsJsonAsync(
                "/api/v1/billing/responses", new ClaimResponseIngestRequest(remittance)));
            await using var db = restarted.OpenDatabase();
            Assert.Single(await db.ClearinghouseResponseReceipts.ToListAsync());
            Assert.Equal(2, await db.RemittanceClaimOutcomes.CountAsync());
        }

        using var finalHost = new SyntheticPipelineFactory(database, vault);
        using var recoveredBiller = await finalHost.SignInAsync("synthetic-biller");
        Assert.Equal(original, await GenerateAsync(recoveredBiller, periodId, key));
        using var replay = await recoveredBiller.PostAsJsonAsync("/api/v1/billing/responses", new ClaimResponseIngestRequest(remittance));
        await RequireSuccessAsync(replay);
        var recovered = (await replay.Content.ReadFromJsonAsync<ClaimResponseIngestResultDto>())!;
        Assert.True(recovered.AlreadyImported);
        await using var retained = finalHost.OpenDatabase();
        Assert.Single(await retained.EdiGenerations.ToListAsync());
        Assert.Equal((1, 2, 1, 2), await CountsAsync(retained));
        var receipt = await retained.ClearinghouseResponseReceipts.SingleAsync();
        Assert.Equal(receipt.Id, recovered.ResponseId);
        // Host recreation retains the external key-service stand-in, but uses a new
        // protector instance: encrypted retained evidence remains readable and bound.
        var protector = new EnvelopeProtector(vault);
        var plaintext = await protector.UnprotectAsync(new ProtectedValue(
            receipt.Ciphertext, receipt.Nonce, receipt.Tag, receipt.WrappedDataKey, receipt.KeyId),
            ClaimResponseIngestion.Binding(receipt));
        Assert.Equal(remittance, plaintext);
    }

    internal static async Task<int> PrepareSubmittedPeriodAsync(SyntheticPipelineFactory factory, PipelineActors actors)
    {
        using var author = await factory.SignInAsync("synthetic-author");
        using var supervisor = await factory.SignInAsync("synthetic-supervisor");
        using var biller = await factory.SignInAsync("synthetic-biller");
        int? periodId = null;
        var index = 0;
        foreach (var person in new[] { actors.FirstPersonId, actors.SecondPersonId })
        {
            var request = new SaveNoteRequest("Synthetic joined-pipeline service contact.", DateTime.Today,
                "Pending", 60, 120 + index++ * 60, person, null, "Contact", null, null);
            using var create = await author.PostAsJsonAsync("/api/v1/notes", request);
            await RequireSuccessAsync(create);
            var draft = (await create.Content.ReadFromJsonAsync<NoteDto>())!;
            Assert.Equal("Pending", draft.Status);
            using var submit = await author.PutAsJsonAsync($"/api/v1/notes/{draft.Id}",
                request with { Status = "Logged", ExpectedRevision = draft.Revision });
            await RequireSuccessAsync(submit);
            var logged = (await submit.Content.ReadFromJsonAsync<NoteDto>())!;
            Assert.Equal("Logged", logged.Status);
            using var approval = await supervisor.PostAsJsonAsync($"/api/v1/supervisor/notes/{draft.Id}/approve",
                new SupervisorNoteActionRequest(null, logged.Revision));
            await RequireSuccessAsync(approval);
            var approved = (await approval.Content.ReadFromJsonAsync<NoteDto>())!;
            Assert.Equal("Approved", approved.Status);
            Assert.Equal(actors.SupervisorId, approved.ApprovedById);
            using var createClaim = await biller.PostAsJsonAsync("/api/v1/billing/claim-lines", new CreateClaimLineRequest(draft.Id, false, null));
            await RequireSuccessAsync(createClaim);
            var line = (await createClaim.Content.ReadFromJsonAsync<ClaimLineDto>())!;
            Assert.Equal(4m, line.Units);
            Assert.Equal(100m, line.ChargeAmount);
            Assert.False(line.IsComplianceException);
            if (periodId.HasValue) Assert.Equal(periodId.Value, line.BillingPeriodId);
            periodId = line.BillingPeriodId;
        }
        using var lockPeriod = await biller.PostAsync($"/api/v1/billing/periods/{periodId}/submit", null);
        await RequireSuccessAsync(lockPeriod);
        await using var db = factory.OpenDatabase();
        Assert.Equal(2, await db.ClaimLines.CountAsync());
        Assert.Equal(1, (await db.BillingPeriods.SingleAsync()).Status);
        Assert.Equal(2, await db.AuditEvents.CountAsync(x => x.Action == "note.approved"));
        return periodId!.Value;
    }

    private static HttpClient LoseSuccessfulResponse(SyntheticPipelineFactory factory, HttpClient authenticated)
    {
        var client = new HttpClient(new DiscardSuccessfulResponse { InnerHandler = factory.Server.CreateHandler() })
            { BaseAddress = new Uri("https://localhost") };
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        return client;
    }

    private sealed class DiscardSuccessfulResponse : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var response = await base.SendAsync(request, cancellationToken);
            await RequireSuccessAsync(response);
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            throw new HttpRequestException("Synthetic fault: successful committed response lost before delivery to caller.");
        }
    }

    private static async Task<EdiFileDto> GenerateAsync(HttpClient biller, int periodId, string key)
    {
        using var response = await biller.PostAsJsonAsync($"/api/v1/billing/periods/{periodId}/edi", new GenerateEdiRequest(true, key));
        await RequireSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<EdiFileDto>())!;
    }

    private static async Task RequireSuccessAsync(HttpResponseMessage response)
        => Assert.True(response.IsSuccessStatusCode, $"HTTP {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task<(int Receipts, int Outcomes, int Deposits, int Events)> CountsAsync(Sati.Api.Data.ApiDbContext db)
        => (await db.ClearinghouseResponseReceipts.CountAsync(), await db.RemittanceClaimOutcomes.CountAsync(),
            await db.RemittanceDeposits.CountAsync(), await db.BillingSubmissionEvents.CountAsync());
}

[Collection("Synthetic pipeline SQL Server")]
public sealed class JoinedBillingPipelineSqlServerAcceptanceTests
{
    [SqlServerFact]
    public Task ActualSqlServerPipelineSurvivesLostSuccessfulResponsesAndTwoHostRestarts()
        => JoinedBillingPipelineAcceptanceTests.LostResponseRecoveryAsync(sqlServer: true);
}
