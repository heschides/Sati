using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models;
using Xunit;

namespace Sati.Api.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class BillingCompliancePolicyApiCollection : ICollectionFixture<SatiApiFactory>
{
    public const string CollectionName = "Billing compliance policy API";
}

[Collection(BillingCompliancePolicyApiCollection.CollectionName)]
public sealed class BillingCompliancePolicyApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task ExactDateResolutionIsAuthenticatedTenantScopedAndDoesNotExposeHistory()
    {
        using var agencyOne = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var agencyTwo = await factory.CreateAuthenticatedClientAsync("admin-two");
        using var anonymous = factory.CreateAnonymousClient();
        var firstEnforcement = new DateTime(2188, 4, 1);
        var secondEnforcement = new DateTime(2188, 5, 1);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.BillingCompliancePolicyVersions.AddRange(
                BillingCompliancePolicyVersion.Create(
                    1, BillingComplianceRequirements.Pcp,
                    firstEnforcement, firstEnforcement, 11,
                    new DateTime(2188, 4, 1, 12, 0, 0, DateTimeKind.Utc)),
                BillingCompliancePolicyVersion.Create(
                    1, BillingComplianceRequirements.ComprehensiveAssessment,
                    secondEnforcement, secondEnforcement, 11,
                    new DateTime(2188, 5, 1, 12, 0, 0, DateTimeKind.Utc)),
                BillingCompliancePolicyVersion.Create(
                    2, BillingComplianceRequirements.SafetyPlan,
                    firstEnforcement, firstEnforcement, 21,
                    new DateTime(2188, 4, 1, 12, 0, 0, DateTimeKind.Utc)),
                BillingCompliancePolicyVersion.Create(
                    2, BillingComplianceRequirements.PrivacyPractices,
                    secondEnforcement, secondEnforcement, 21,
                    new DateTime(2188, 5, 1, 12, 0, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync();
        }

        const string path =
            "/api/v1/settings/billing-compliance-requirements?serviceDate=";
        var before = await agencyOne.GetFromJsonAsync<
            BillingComplianceRequirementsAtDateDto>(
            path + secondEnforcement.AddDays(-1).ToString("yyyy-MM-dd"));
        using var onResponse = await agencyOne.GetAsync(
            path + secondEnforcement.ToString("yyyy-MM-dd"));
        var payload = await onResponse.Content.ReadAsStringAsync();
        var on = JsonSerializer.Deserialize<BillingComplianceRequirementsAtDateDto>(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var foreign = await agencyTwo.GetFromJsonAsync<
            BillingComplianceRequirementsAtDateDto>(
            path + secondEnforcement.ToString("yyyy-MM-dd"));
        using var denied = await anonymous.GetAsync(
            path + secondEnforcement.ToString("yyyy-MM-dd"));

        Assert.Equal(BillingComplianceRequirements.Pcp, before!.Requirements);
        Assert.Equal(
            BillingComplianceRequirements.ComprehensiveAssessment,
            on!.Requirements);
        Assert.Equal(
            BillingComplianceRequirements.PrivacyPractices,
            foreign!.Requirements);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.DoesNotContain("explanation", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdBy", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImpactPreviewIsAdminOnlyTenantScopedAndReadOnly()
    {
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        int draftNoteId;
        int finalizedNoteId;
        var baselinePolicyChangeId = Guid.NewGuid();
        var appliedPolicyChangeId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.BillingCompliancePolicyVersions.Add(
                BillingCompliancePolicyVersion.Create(
                    1,
                    BillingComplianceRequirements.Pcp,
                    new DateTime(2098, 1, 1),
                    new DateTime(2098, 1, 1),
                    11,
                    new DateTime(2098, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                    changeId: baselinePolicyChangeId));
            var pcp = await db.Forms.SingleAsync(form =>
                form.PersonId == personId && form.Type == "PCP");
            pcp.DueDate = new DateTime(2098, 7, 31);
            pcp.TargetEffectiveDate = new DateTime(2098, 7, 31);
            pcp.CompletedDate = null;

            var nextNoteId = await db.Notes.MaxAsync(note => note.Id) + 1;
            draftNoteId = nextNoteId;
            finalizedNoteId = nextNoteId + 1;
            db.Notes.AddRange(
                new ServerNote
                {
                    Id = draftNoteId,
                    PersonId = personId,
                    AgencyId = 1,
                    Narrative = "Synthetic draft preview note",
                    EventDate = new DateTime(2098, 8, 3),
                    Minutes = 15,
                    Status = NoteWorkflow.ComplianceBlocked
                },
                new ServerNote
                {
                    Id = finalizedNoteId,
                    PersonId = personId,
                    AgencyId = 1,
                    Narrative = "Synthetic finalized preview note",
                    EventDate = new DateTime(2098, 8, 4),
                    Minutes = 15,
                    Status = NoteWorkflow.Approved
                });
            await db.SaveChangesAsync();

            var period = new ServerBillingPeriod
            {
                UserId = 12,
                Month = 8,
                Year = 2098,
                Status = 1,
                SubmittedAt = new DateTime(2098, 9, 1)
            };
            period.Lines.Add(new ServerClaimLine
            {
                NoteId = finalizedNoteId,
                DateOfService = new DateTime(2098, 8, 4),
                ProcedureCode = "T2023",
                Units = 1,
                ChargeAmount = 1,
                ClientMaineCareId = "synthetic",
                RenderingProviderNpi = "1999999984",
                DiagnosisCode = "F89",
                PlaceOfService = 11
            });
            db.BillingPeriods.Add(period);
            await db.SaveChangesAsync();
        }

        var request = new PreviewBillingCompliancePolicyRequest(
            new DateTime(2098, 8, 1),
            BillingComplianceRequirements.None);
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var foreignAdmin = await factory.CreateAuthenticatedClientAsync("admin-two");

        using var denied = await caseManager.PostAsJsonAsync(
            "/api/v1/settings/billing-compliance-policies/preview", request);
        using var response = await admin.PostAsJsonAsync(
            "/api/v1/settings/billing-compliance-policies/preview", request);
        var preview = await response.Content
            .ReadFromJsonAsync<BillingCompliancePolicyImpactPreviewDto>();
        var foreignPreview = await foreignAdmin.PostAsJsonAsync(
            "/api/v1/settings/billing-compliance-policies/preview", request);
        var foreign = await foreignPreview.Content
            .ReadFromJsonAsync<BillingCompliancePolicyImpactPreviewDto>();

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, preview!.DraftOrUnsubmittedNotes.NewlyUnblocked);
        Assert.Equal(1, preview.SubmittedOrFinalizedNotes.NewlyUnblocked);
        Assert.Equal(1, preview.SubmittedOrFinalizedClaimRecords.NewlyUnblocked);
        Assert.Equal(0, foreign!.DraftOrUnsubmittedNotes.TotalAffected);
        Assert.Equal(0, foreign.SubmittedOrFinalizedNotes.TotalAffected);

        var appendRequest = new AppendBillingCompliancePolicyRequest(
            appliedPolicyChangeId,
            request.EffectiveOn,
            request.Requirements,
            "Synthetic future-dated policy review-flag test.");
        using var appliedResponse = await admin.PostAsJsonAsync(
            "/api/v1/settings/billing-compliance-policies", appendRequest);
        var applied = await appliedResponse.Content
            .ReadFromJsonAsync<BillingCompliancePolicyVersionDto>();
        using var replayResponse = await admin.PostAsJsonAsync(
            "/api/v1/settings/billing-compliance-policies", appendRequest);
        var replay = await replayResponse.Content
            .ReadFromJsonAsync<BillingCompliancePolicyVersionDto>();
        Assert.Equal(HttpStatusCode.OK, appliedResponse.StatusCode);
        Assert.NotNull(applied);
        Assert.Equal(applied, replay);

        using var billing = await factory.CreateAuthenticatedClientAsync("billing-only-one");
        var queue = await billing.GetFromJsonAsync<List<BillingCompliancePolicyReviewFlagDto>>(
            "/api/v1/billing/compliance-policy-review-flags");
        using var deniedQueue = await caseManager.GetAsync(
            "/api/v1/billing/compliance-policy-review-flags");
        var foreignQueue = await foreignAdmin.GetFromJsonAsync<List<BillingCompliancePolicyReviewFlagDto>>(
            "/api/v1/billing/compliance-policy-review-flags");
        var appliedFlags = queue!.Where(flag => flag.PolicyVersionId == applied.Id).ToArray();
        Assert.Equal(HttpStatusCode.Forbidden, deniedQueue.StatusCode);
        Assert.DoesNotContain(foreignQueue!, flag => flag.PolicyVersionId == applied.Id);
        Assert.Equal(2, appliedFlags.Length);
        Assert.Single(appliedFlags, flag => flag.ClaimRecordId is null);
        Assert.Single(appliedFlags, flag => flag.ClaimRecordId is not null);
        Assert.All(appliedFlags, flag =>
        {
            Assert.Equal("Unresolved", flag.Status);
            Assert.Contains(flag.PreviousBlockingObligationIds,
                obligationId => obligationId.StartsWith("form:", StringComparison.Ordinal));
            Assert.Contains(flag.PreviousBlockingObligationIds,
                obligationId => obligationId.StartsWith("missing-form:PCP:", StringComparison.Ordinal));
            Assert.Empty(flag.NewBlockingObligationIds);
        });

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Single(await verify.BillingCompliancePolicyVersions.AsNoTracking()
            .Where(version => version.VersionId == baselinePolicyChangeId)
            .ToListAsync());
        Assert.Single(await verify.BillingCompliancePolicyVersions.AsNoTracking()
            .Where(version => version.VersionId == appliedPolicyChangeId)
            .ToListAsync());
        Assert.Equal(2, await verify.BillingCompliancePolicyReviewFlags.AsNoTracking()
            .CountAsync(flag => flag.PolicyVersionId == applied!.Id));
        Assert.Equal(NoteWorkflow.Pending,
            (await verify.Notes.AsNoTracking().SingleAsync(note => note.Id == draftNoteId)).Status);
        Assert.Equal(NoteWorkflow.Approved,
            (await verify.Notes.AsNoTracking().SingleAsync(note => note.Id == finalizedNoteId)).Status);
    }

    [Fact]
    public async Task SupervisorQueueResolvesPolicyFromEachNotesServiceDate()
    {
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        int earlierNoteId;
        int laterNoteId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var pcp = await db.Forms.SingleAsync(form =>
                form.PersonId == personId && form.Type == "PCP");
            pcp.DueDate = new DateTime(2026, 7, 1);
            pcp.CompletedDate = null;

            db.BillingCompliancePolicyVersions.AddRange(
                BillingCompliancePolicyVersion.Create(
                    1, BillingComplianceRequirements.Pcp,
                    new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), 11,
                    new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                    changeId: Guid.NewGuid()),
                BillingCompliancePolicyVersion.Create(
                    1, BillingComplianceRequirements.ComprehensiveAssessment,
                    new DateTime(2026, 8, 1), new DateTime(2026, 8, 1), 11,
                    new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
                    changeId: Guid.NewGuid()));

            var nextId = await db.Notes.MaxAsync(note => note.Id) + 1;
            earlierNoteId = nextId;
            laterNoteId = nextId + 1;
            db.Notes.AddRange(
                new ServerNote
                {
                    Id = earlierNoteId,
                    PersonId = personId,
                    AgencyId = 1,
                    Narrative = "Earlier policy note",
                    EventDate = new DateTime(2026, 7, 15),
                    Minutes = 15,
                    Status = NoteWorkflow.Logged
                },
                new ServerNote
                {
                    Id = laterNoteId,
                    PersonId = personId,
                    AgencyId = 1,
                    Narrative = "Later policy note",
                    EventDate = new DateTime(2026, 8, 3),
                    Minutes = 15,
                    Status = NoteWorkflow.Logged
                });
            await db.SaveChangesAsync();
        }

        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var page = await supervisor.GetFromJsonAsync<NoteReviewPage<NoteDto>>(
            $"/api/v1/supervisor/notes/page?personId={personId}");

        var earlier = Assert.Single(page!.Notes, note => note.Id == earlierNoteId);
        var later = Assert.Single(page.Notes, note => note.Id == laterNoteId);
        Assert.Contains(earlier.ComplianceFailureReasons!,
            reason => reason.Contains("PCP", StringComparison.Ordinal));
        Assert.Empty(later.ComplianceFailureReasons!);
    }

    [Fact]
    public async Task PolicyHistoryIsAdminOnlyEffectiveDatedIdempotentAndTenantScoped()
    {
        // This collection deliberately shares one in-memory API. The service-date
        // test appends its own synthetic versions, so reset this test-owned table
        // before asserting the fresh agency default. Production append-only guards
        // are not bypassed; this is the disposable SQLite fixture only.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM BillingCompliancePolicyReviewFlags");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM BillingCompliancePolicyVersions");
        }
        var priorPolicyAuditCount = (await factory.GetAuditEventsAsync(
            "billing-compliance-policy.appended")).Count;

        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var otherAgencyAdmin = await factory.CreateAuthenticatedClientAsync("admin-two");
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await caseManager.GetAsync("/api/v1/settings/billing-compliance-policies")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await caseManager.PostAsJsonAsync(
                "/api/v1/settings/billing-compliance-policies",
                new AppendBillingCompliancePolicyRequest(
                    Guid.NewGuid(), new DateTime(2099, 1, 1),
                    BillingComplianceRequirements.Pcp))).StatusCode);

        var originalSettings = await admin.GetFromJsonAsync<SettingsDto>("/api/v1/settings");
        Assert.NotNull(originalSettings);
        Assert.False(originalSettings.AllowPastBillingPolicyEffectiveDates);
        Assert.Equal(BillingComplianceGate.DefaultRequirements,
            originalSettings.BillingComplianceRequirements);

        var futureDate = new DateTime(2099, 1, 1);
        var futureRequest = new AppendBillingCompliancePolicyRequest(
            Guid.NewGuid(),
            futureDate,
            BillingComplianceRequirements.PcpOpening | BillingComplianceRequirements.Pcp);
        var futureResponse = await admin.PostAsJsonAsync(
            "/api/v1/settings/billing-compliance-policies", futureRequest);
        var future = await futureResponse.Content
            .ReadFromJsonAsync<BillingCompliancePolicyVersionDto>();
        Assert.Equal(HttpStatusCode.OK, futureResponse.StatusCode);
        Assert.NotNull(future);

        // Replaying the same logical write returns its first row and audit result.
        var replayResponse = await admin.PostAsJsonAsync(
            "/api/v1/settings/billing-compliance-policies", futureRequest);
        var replay = await replayResponse.Content
            .ReadFromJsonAsync<BillingCompliancePolicyVersionDto>();
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(future, replay);

        var reusedId = await admin.PostAsJsonAsync(
            "/api/v1/settings/billing-compliance-policies",
            futureRequest with { Requirements = BillingComplianceRequirements.None });
        Assert.Equal(HttpStatusCode.Conflict, reusedId.StatusCode);

        // A correction on the same date is retained as a second row; greater Id wins.
        var correctionResponse = await admin.PostAsJsonAsync(
            "/api/v1/settings/billing-compliance-policies",
            new AppendBillingCompliancePolicyRequest(
                Guid.NewGuid(), futureDate, BillingComplianceRequirements.SafetyPlan));
        var correction = await correctionResponse.Content
            .ReadFromJsonAsync<BillingCompliancePolicyVersionDto>();
        Assert.Equal(HttpStatusCode.OK, correctionResponse.StatusCode);
        Assert.True(correction!.Id > future!.Id);

        var agencyOneHistory = await admin.GetFromJsonAsync<List<BillingCompliancePolicyVersionDto>>(
            "/api/v1/settings/billing-compliance-policies");
        var agencyTwoHistory = await otherAgencyAdmin.GetFromJsonAsync<List<BillingCompliancePolicyVersionDto>>(
            "/api/v1/settings/billing-compliance-policies");
        Assert.Equal([correction.Id, future.Id], agencyOneHistory!.Select(item => item.Id));
        Assert.Empty(agencyTwoHistory!);

        var rejectedPast = await admin.PostAsJsonAsync(
            "/api/v1/settings/billing-compliance-policies",
            new AppendBillingCompliancePolicyRequest(
                Guid.NewGuid(), new DateTime(2020, 1, 1), BillingComplianceRequirements.Pcp));
        Assert.Equal(HttpStatusCode.BadRequest, rejectedPast.StatusCode);

        var enabledResponse = await admin.PutAsJsonAsync(
            "/api/v1/settings",
            originalSettings with { AllowPastBillingPolicyEffectiveDates = true });
        Assert.Equal(HttpStatusCode.OK, enabledResponse.StatusCode);

        var pastWithoutReason = await admin.PostAsJsonAsync(
            "/api/v1/settings/billing-compliance-policies",
            new AppendBillingCompliancePolicyRequest(
                Guid.NewGuid(), new DateTime(2020, 1, 1), BillingComplianceRequirements.Pcp));
        Assert.Equal(HttpStatusCode.BadRequest, pastWithoutReason.StatusCode);

        var pastResponse = await admin.PostAsJsonAsync(
            "/api/v1/settings/billing-compliance-policies",
            new AppendBillingCompliancePolicyRequest(
                Guid.NewGuid(), new DateTime(2020, 1, 1),
                BillingComplianceRequirements.ComprehensiveAssessment,
                "Correcting the enforcement date recorded in error."));
        Assert.Equal(HttpStatusCode.OK, pastResponse.StatusCode);

        var currentSettings = await admin.GetFromJsonAsync<SettingsDto>("/api/v1/settings");
        Assert.Equal(
            BillingComplianceRequirements.ComprehensiveAssessment,
            currentSettings!.BillingComplianceRequirements);

        var audits = await factory.GetAuditEventsAsync("billing-compliance-policy.appended");
        Assert.Equal(priorPolicyAuditCount + 3, audits.Count);
        Assert.All(audits.TakeLast(3), audit =>
        {
            Assert.Equal(1, audit.AgencyId);
            Assert.Equal(11, audit.ActorUserId);
            Assert.Equal("BillingCompliancePolicyVersion", audit.ResourceType);
        });
    }
}
