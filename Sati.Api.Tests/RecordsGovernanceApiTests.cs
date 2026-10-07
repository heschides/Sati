using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models;
using Xunit;
namespace Sati.Api.Tests;

public sealed class RecordsGovernanceApiTests
{
    private const string Root = "/api/v1/admin/records-governance";
    [Theory]
    [InlineData("case-manager-one")][InlineData("supervisor-one")][InlineData("billing-only-one")]
    public async Task NonAdministratorsCannotDiscoverOrWriteGovernance(string username)
    {
        await using var factory = new SatiApiFactory(); using var client = await factory.CreateAuthenticatedClientAsync(username);
        using var holds = await client.GetAsync(Root + "/holds"); Assert.Equal(HttpStatusCode.Forbidden, holds.StatusCode);
        using var place = await client.PostAsJsonAsync(Root + "/holds", Place()); Assert.Equal(HttpStatusCode.Forbidden, place.StatusCode);
        using var policy = await client.PostAsJsonAsync(Root + "/policies", Policy()); Assert.Equal(HttpStatusCode.Forbidden, policy.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Empty(await db.Set<RecordsHold>().ToListAsync()); Assert.Empty(await db.Set<RecordsRetentionPolicy>().ToListAsync());
    }
    [Fact]
    public async Task TenantScopeRevisionsExactReplayAndIndependentApprovalAreEnforced()
    {
        await using var factory = new SatiApiFactory(); using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var other = await factory.CreateAuthenticatedClientAsync("admin-two"); using var reviewer = await factory.CreateAuthenticatedClientAsync("admin-without-billing-one");
        var request = Place(); var hold = await Act(admin, request); var replay = await Act(admin, request);
        Assert.Equal(hold.Id, replay.Id); Assert.Equal(hold.Revision, replay.Revision);
        using var foreign = await other.PostAsJsonAsync(Root + "/holds", Change(hold, GovernanceHoldAction.Amend)); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<List<GovernanceHoldDto>>(Root + "/holds"))!);
        using var foreignPerson = await admin.PostAsJsonAsync(Root + "/holds", Place() with { Scope = PreservationScope.Person, PersonId = 201 });
        Assert.Equal(HttpStatusCode.NotFound, foreignPerson.StatusCode);
        var pending = await Act(admin, Change(hold, GovernanceHoldAction.RequestRelease));
        using var self = await admin.PostAsJsonAsync(Root + "/holds", Change(pending, GovernanceHoldAction.ApproveRelease)); Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        using var stale = await reviewer.PostAsJsonAsync(Root + "/holds", Change(hold, GovernanceHoldAction.ApproveRelease)); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(RecordsGovernanceRules.ConflictCode, (await stale.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        var approved = await Act(reviewer, Change(pending, GovernanceHoldAction.ApproveRelease)); Assert.True(approved.IsReleased);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Equal(3, await db.Set<RecordsHoldEvent>().CountAsync());
        var audits = await db.AuditEvents.Where(x => x.Action.StartsWith("records.hold.")).ToArrayAsync(); Assert.Equal(3, audits.Length);
        Assert.All(audits, x => { Assert.DoesNotContain("Synthetic restricted reason", x.MetadataJson); Assert.Contains("revision", x.MetadataJson); });
    }
    [Fact]
    public async Task PreviewIsScopedExplicitlyUnknownAndExecutionStaysDisabled()
    {
        await using var factory = new SatiApiFactory(); using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var other = await factory.CreateAuthenticatedClientAsync("admin-two"); var request = Policy();
        using var response = await admin.PostAsJsonAsync(Root + "/policies", request); response.EnsureSuccessStatusCode();
        var policy = (await response.Content.ReadFromJsonAsync<RetentionPolicyDto>())!; Assert.Equal("PolicyOnly", policy.EnforcementMode);
        using var retry = await admin.PostAsJsonAsync(Root + "/policies", request); retry.EnsureSuccessStatusCode();
        Assert.Equal(policy.Id, (await retry.Content.ReadFromJsonAsync<RetentionPolicyDto>())!.Id);
        using var previewResponse = await admin.PostAsync($"{Root}/policies/{policy.Id}/preview", null); previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<RetentionPreviewDto>())!; Assert.Null(preview.CandidateCount); Assert.False(preview.CanExecute);
        using var foreign = await other.PostAsync($"{Root}/policies/{policy.Id}/preview", null); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var foreignExecute = await other.PostAsync($"{Root}/plans/{preview.Id}/execute", null); Assert.Equal(HttpStatusCode.NotFound, foreignExecute.StatusCode);
        using var execute = await admin.PostAsync($"{Root}/plans/{preview.Id}/execute", null); execute.EnsureSuccessStatusCode();
        Assert.Contains("runtime_policy_only", (await execute.Content.ReadFromJsonAsync<RetentionBatchResult>())!.Blockers);
    }
    [Fact]
    public async Task OldPersonHoldRouteRequiresIndependentApprovalAndKeepsLegacyHistory()
    {
        await using var factory = new SatiApiFactory(); using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var reviewer = await factory.CreateAuthenticatedClientAsync("admin-without-billing-one");
        using var placement = await admin.PostAsJsonAsync("/api/v1/admin/legal-holds", new PlaceLegalHoldRequest(101, "Original restricted reason", "Synthetic reference", null, DateTime.UtcNow));
        placement.EnsureSuccessStatusCode(); var original = (await placement.Content.ReadFromJsonAsync<LegalHoldDto>())!;
        using var request = await admin.PostAsJsonAsync($"/api/v1/admin/legal-holds/{original.Id}/release", new ReleaseLegalHoldRequest("Synthetic independent review requested"));
        request.EnsureSuccessStatusCode(); Assert.False((await request.Content.ReadFromJsonAsync<LegalHoldDto>())!.IsReleased);
        var pending = (await admin.GetFromJsonAsync<List<GovernanceHoldDto>>(Root + "/holds"))!.Single();
        Assert.Equal("Original restricted reason", pending.History[0].Reason); Assert.Equal(101, pending.PersonId);
        await Act(reviewer, Change(pending, GovernanceHoldAction.ApproveRelease));
        var legacy = (await admin.GetFromJsonAsync<List<LegalHoldDto>>("/api/v1/admin/legal-holds?personId=101"))!.Single();
        Assert.True(legacy.IsReleased); Assert.Equal(16, legacy.ReleasedByUserId); Assert.Equal(original.Reason, legacy.Reason);
    }
    private static GovernanceHoldRequest Place() => new(Guid.NewGuid(), GovernanceHoldAction.Place, null, 0,
        PreservationScope.Agency, null, null, null, "Synthetic restricted reason");
    private static GovernanceHoldRequest Change(GovernanceHoldDto hold, GovernanceHoldAction action) => new(Guid.NewGuid(), action,
        hold.Id, hold.Revision, hold.Scope, hold.RecordClass, hold.PersonId, hold.RecordId, "Synthetic independent decision");
    private static RetentionPolicyRequest Policy() => new(Guid.NewGuid(), RetentionRecordClass.Clinical, 0, null, "Proposed preservation; no legal period invented");
    private static async Task<GovernanceHoldDto> Act(HttpClient client, GovernanceHoldRequest request)
    { using var response = await client.PostAsJsonAsync(Root + "/holds", request); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<GovernanceHoldDto>())!; }
}
