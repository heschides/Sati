using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(BillingCompliancePolicyApiCollection.CollectionName)]
public sealed class BillingComplianceRecoveryApiTests(SatiApiFactory factory)
{
    private static readonly DateTime ServiceDate = new(2010, 8, 3);
    private static readonly DateTime DueDate = new(2010, 7, 31);
    private static readonly DateTime CompletedDate = new(2010, 8, 5);

    [Fact]
    public async Task RecoveryIsAdminOnlyAndDoesNotDiscloseAnotherAgencyConsumer()
    {
        var seeded = await SeedRecoveryScenarioAsync(completed: true, noteCount: 1);
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var otherAgencyAdmin = await factory.CreateAuthenticatedClientAsync("admin-two");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");

        using var denied = await caseManager.GetAsync(
            $"/api/v1/billing/compliance-recovery/{seeded.PersonId}");
        using var foreign = await otherAgencyAdmin.GetAsync(
            $"/api/v1/billing/compliance-recovery/{seeded.PersonId}");
        using var allowed = await admin.GetAsync(
            $"/api/v1/billing/compliance-recovery/{seeded.PersonId}");

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var plan = await allowed.Content.ReadFromJsonAsync<BillingComplianceRecoveryPlan>();
        Assert.Equal(seeded.NoteIds, plan!.NoteOptions.Select(item => item.NoteId));
        Assert.All(plan.NoteOptions, item => Assert.True(item.IsSelectedByDefault));
    }

    [Fact]
    public async Task IncompleteOrUnselectedNotesCannotBeReleased()
    {
        var seeded = await SeedRecoveryScenarioAsync(completed: false, noteCount: 1);
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");

        var plan = await admin.GetFromJsonAsync<BillingComplianceRecoveryPlan>(
            $"/api/v1/billing/compliance-recovery/{seeded.PersonId}");
        Assert.Empty(plan!.NoteOptions);
        Assert.Equal(seeded.NoteIds, plan.UnresolvedNoteIds);

        using var incomplete = await admin.PostAsJsonAsync(
            $"/api/v1/billing/compliance-recovery/{seeded.PersonId}",
            new CreateBillingComplianceRecoveryRequest(
                seeded.NoteIds,
                "Attempting recovery before the PCP is complete.",
                AttestationConfirmed: true));
        using var noneSelected = await admin.PostAsJsonAsync(
            $"/api/v1/billing/compliance-recovery/{seeded.PersonId}",
            new CreateBillingComplianceRecoveryRequest(
                [],
                "No note was selected.",
                AttestationConfirmed: true));

        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noneSelected.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.False(await db.BillingComplianceRecoveryDecisions.AsNoTracking()
            .AnyAsync(item => item.PersonId == seeded.PersonId));
    }

