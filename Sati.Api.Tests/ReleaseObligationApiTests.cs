using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[CollectionDefinition(CollectionName)]
public sealed class ReleaseObligationApiCollection : ICollectionFixture<SatiApiFactory>
{
    public const string CollectionName = "Release obligation API integration";
}

[Collection(ReleaseObligationApiCollection.CollectionName)]
public sealed class ReleaseObligationApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task ApiReconcilesRecipientRowsAndRecordsIndependentAttestationAndWithdrawal()
    {
        var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var target = DateTime.Today.AddMonths(-1).Date;
        var medical = await AddProviderAsync(client, "Healthcare", $"Medical {Guid.NewGuid():N}");
        var waiver = await AddProviderAsync(client, "Waiver", $"Waiver {Guid.NewGuid():N}");
        await AddLinkAsync(client, 101, medical.Id, target.AddYears(-1));
        await AddLinkAsync(client, 101, waiver.Id, target.AddYears(-1));

        var status = await PostAsync<ReconcileReleaseObligationsRequest, ReleaseObligationStatusDto>(
            client,
            "/api/v1/people/101/release-obligations/reconcile",
            new ReconcileReleaseObligationsRequest(target));

        Assert.Equal(3, status.Obligations.Count);
        Assert.Single(status.Obligations.Where(item => item.Category == "Medical"));
        Assert.Single(status.Obligations.Where(item => item.Category == "Agency"));
        Assert.Single(status.Obligations.Where(item => item.Category == "Dhhs"));
        Assert.Equal("Consumer", status.RequiredSignerCapacity);
        Assert.Equal("Consumer signature", status.SignerLabel);

        var medicalObligation = status.Obligations.Single(item => item.Category == "Medical");
        var completed = await PostAsync<AttestReleaseObligationRequest, ReleaseObligationDto>(
            client,
            $"/api/v1/people/101/release-obligations/{medicalObligation.ObligationId:D}/attest",
            new AttestReleaseObligationRequest(target));
        var withdrawn = await PostAsync<WithdrawReleaseAuthorizationRequest, ReleaseObligationDto>(
            client,
            $"/api/v1/people/101/release-obligations/{medicalObligation.ObligationId:D}/withdraw",
            new WithdrawReleaseAuthorizationRequest(target.AddDays(1), "Synthetic withdrawal."));

        Assert.Equal(target, completed.CompletedOn);
        Assert.Single(completed.Attestations);
        Assert.Equal(target, withdrawn.CompletedOn);
        Assert.Equal(target.AddDays(1), withdrawn.WithdrawnOn);
        Assert.False(withdrawn.IsAuthorizationActive);

        var read = await client.GetFromJsonAsync<ReleaseObligationStatusDto>(
            $"/api/v1/people/101/release-obligations?targetEffectiveDate={target:yyyy-MM-dd}");
        Assert.NotNull(read);
        Assert.Null(read.Obligations.Single(item => item.Category == "Agency").CompletedOn);
    }

    [Fact]
    public async Task ApiEnforcesCaseloadAndGuardianOnlyPresentation()
    {
        var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var target = new DateTime(DateTime.Today.Year, 5, 6);
        if (target > DateTime.Today)
            target = target.AddYears(-1);
        var status = await PostAsync<ReconcileReleaseObligationsRequest, ReleaseObligationStatusDto>(
            owner,
            "/api/v1/people/102/release-obligations/reconcile",
            new ReconcileReleaseObligationsRequest(target));
        Assert.Equal("Guardian", status.RequiredSignerCapacity);
        Assert.Equal("Guardian signature only", status.SignerLabel);

        var foreign = await factory.CreateAuthenticatedClientAsync("case-manager-two");
        var response = await foreign.GetAsync(
            $"/api/v1/people/102/release-obligations?targetEffectiveDate={target:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApiStatusSurfacesLegacyCompletionWithoutProjectingItToTheExactRelease()
    {
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        DateTime target;
        var legacyCompletedOn = default(DateTime);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var person = await db.People.SingleAsync(item => item.Id == personId);
            target = person.EffectiveDate!.Value.Date;
            legacyCompletedOn = target.AddDays(-3);
            db.Forms.Add(new ServerForm
            {
                PersonId = personId,
                Type = "Release_DHHS",
                TargetEffectiveDate = target,
                DueDate = target,
                CompletedDate = legacyCompletedOn
            });
            await db.SaveChangesAsync();
        }

        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var reconciled = await PostAsync<ReconcileReleaseObligationsRequest,
            ReleaseObligationStatusDto>(
            client,
            $"/api/v1/people/{personId}/release-obligations/reconcile",
            new ReconcileReleaseObligationsRequest(target));

        var warning = Assert.Single(reconciled.LinkageIssues,
            issue => issue.Code == ReleaseLinkageIssueCodes.LegacyCategoryCompletionNeedsReview);
        Assert.Contains("did not copy its date", warning.Message);
        var dhhs = Assert.Single(reconciled.Obligations,
            item => item.Category == nameof(ReleaseObligationCategory.Dhhs));
        Assert.Null(dhhs.CompletedOn);

        await PostAsync<AttestReleaseObligationRequest, ReleaseObligationDto>(
            client,
            $"/api/v1/people/{personId}/release-obligations/{dhhs.ObligationId:D}/attest",
            new AttestReleaseObligationRequest(legacyCompletedOn));
        var reviewed = await client.GetFromJsonAsync<ReleaseObligationStatusDto>(
            $"/api/v1/people/{personId}/release-obligations?targetEffectiveDate={target:yyyy-MM-dd}");

        Assert.NotNull(reviewed);
        Assert.DoesNotContain(reviewed.LinkageIssues,
            issue => issue.Code == ReleaseLinkageIssueCodes.LegacyCategoryCompletionNeedsReview);
    }

    [Fact]
    public async Task GeneratedMedicalReleaseArtifactUsesTheExactObligationsCycleWhenCycleIsOmitted()
    {
        var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var target = new DateTime(DateTime.Today.Year, 5, 6);
        if (target > DateTime.Today)
            target = target.AddYears(-1);
        // Use an earlier valid anniversary so this catches accidental fallback to the
        // server's current cycle when the client supplies only the durable obligation id.
        target = target.AddYears(-1);
        var medical = await AddProviderAsync(
            client, "Healthcare", $"Exact medical {Guid.NewGuid():N}");
        await AddLinkAsync(client, 102, medical.Id, target.AddYears(-1));
        var status = await PostAsync<ReconcileReleaseObligationsRequest, ReleaseObligationStatusDto>(
            client,
            "/api/v1/people/102/release-obligations/reconcile",
            new ReconcileReleaseObligationsRequest(target));
        var obligation = status.Obligations.Single(item =>
            item.Category == nameof(ReleaseObligationCategory.Medical) &&
            item.RecipientProviderId == medical.Id);

        using var generated = await client.PostAsJsonAsync(
            $"/api/v1/people/102/documents/{AnnualDocumentKind.ReleaseMedical}",
            new RenderAnnualDocumentRequest(
                Release: ValidReleaseRequest(),
                ReleaseObligationId: obligation.ObligationId));
        generated.EnsureSuccessStatusCode();

        var artifacts = await client.GetFromJsonAsync<List<DocumentArtifactDto>>(
            $"/api/v1/people/102/documents?cycleStart={target:yyyy-MM-dd}");
        var artifact = Assert.Single(artifacts!, item =>
            item.Kind == nameof(AnnualDocumentKind.ReleaseMedical) &&
            item.ReleaseObligationRecordId == obligation.Id);
        Assert.Equal(target, artifact.CycleStart);

        using var conflictingCycle = await client.PostAsJsonAsync(
            $"/api/v1/people/102/documents/{AnnualDocumentKind.ReleaseMedical}",
            new RenderAnnualDocumentRequest(
                target.AddYears(1),
                ValidReleaseRequest(),
                ReleaseObligationId: obligation.ObligationId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, conflictingCycle.StatusCode);
    }

    private static async Task<ProviderDto> AddProviderAsync(
        HttpClient client,
        string type,
        string name) =>
        await PostAsync<SaveProviderRequest, ProviderDto>(
            client,
            "/api/v1/providers",
            new SaveProviderRequest(
                type,
                name,
                null,
                null,
                null,
                null,
                null,
                null,
                0,
                false,
                null,
                null,
                null,
                MedicalKind: type == "Healthcare" ? "Individual" : null));

    private static async Task AddLinkAsync(
        HttpClient client,
        int personId,
        int providerId,
        DateTime startsOn)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/people/{personId}/providers",
            new SaveConsumerProviderRequest(
                providerId,
                "Synthetic assignment",
                false,
                startsOn,
                null,
                false,
                0));
        response.EnsureSuccessStatusCode();
    }

    private static AgencyReleaseRequest ValidReleaseRequest() => new(
        true,
        "Healthcare provider",
        "Exact medical recipient",
        "Medical provider",
        "1 Synthetic Street",
        "Augusta",
        "ME",
        null,
        "207-555-0100",
        "records@example.test",
        [AgencyReleaseInformation.IntakeAssessment],
        null,
        DateOnly.FromDateTime(DateTime.Today),
        DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
        nameof(AgencyReleaseScope.OneTime),
        false,
        false,
        false,
        false);

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(
        HttpClient client,
        string path,
        TRequest request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>()
               ?? throw new InvalidOperationException("The API returned no response body.");
    }
}
