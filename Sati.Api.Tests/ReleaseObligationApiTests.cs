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
        var evidenceNoteId = Assert.IsType<int>(completed.Attestations[0].EvidenceNoteId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var draft = await db.Notes.AsNoTracking().SingleAsync(note => note.Id == evidenceNoteId);
            Assert.Equal(medicalObligation.Id, draft.ReleaseObligationId);
            Assert.Equal(NoteWorkflow.Pending, draft.Status);
            Assert.Equal(target, draft.EventDate);
        }
        Assert.Equal(target, withdrawn.CompletedOn);
        Assert.Equal(target.AddDays(1), withdrawn.WithdrawnOn);
        Assert.False(withdrawn.IsAuthorizationActive);

        var revoked = await PostAsync<RevokeReleaseAttestationRequest, ReleaseObligationDto>(
            client,
            $"/api/v1/people/101/release-obligations/{medicalObligation.ObligationId:D}/attestation/revoke",
            new RevokeReleaseAttestationRequest("The completion date needs correction."));
        Assert.Null(revoked.CompletedOn);
        Assert.NotNull(revoked.Attestations[0].RevokedAtUtc);
        Assert.Equal("The completion date needs correction.",
            revoked.Attestations[0].RevocationReason);

        var conflicting = await client.PostAsJsonAsync(
            $"/api/v1/people/101/release-obligations/{medicalObligation.ObligationId:D}/attest",
            new AttestReleaseObligationRequest(target.AddDays(2)));
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        Assert.Equal("release_note_date_conflict",
            (await conflicting.Content.ReadFromJsonAsync<ApiErrorDto>())?.Code);

        var reattested = await PostAsync<AttestReleaseObligationRequest, ReleaseObligationDto>(
            client,
            $"/api/v1/people/101/release-obligations/{medicalObligation.ObligationId:D}/attest",
            new AttestReleaseObligationRequest(target));
        Assert.Equal(evidenceNoteId, reattested.Attestations.Last().EvidenceNoteId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.Equal(1, await db.Notes.CountAsync(note =>
                note.ReleaseObligationId == medicalObligation.Id));
        }

        var read = await client.GetFromJsonAsync<ReleaseObligationStatusDto>(
            $"/api/v1/people/101/release-obligations?targetEffectiveDate={target:yyyy-MM-dd}");
        Assert.NotNull(read);
        Assert.Null(read.Obligations.Single(item => item.Category == "Agency").CompletedOn);
        Assert.Equal(target,
            read.Obligations.Single(item => item.Category == "Medical").CompletedOn);
    }

    [Fact]
    public async Task AdminCorrectsClaimedReleaseNoteAndFlagsTheRetainedClaim()
    {
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        var targetDate = DateTime.Today.AddMonths(-1).Date;
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var status = await PostAsync<ReconcileReleaseObligationsRequest, ReleaseObligationStatusDto>(
            caseManager,
            $"/api/v1/people/{personId}/release-obligations/reconcile",
            new ReconcileReleaseObligationsRequest(targetDate));
        var dhhs = Assert.Single(status.Obligations, item => item.Category == "Dhhs");
        var completed = await PostAsync<AttestReleaseObligationRequest, ReleaseObligationDto>(
            caseManager,
            $"/api/v1/people/{personId}/release-obligations/{dhhs.ObligationId:D}/attest",
            new AttestReleaseObligationRequest(targetDate));
        var noteId = Assert.IsType<int>(Assert.Single(completed.Attestations).EvidenceNoteId);
        int? createdPeriodId = null;
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var note = await db.Notes.SingleAsync(item => item.Id == noteId);
                note.Status = NoteWorkflow.Approved;
                var period = await db.BillingPeriods.SingleOrDefaultAsync(item =>
                    item.UserId == 12 && item.Month == targetDate.Month &&
                    item.Year == targetDate.Year);
                if (period is null)
                {
                    period = new ServerBillingPeriod
                    {
                        UserId = 12,
                        Month = targetDate.Month,
                        Year = targetDate.Year,
                        Status = 1,
                        SubmittedAt = DateTime.UtcNow
                    };
                    db.BillingPeriods.Add(period);
                    await db.SaveChangesAsync();
                    createdPeriodId = period.Id;
                }
                db.ClaimLines.Add(new ServerClaimLine
                {
                    NoteId = noteId,
                    BillingPeriodId = period.Id,
                    DateOfService = targetDate,
                    ProcedureCode = "T1016",
                    Units = 1,
                    ChargeAmount = 1,
                    ClientMaineCareId = "synthetic",
                    RenderingProviderNpi = "1999999984",
                    DiagnosisCode = "F89",
                    PlaceOfService = 11
                });
                await db.SaveChangesAsync();
            }

            var path = $"/api/v1/admin/notes/{noteId}/release-date-correction-target";
            using var forbidden = await caseManager.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            var source = await admin.GetFromJsonAsync<AdminReleaseNoteCorrectionTargetDto>(path);
            Assert.NotNull(source);
            Assert.Equal(dhhs.Id, source.ReleaseObligationId);
            Assert.True(source.HasClaimRecord);
            var correctedDate = targetDate.AddDays(1);
            var request = new AdminCorrectReleaseNoteDateRequest(
                source.Revision, correctedDate,
                "The source record confirms the release was completed the next day.", true);
            using var denied = await caseManager.PostAsJsonAsync(
                $"/api/v1/admin/notes/{noteId}/correct-release-date", request);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var changed = await admin.PostAsJsonAsync(
                $"/api/v1/admin/notes/{noteId}/correct-release-date", request);
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

            await using var verificationScope = factory.Services.CreateAsyncScope();
            var verify = verificationScope.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.Equal(correctedDate, (await verify.Notes.AsNoTracking()
                .SingleAsync(item => item.Id == noteId)).EventDate);
            var release = await verify.ReleaseObligations.AsNoTracking()
                .Include(item => item.Attestations)
                .SingleAsync(item => item.Id == dhhs.Id);
            Assert.Equal(correctedDate, release.CompletedOn);
            Assert.Equal(2, release.Attestations.Count);
            var retainedClaim = await verify.ClaimLines.AsNoTracking()
                .SingleAsync(item => item.NoteId == noteId);
            Assert.Equal(targetDate, retainedClaim.DateOfService);
            var flag = await verify.FormAttestationChangeReviewFlags.AsNoTracking()
                .SingleAsync(item => item.NoteId == noteId);
            Assert.Equal(dhhs.Id, flag.ReleaseObligationId);
            Assert.Equal(retainedClaim.Id, flag.ClaimLineId);
            Assert.True(flag.RequiresBillingAttention);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await db.FormAttestationChangeReviewFlags.Where(item => item.NoteId == noteId)
                .ExecuteDeleteAsync();
            await db.ClaimLines.Where(item => item.NoteId == noteId).ExecuteDeleteAsync();
            if (createdPeriodId is int periodId)
                await db.BillingPeriods.Where(item => item.Id == periodId).ExecuteDeleteAsync();
        }
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