    [Fact]
    public async Task RecoveryStoresExactEvidenceAndReleasesOnlyTheSelectedUnbilledNote()
    {
        var seeded = await SeedRecoveryScenarioAsync(completed: true, noteCount: 2);
        var selectedNoteId = seeded.NoteIds[0];
        var unselectedNoteId = seeded.NoteIds[1];
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var before = await ReadNoteStateAsync(selectedNoteId);

        var plan = await admin.GetFromJsonAsync<BillingComplianceRecoveryPlan>(
            $"/api/v1/billing/compliance-recovery/{seeded.PersonId}");
        Assert.Equal(seeded.NoteIds, plan!.NoteOptions.Select(item => item.NoteId));
        var obligation = Assert.Single(plan.Obligations);
        Assert.Equal($"form:{seeded.FormId}", obligation.ObligationId);
        Assert.Equal($"form-attestation:{seeded.AttestationId}", obligation.EvidenceId);

        using var response = await admin.PostAsJsonAsync(
            $"/api/v1/billing/compliance-recovery/{seeded.PersonId}",
            new CreateBillingComplianceRecoveryRequest(
                [selectedNoteId],
                "The PCP is now complete; release only the checked service note.",
                AttestationConfirmed: true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var decision = await response.Content
            .ReadFromJsonAsync<Sati.Contracts.V1.BillingComplianceRecoveryDecision>();
        Assert.Equal([selectedNoteId], decision!.NoteIds);
        Assert.Equal(obligation, Assert.Single(decision.Obligations));

        var candidates = await admin.GetFromJsonAsync<List<BillingCandidateDto>>(
            "/api/v1/billing/candidates");
        Assert.Empty(Assert.Single(candidates!, item => item.Note.Id == selectedNoteId).Errors);
        Assert.Contains(
            Assert.Single(candidates!, item => item.Note.Id == unselectedNoteId).Errors,
            error => error.Contains("PCP", StringComparison.Ordinal));

        using var selectedClaim = await admin.PostAsJsonAsync(
            "/api/v1/billing/claim-lines",
            new CreateClaimLineRequest(selectedNoteId, false, null));
        using var unselectedClaim = await admin.PostAsJsonAsync(
            "/api/v1/billing/claim-lines",
            new CreateClaimLineRequest(unselectedNoteId, false, null));
        Assert.Equal(HttpStatusCode.OK, selectedClaim.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unselectedClaim.StatusCode);

        var after = await ReadNoteStateAsync(selectedNoteId);
        Assert.Equal(before, after);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var stored = await db.BillingComplianceRecoveryDecisions.AsNoTracking()
            .Include(item => item.Obligations)
            .Include(item => item.Notes)
            .SingleAsync(item => item.DecisionId == decision.DecisionId);
        Assert.Equal([selectedNoteId], stored.Notes.Select(item => item.NoteId));
        Assert.Equal(obligation.EvidenceId, Assert.Single(stored.Obligations).EvidenceId);
        Assert.False((await db.Notes.AsNoTracking()
            .SingleAsync(item => item.Id == selectedNoteId)).ComplianceOverride);
        Assert.Single(await factory.GetAuditEventsAsync(
            "billing-compliance-recovery.recorded"),
            item => item.ResourceId == seeded.PersonId.ToString());
    }

    private async Task<(int PersonId, int FormId, long AttestationId, int[] NoteIds)>
        SeedRecoveryScenarioAsync(bool completed, int noteCount)
    {
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var pcp = await db.Forms.SingleAsync(item =>
            item.PersonId == personId && item.Type == "PCP");
        pcp.DueDate = DueDate;
        pcp.CompletedDate = completed ? CompletedDate : null;

        long attestationId = 0;
        if (completed)
        {
            var attestation = new ServerFormAttestation
            {
                FormId = pcp.Id,
                Kind = "Attested",
                CompletedOn = CompletedDate,
                ActorKind = "CaseManager",
                ActorUserId = 12,
                RecordedAtUtc = new DateTime(2010, 8, 6, 12, 0, 0, DateTimeKind.Utc)
            };
            db.FormAttestations.Add(attestation);
            await db.SaveChangesAsync();
            attestationId = attestation.Id;
        }

        var nextNoteId = await db.Notes.MaxAsync(item => item.Id) + 1;
        var noteIds = Enumerable.Range(nextNoteId, noteCount).ToArray();
        db.Notes.AddRange(noteIds.Select(noteId => new ServerNote
        {
            Id = noteId,
            PersonId = personId,
            AgencyId = 1,
            Narrative = "Synthetic recovery note",
            EventDate = ServiceDate,
            Minutes = 15,
            Status = 6,
            ApprovedById = 13,
            ApprovedAt = new DateTime(2010, 8, 4, 12, 0, 0, DateTimeKind.Utc)
        }));
        await db.SaveChangesAsync();
        return (personId, pcp.Id, attestationId, noteIds);
    }

    private async Task<(int? Status, int Revision, bool ComplianceOverride)> ReadNoteStateAsync(
        int noteId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        return await db.Notes.AsNoTracking()
            .Where(item => item.Id == noteId)
            .Select(item => new ValueTuple<int?, int, bool>(
                item.Status, item.Revision, item.ComplianceOverride))
            .SingleAsync();
    }
}
