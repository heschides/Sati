using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapRecordsGovernance(RouteGroupBuilder api)
    {
        var records = api.MapGroup("/admin/records-governance");
        records.MapGet("/holds", (ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
            GovernanceResult(async () => { var actor = Actor.From(principal); RecordsGovernanceRules.RequireAdmin(actor.ToAgencyActor());
                return await RecordsGovernanceWorkflow.HoldsAsync(db, actor.AgencyId, ct); }));
        records.MapPost("/holds", (GovernanceHoldRequest request, ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, CancellationToken ct) =>
            GovernanceResult(async () => {
                var actor = Actor.From(principal); RecordsGovernanceRules.RequireAdmin(actor.ToAgencyActor());
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await RecordsGovernanceWorkflow.HoldAsync(db, actor.ToAgencyActor(), request,
                    id => db.People.AnyAsync(x => x.Id == id && x.AgencyId == actor.AgencyId, ct),
                    (action, id, revision) => audit.Record(actor, action, "RecordsHold", metadataJson: JsonSerializer.Serialize(new { holdId = id, revision })), DateTime.UtcNow, ct,
                    (id, reason) => LegacyRecordsHoldBridge.CompleteReleaseAsync<ServerLegalHold>(db, actor.ToAgencyActor(), id, reason, DateTime.UtcNow, ct));
                await tx.CommitAsync(ct); return result;
            }));
        records.MapGet("/policies", (ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
            GovernanceResult(async () => {
                var actor = Actor.From(principal); RecordsGovernanceRules.RequireAdmin(actor.ToAgencyActor());
                var policies = await db.Set<RecordsRetentionPolicy>().AsNoTracking().Where(x => x.AgencyId == actor.AgencyId).OrderByDescending(x => x.Id).ToListAsync(ct);
                return policies.Select(RecordsGovernanceWorkflow.ToDto).ToArray();
            }));
        records.MapPost("/policies", (RetentionPolicyRequest request, ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, CancellationToken ct) =>
            GovernanceResult(async () => {
                var actor = Actor.From(principal); RecordsGovernanceRules.RequireAdmin(actor.ToAgencyActor());
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await RecordsGovernanceWorkflow.PolicyAsync(db, actor.ToAgencyActor(), request,
                    (id, version) => audit.Record(actor, "records.policy.created", "RetentionPolicy", metadataJson: JsonSerializer.Serialize(new { policyId = id, version })), DateTime.UtcNow, ct);
                await tx.CommitAsync(ct); return result;
            }));
        records.MapPost("/policies/{policyId:long}/preview", (long policyId, ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, CancellationToken ct) =>
            GovernanceResult(async () => {
                var actor = Actor.From(principal); RecordsGovernanceRules.RequireAdmin(actor.ToAgencyActor());
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var legacy = (await db.LegalHolds.Where(x => x.AgencyId == actor.AgencyId && !x.IsReleased).Select(x => x.PersonId).ToListAsync(ct)).ToHashSet();
                var result = await RecordsRetentionWorkflow.PreviewAsync(db, actor.ToAgencyActor(), policyId, new UnavailableRecordsRetentionStore(), legacy, DateTime.UtcNow, ct);
                audit.Record(actor, "records.retention.previewed", "RetentionPlan", metadataJson: JsonSerializer.Serialize(new { planId = result.Id, result.PolicyId, result.PolicyVersion }));
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
            }));
        records.MapPost("/plans/{planId:guid}/execute", (Guid planId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
            GovernanceResult(async () => {
                var actor = Actor.From(principal); RecordsGovernanceRules.RequireAdmin(actor.ToAgencyActor());
                if (!await db.Set<RecordsRetentionPlan>().AnyAsync(x => x.Id == planId && x.AgencyId == actor.AgencyId, ct)) throw new KeyNotFoundException();
                return new RetentionBatchResult(planId, 0, 0, false, ["runtime_policy_only"]);
            }));
    }
    private static async Task<IResult> GovernanceResult<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [exception.Message] }); }
        catch (InvalidOperationException) { return Results.Conflict(new ApiErrorDto(RecordsGovernanceRules.ConflictCode, "The governance record changed or this decision is not available. Refresh and review it.", "")); }
        catch (DbUpdateException) { return Results.Conflict(new ApiErrorDto(RecordsGovernanceRules.ConflictCode, "A simultaneous governance update was refused. Refresh and review it.", "")); }
    }
}
