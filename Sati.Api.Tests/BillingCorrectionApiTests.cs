using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// Recording the bank deposit behind a remittance, and correcting a claim after the payer
/// answered: over HTTP, against the permanent ingestion path the mock clearinghouse feeds.
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class BillingCorrectionApiTests
{
    // Agency 2's submitted period and its single claim line (note 603, $33.25).
    private const int SubmittedPeriodId = 1202;
    private const int ClaimLineId = 1402;
    // Agency 2's seeded remittance deposit: the report says $25.60; its legacy EFT column says $25.50.
    private const long SeededDepositId = 1702;

    private readonly SatiApiFactory _factory;

    public BillingCorrectionApiTests(SatiApiFactory factory) => _factory = factory;

    // -------------------------------------------------------------------------
    // Bank deposits
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RecordingTheMatchingDepositReconcilesItAndACorrectionIsKeptBesideIt()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-two");
        var before = await DepositAsync(admin, SeededDepositId);

        var matched = await RecordAsync(admin, SeededDepositId,
            new RecordEftDepositRequest(25.60m, new DateTime(2026, 8, 16), "ACH-778", null, before.CurrentEftRecordId));
        Assert.Equal(HttpStatusCode.OK, matched.StatusCode);
        var afterMatch = await DepositAsync(admin, SeededDepositId);
        Assert.Equal(nameof(DepositReconciliationStatus.Matched), afterMatch.Status);
        Assert.Equal(25.60m, afterMatch.EftDepositAmount);

        // Someone who still has the old figure open cannot stack a second entry on it.
        var stale = await RecordAsync(admin, SeededDepositId,
            new RecordEftDepositRequest(25.60m, new DateTime(2026, 8, 16), null, null, before.CurrentEftRecordId));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // A correction must say why.
        var unexplained = await RecordAsync(admin, SeededDepositId,
            new RecordEftDepositRequest(25.00m, new DateTime(2026, 8, 16), null, null, afterMatch.CurrentEftRecordId));
        Assert.Equal(HttpStatusCode.BadRequest, unexplained.StatusCode);

        var corrected = await RecordAsync(admin, SeededDepositId,
            new RecordEftDepositRequest(25.00m, new DateTime(2026, 8, 16), null, "Bank statement shows $25.00.",
                afterMatch.CurrentEftRecordId));
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var afterCorrection = await DepositAsync(admin, SeededDepositId);
        Assert.Equal(nameof(DepositReconciliationStatus.EftMismatch), afterCorrection.Status);
        Assert.Equal(-0.60m, afterCorrection.Difference);

        var history = await admin.GetFromJsonAsync<List<EftDepositRecordDto>>(
            $"/api/v1/billing/remittance-deposits/{SeededDepositId}/eft");
        Assert.Equal([25.00m, 25.60m], history!.Take(2).Select(row => row.Amount));
        Assert.Equal(history![1].Id, history[0].SupersedesRecordId);
    }

    [Fact]
    public async Task AFutureDepositDateIsRefused()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-two");
        var before = await DepositAsync(admin, SeededDepositId);

        var response = await RecordAsync(admin, SeededDepositId,
            new RecordEftDepositRequest(25.60m, DateTime.Today.AddDays(30), null, "x", before.CurrentEftRecordId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnotherAgencysDepositIsNotFound()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var record = await RecordAsync(admin, SeededDepositId,
            new RecordEftDepositRequest(25.60m, new DateTime(2026, 8, 16), null, null, null));
        var read = await admin.GetAsync($"/api/v1/billing/remittance-deposits/{SeededDepositId}/eft");

        Assert.Equal(HttpStatusCode.NotFound, record.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task APaidBatchReadsReconciledOnlyWhileItsDepositMatches()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-two");
        await RunMockAsync(admin, MockClearinghouseScenario.Accepted);
        var deposits = await admin.GetFromJsonAsync<List<RemittanceDepositDto>>("/api/v1/billing/remittance-deposits");
        var deposit = deposits!.Where(row => row.Id != SeededDepositId).MaxBy(row => row.Id)!;
        Assert.Equal(nameof(DepositReconciliationStatus.AwaitingEft), deposit.Status);
        Assert.Equal(nameof(BillingSubmissionStage.Paid), await CurrentStageAsync(admin));

        await RecordAsync(admin, deposit.Id, new RecordEftDepositRequest(
            deposit.RemittancePaymentAmount, DateTime.Today, null, null, null));
        Assert.Equal(nameof(BillingSubmissionStage.Reconciled), await CurrentStageAsync(admin));

        // A later correction to the deposit withdraws the reconciliation; nothing was stored
        // that would have to be taken back.
        var current = (await admin.GetFromJsonAsync<List<RemittanceDepositDto>>("/api/v1/billing/remittance-deposits"))!
            .Single(row => row.Id == deposit.Id);
        await RecordAsync(admin, deposit.Id, new RecordEftDepositRequest(
            deposit.RemittancePaymentAmount - 1m, DateTime.Today, null, "Short by a dollar.", current.CurrentEftRecordId));
        Assert.Equal(nameof(BillingSubmissionStage.Paid), await CurrentStageAsync(admin));
    }

    // -------------------------------------------------------------------------
    // Claim corrections
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ADeniedClaimIsReplacedCitingThePayersClaimNumber()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-two");
        await RunMockAsync(admin, MockClearinghouseScenario.DeniedMissingInformation);

        var denied = await ClaimAsync(admin);
        Assert.Equal(nameof(ClaimLifecycleState.Denied), denied.State);
        Assert.Equal([nameof(ClaimCorrectionAction.Replace)], denied.AllowedActions);
        Assert.StartsWith("MOCK", denied.PayerClaimControlNumber);

        // Void is not offered for a denied claim, and asking for it anyway is refused.
        var voided = await CorrectAsync(admin, ClaimCorrectionAction.Void, "Billed in error.");
        Assert.Equal(HttpStatusCode.Conflict, voided.StatusCode);

        var replaced = await CorrectAsync(admin, ClaimCorrectionAction.Replace, "Diagnosis corrected on the profile.");
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(nameof(ClaimLifecycleState.CorrectionWaitingToSend), (await ClaimAsync(admin)).State);

        // One correction at a time.
        var second = await CorrectAsync(admin, ClaimCorrectionAction.Replace, "Second attempt.");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var file = await GenerateCorrectionAsync(admin);
        Assert.Contains("CORRECTION", file.FileName);
        Assert.Contains($"{denied.PayerClaimControlNumber}~", file.Content);
        Assert.Contains("REF*F8*", file.Content);
        Assert.Contains("*11::7*", file.Content);
        var submission = ClaimResponseReader.ReadSubmission(file.Content);
        Assert.Equal("7", Assert.Single(submission.Claims).FrequencyCode);

        // Nothing is left waiting, so a second correction file has nothing to carry.
        var empty = await admin.PostAsJsonAsync($"/api/v1/billing/periods/{SubmittedPeriodId}/corrections/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Conflict, empty.StatusCode);

        // The replacement goes through the same mock and comes back paid.
        await SubmitLatestToMockAsync(admin, MockClearinghouseScenario.Accepted);
        var paid = await ClaimAsync(admin);
        Assert.Equal(nameof(ClaimLifecycleState.Paid), paid.State);
        Assert.True(paid.SubmissionCount >= 2);
    }

    [Fact]
    public async Task APaidClaimCanBeVoidedAndTheVoidComesBackAsAReversal()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-two");
        await RunMockAsync(admin, MockClearinghouseScenario.Accepted);
        var paid = await ClaimAsync(admin);
        Assert.Equal(nameof(ClaimLifecycleState.Paid), paid.State);
        Assert.Equal(
            [nameof(ClaimCorrectionAction.Replace), nameof(ClaimCorrectionAction.Void)],
            paid.AllowedActions);

        Assert.Equal(HttpStatusCode.OK,
            (await CorrectAsync(admin, ClaimCorrectionAction.Void, "Service was billed in error.")).StatusCode);
        var file = await GenerateCorrectionAsync(admin);
        Assert.Contains("*11::8*", file.Content);
        Assert.Contains($"REF*F8*{paid.PayerClaimControlNumber}~", file.Content);

        await SubmitLatestToMockAsync(admin, MockClearinghouseScenario.Accepted);
        var reversed = await ClaimAsync(admin);
        Assert.Equal(nameof(ClaimLifecycleState.Reversed), reversed.State);
        // With the payment taken back, the service can only be billed again as a new claim.
        Assert.Equal([nameof(ClaimCorrectionAction.Resubmit)], reversed.AllowedActions);
    }

    [Fact]
    public async Task AClaimRejectedBeforeReviewIsResentAsANewClaim()
    {
        // A fresh pipeline: the shared fixture's claim already has payment history, and a claim
        // the payer once adjudicated is (rightly) replaced rather than resent.
        await using var database = new SyntheticPipelineDatabase();
        await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database);
        var actors = await factory.SeedAsync();
        var periodId = await JoinedBillingPipelineAcceptanceTests.PrepareSubmittedPeriodAsync(factory, actors);
        using var biller = await factory.SignInAsync("synthetic-biller");
        (await biller.PostAsJsonAsync($"/api/v1/billing/periods/{periodId}/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")))).EnsureSuccessStatusCode();
        (await biller.PostAsJsonAsync($"/api/v1/billing/periods/{periodId}/mock-clearinghouse",
            new MockClearinghouseRequest(MockClearinghouseScenario.ClaimsRejected))).EnsureSuccessStatusCode();

        var claims = (await biller.GetFromJsonAsync<List<BillingClaimStatusDto>>(
            $"/api/v1/billing/periods/{periodId}/claims"))!;
        Assert.NotEmpty(claims);
        Assert.All(claims, claim =>
        {
            Assert.Equal(nameof(ClaimLifecycleState.ClaimRejected), claim.State);
            Assert.Equal([nameof(ClaimCorrectionAction.Resubmit)], claim.AllowedActions);
            Assert.Null(claim.PayerClaimControlNumber);
        });

        var first = claims[0];
        Assert.Equal(HttpStatusCode.OK, (await biller.PostAsJsonAsync(
            $"/api/v1/billing/periods/{periodId}/corrections",
            new CreateClaimCorrectionRequest(first.ClaimLineId, ClaimCorrectionAction.Resubmit, "MaineCare ID corrected."))).StatusCode);
        var response = await biller.PostAsJsonAsync($"/api/v1/billing/periods/{periodId}/corrections/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var file = (await response.Content.ReadFromJsonAsync<EdiFileDto>())!;
        // Only the corrected claim goes, as a new claim with no payer number to cite.
        Assert.Single(ClaimResponseReader.ReadSubmission(file.Content).Claims);
        Assert.Contains("::1*", file.Content);
        Assert.DoesNotContain("REF*F8*", file.Content);
    }

    [Fact]
    public async Task AClaimStillAwaitingThePayerOffersNoCorrection()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-two");
        var generation = await admin.PostAsJsonAsync($"/api/v1/billing/periods/{SubmittedPeriodId}/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")));
        generation.EnsureSuccessStatusCode();

        var waiting = await ClaimAsync(admin);

        Assert.Equal(nameof(ClaimLifecycleState.AwaitingFileCheck), waiting.State);
        Assert.Empty(waiting.AllowedActions);
        Assert.Equal(HttpStatusCode.Conflict,
            (await CorrectAsync(admin, ClaimCorrectionAction.Resubmit, "Too early.")).StatusCode);
    }

    [Fact]
    public async Task ACorrectionNeedsAReason()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-two");

        var response = await CorrectAsync(admin, ClaimCorrectionAction.Replace, "  ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnotherAgencysClaimsCannotBeReadOrCorrected()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-one");

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/v1/billing/periods/{SubmittedPeriodId}/claims")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await CorrectAsync(admin, ClaimCorrectionAction.Replace, "Not my agency.")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync($"/api/v1/billing/periods/{SubmittedPeriodId}/corrections/edi",
                new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")))).StatusCode);
    }

    [Fact]
    public async Task EveryCorrectionRouteNeedsBillingPermission()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-without-billing-one");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/v1/billing/periods/{SubmittedPeriodId}/claims")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await CorrectAsync(client, ClaimCorrectionAction.Replace, "No permission.")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/v1/billing/periods/{SubmittedPeriodId}/corrections/edi",
                new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/v1/billing/remittance-deposits/{SeededDepositId}/eft")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await RecordAsync(client, SeededDepositId,
                new RecordEftDepositRequest(1m, new DateTime(2026, 8, 16), null, null, null))).StatusCode);
    }

    // -------------------------------------------------------------------------

    private static async Task<RemittanceDepositDto> DepositAsync(HttpClient client, long id) =>
        (await client.GetFromJsonAsync<List<RemittanceDepositDto>>("/api/v1/billing/remittance-deposits"))!
            .Single(row => row.Id == id);

    private static Task<HttpResponseMessage> RecordAsync(HttpClient client, long depositId, RecordEftDepositRequest request) =>
        client.PostAsJsonAsync($"/api/v1/billing/remittance-deposits/{depositId}/eft", request);

    private static async Task<BillingClaimStatusDto> ClaimAsync(HttpClient client) =>
        Assert.Single((await client.GetFromJsonAsync<List<BillingClaimStatusDto>>(
            $"/api/v1/billing/periods/{SubmittedPeriodId}/claims"))!);

    private static Task<HttpResponseMessage> CorrectAsync(HttpClient client, ClaimCorrectionAction action, string reason) =>
        client.PostAsJsonAsync($"/api/v1/billing/periods/{SubmittedPeriodId}/corrections",
            new CreateClaimCorrectionRequest(ClaimLineId, action, reason));

    private static async Task<EdiFileDto> GenerateCorrectionAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/billing/periods/{SubmittedPeriodId}/corrections/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<EdiFileDto>())!;
    }

    private static async Task RunMockAsync(HttpClient client, MockClearinghouseScenario scenario)
    {
        var generation = await client.PostAsJsonAsync($"/api/v1/billing/periods/{SubmittedPeriodId}/edi",
            new GenerateEdiRequest(true, Guid.NewGuid().ToString("N")));
        Assert.True(generation.IsSuccessStatusCode, await generation.Content.ReadAsStringAsync());
        await SubmitLatestToMockAsync(client, scenario);
    }

    private static async Task SubmitLatestToMockAsync(HttpClient client, MockClearinghouseScenario scenario)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/billing/periods/{SubmittedPeriodId}/mock-clearinghouse", new MockClearinghouseRequest(scenario));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> CurrentStageAsync(HttpClient client)
    {
        var rows = (await client.GetFromJsonAsync<List<BillingSubmissionHistoryDto>>("/api/v1/billing/submissions"))!;
        return BillingSubmissionProgressRules.Current(rows.Where(row => row.BillingPeriodId == SubmittedPeriodId))!.Stage;
    }
}
