using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
namespace Sati.Data;
public sealed class RecordsGovernanceService(IDbContextFactory<SatiContext> factory, ISessionService session) : IRecordsGovernanceService
{
    public async Task<IReadOnlyList<GovernanceHoldDto>> GetHoldsAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext(); var actor = await LocalTenantAccess.EnsureSessionAsync(db, session);
        RecordsGovernanceRules.RequireAdmin(actor.ToAgencyActor()); return await RecordsGovernanceWorkflow.HoldsAsync(db, actor.AgencyId, ct);
    }
    public async Task<GovernanceHoldDto> ChangeHoldAsync(GovernanceHoldRequest request, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = await LocalTenantAccess.EnsureSessionAsync(db, session);
        var result = await RecordsGovernanceWorkflow.HoldAsync(db, actor.ToAgencyActor(), request,
            person => db.People.AnyAsync(p => p.Id == person && p.AgencyId == actor.AgencyId, ct),
            (action, id, revision) => LocalAuditTrail.Record(db, actor, action, "RecordsHold", metadataJson: JsonSerializer.Serialize(new { holdId = id, revision })), DateTime.UtcNow, ct,
            (id, reason) => LegacyRecordsHoldBridge.CompleteReleaseAsync<LegalHold>(db, actor.ToAgencyActor(), id, reason, DateTime.UtcNow, ct));
        await tx.CommitAsync(ct); return result;
    }
    public async Task<IReadOnlyList<RetentionPolicyDto>> GetPoliciesAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext(); var actor = await LocalTenantAccess.EnsureSessionAsync(db, session);
        RecordsGovernanceRules.RequireAdmin(actor.ToAgencyActor());
        var policies = await db.Set<RecordsRetentionPolicy>().AsNoTracking().Where(x => x.AgencyId == actor.AgencyId).OrderByDescending(x => x.Id).ToListAsync(ct);
        return policies.Select(RecordsGovernanceWorkflow.ToDto).ToArray();
    }
    public async Task<RetentionPolicyDto> SavePolicyAsync(RetentionPolicyRequest request, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = await LocalTenantAccess.EnsureSessionAsync(db, session);
        var result = await RecordsGovernanceWorkflow.PolicyAsync(db, actor.ToAgencyActor(), request,
            (id, version) => LocalAuditTrail.Record(db, actor, "records.policy.created", "RetentionPolicy", metadataJson: JsonSerializer.Serialize(new { policyId = id, version })), DateTime.UtcNow, ct);
        await tx.CommitAsync(ct); return result;
    }
    public async Task<RetentionPreviewDto> PreviewAsync(long policyId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = await LocalTenantAccess.EnsureSessionAsync(db, session);
        var legacy = (await db.LegalHolds.Where(x => x.AgencyId == actor.AgencyId && !x.IsReleased).Select(x => x.PersonId).ToListAsync(ct)).ToHashSet();
        var result = await RecordsRetentionWorkflow.PreviewAsync(db, actor.ToAgencyActor(), policyId, new UnavailableRecordsRetentionStore(), legacy, DateTime.UtcNow, ct);
        LocalAuditTrail.Record(db, actor, "records.retention.previewed", "RetentionPlan", metadataJson: JsonSerializer.Serialize(new { planId = result.Id, result.PolicyId, result.PolicyVersion }));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
    }
}
