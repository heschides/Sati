using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
namespace Sati.Data;

/// <summary>Each adapter must use the caller's DbContext/transaction; remote deletion requires a separately reviewed outbox adapter.</summary>
public interface IRecordsRetentionStore
{
    Task<RetentionStoreInventory> InventoryAsync(DbContext db, int agencyId, CancellationToken ct);
    Task<PreservationReceipt?> PreservationAsync(DbContext db, Guid planId, CancellationToken ct);
    Task DeleteAsync(DbContext db, IReadOnlyList<RetentionCandidate> candidates, CancellationToken ct);
}

public sealed class UnavailableRecordsRetentionStore : IRecordsRetentionStore
{
    public Task<RetentionStoreInventory> InventoryAsync(DbContext db, int agencyId, CancellationToken ct) =>
        Task.FromResult(new RetentionStoreInventory(false, false, [], ["storage_dependency_adapter_unavailable"]));
    public Task<PreservationReceipt?> PreservationAsync(DbContext db, Guid planId, CancellationToken ct) => Task.FromResult<PreservationReceipt?>(null);
    public Task DeleteAsync(DbContext db, IReadOnlyList<RetentionCandidate> candidates, CancellationToken ct) =>
        throw new InvalidOperationException("No destructive adapter is activated.");
}

public static class RecordsRetentionWorkflow
{
    private sealed record PreparedInventory(int Version, RetentionCandidate[] Candidates, RetentionCandidate[] Records);
    public static async Task<RetentionPreviewDto> PreviewAsync(DbContext db, AgencyActor actor, long policyId,
        IRecordsRetentionStore store, IReadOnlySet<int> legacyHeldPeople, DateTime nowUtc, CancellationToken ct = default)
    {
        RecordsGovernanceRules.RequireAdmin(actor);
        var state = await RecordsGovernanceWorkflow.LockAsync(db, actor.AgencyId, ct);
        var policy = await db.Set<RecordsRetentionPolicy>().SingleOrDefaultAsync(x => x.Id == policyId && x.AgencyId == actor.AgencyId, ct) ?? throw new KeyNotFoundException();
        var holds = await RecordsGovernanceWorkflow.HoldsAsync(db, actor.AgencyId, ct);
        var inventory = await store.InventoryAsync(db, actor.AgencyId, ct);
        var blockers = inventory.Blockers.ToList();
        if (!inventory.Available || !inventory.Complete) blockers.Add("dependency_inventory_unavailable_or_incomplete");
        if (inventory.Candidates.Count > RecordsGovernanceRules.MaximumPreview) blockers.Add("preview_inventory_exceeds_bound");
        var cutoff = policy.RetentionDays is int days ? nowUtc.AddDays(-days) : (DateTime?)null;
        if (cutoff is null) blockers.Add("policy_preserves_indefinitely");
        var records = inventory.Candidates.Take(RecordsGovernanceRules.MaximumPreview)
            .GroupBy(x => (x.Record.RecordClass, x.Record.RecordId)).ToDictionary(x => x.Key, x => x.First());
        if (records.Count != inventory.Candidates.Count) blockers.Add("duplicate_or_unbounded_dependency_identity");
        var eligible = records.Values.Where(x => x.Record.RecordClass == policy.RecordClass && cutoff is not null && x.Record.RecordedAtUtc < cutoff).ToArray();
        var clear = new List<RetentionCandidate>(); var held = 0; var unavailable = 0;
        foreach (var candidate in eligible)
        {
            var status = RecordsGovernanceRules.Evaluate(candidate, records, holds, legacyHeldPeople);
            if (status == LegalHoldStatus.Active) held++;
            else if (status == LegalHoldStatus.Unavailable) { unavailable++; blockers.Add("required_dependency_unavailable"); }
            else clear.Add(candidate);
        }
        blockers.Add("runtime_policy_only");
        var known = inventory.Available && inventory.Complete && inventory.Candidates.Count <= RecordsGovernanceRules.MaximumPreview && records.Count == inventory.Candidates.Count;
        var preview = new RetentionPreviewDto(Guid.NewGuid(), policy.Id, policy.Version, state.Revision, policy.RecordClass,
            nowUtc, cutoff, known ? eligible.Length : null, known ? held : null, known ? eligible.Select(x => (DateTime?)x.Record.RecordedAtUtc).Min() : null,
            known ? eligible.Select(x => (DateTime?)x.Record.RecordedAtUtc).Max() : null,
            known ? clear.Count : null, known ? unavailable : null, known ? records.Values.Sum(x => x.Dependencies.Count) : null,
            blockers.Distinct().ToArray(), false, "PolicyOnly");
        db.Add(new RecordsRetentionPlan { Id = preview.Id, AgencyId = actor.AgencyId, PolicyId = policy.Id, GovernanceRevision = state.Revision,
            PreparedAtUtc = nowUtc, PreviewJson = JsonSerializer.Serialize(preview),
            CandidatesJson = JsonSerializer.Serialize(new PreparedInventory(1, clear.ToArray(), records.Values.ToArray())), Revision = 1 });
        await db.SaveChangesAsync(ct); return preview;
    }
    public static async Task<RetentionBatchResult> ExecuteBatchAsync(DbContext db, AgencyActor actor, Guid planId,
        Guid operationId, int expectedCheckpoint, IRecordsRetentionStore store, IReadOnlySet<int> legacyHeldPeople,
        RetentionExecutionMode mode, bool identityVerifiedSyntheticFixture, DateTime nowUtc, CancellationToken ct = default)
    {
        RecordsGovernanceRules.RequireAdmin(actor);
        // No runtime caller supplies fixture identity; API/local adapters always use PolicyOnly.
        if (!RecordsGovernanceRules.CanExecute(mode, identityVerifiedSyntheticFixture) || !IsOwnedSyntheticDatabase(db))
            return new(planId, 0, expectedCheckpoint, false, ["runtime_policy_only"]);
        if (operationId == Guid.Empty) throw new ArgumentException("Supply a batch operation identity.");
        var state = await RecordsGovernanceWorkflow.LockAsync(db, actor.AgencyId, ct);
        var plan = await db.Set<RecordsRetentionPlan>().SingleOrDefaultAsync(x => x.Id == planId && x.AgencyId == actor.AgencyId, ct) ?? throw new KeyNotFoundException();
        var replay = await db.Set<RecordsRetentionBatch>().SingleOrDefaultAsync(x => x.AgencyId == actor.AgencyId && x.OperationId == operationId, ct);
        if (replay is not null)
        {
            if (replay.PlanId != planId || replay.Checkpoint != expectedCheckpoint || replay.ActorId != actor.UserId) throw new RecordsGovernanceConflictException();
            return new(planId, replay.DeletedCount, replay.Checkpoint + replay.DeletedCount, replay.Completed, []);
        }
        if (plan.Checkpoint != expectedCheckpoint || plan.Completed) throw new RecordsGovernanceConflictException();
        var preview = JsonSerializer.Deserialize<RetentionPreviewDto>(plan.PreviewJson)!;
        if (preview.Blockers.Any(x => x != "runtime_policy_only")) return new(planId, 0, plan.Checkpoint, false, preview.Blockers);
        // A placement/release/policy write since preparation invalidates the entire plan.
        // Replay and preview reads acquire the same lock without changing the preservation epoch.
        if (state.Revision != plan.GovernanceRevision) return new(planId, 0, plan.Checkpoint, false, ["governance_changed_reprepare"]);
        var latest = await db.Set<RecordsRetentionPolicy>().Where(x => x.AgencyId == actor.AgencyId && x.RecordClass == preview.RecordClass).MaxAsync(x => x.Version, ct);
        if (latest != preview.PolicyVersion) return new(planId, 0, plan.Checkpoint, false, ["policy_changed_reprepare"]);
        var receipt = await store.PreservationAsync(db, planId, ct);
        if (receipt is null || !RecordsGovernanceRules.ValidReceipt(receipt, planId, state.Revision) || receipt.RecordedAtUtc > nowUtc)
            return new(planId, 0, plan.Checkpoint, false, ["backup_object_recovery_evidence_unavailable"]);
        var inventory = await store.InventoryAsync(db, actor.AgencyId, ct);
        if (!inventory.Available || !inventory.Complete || inventory.Blockers.Count != 0 ||
            inventory.Candidates.Count > RecordsGovernanceRules.MaximumPreview ||
            inventory.Candidates.GroupBy(x => (x.Record.RecordClass, x.Record.RecordId)).Any(x => x.Count() != 1))
            return new(planId, 0, plan.Checkpoint, false, ["dependency_inventory_unavailable_or_incomplete"]);
        var records = inventory.Candidates.ToDictionary(x => (x.Record.RecordClass, x.Record.RecordId));
        var holds = await RecordsGovernanceWorkflow.HoldsAsync(db, actor.AgencyId, ct);
        var prepared = JsonSerializer.Deserialize<PreparedInventory>(plan.CandidatesJson)!;
        if (prepared.Version != 1) return new(planId, 0, plan.Checkpoint, false, ["unsupported_plan_version"]);
        var candidates = prepared.Candidates;
        var committed = candidates.Take(plan.Checkpoint).Select(x => (x.Record.RecordClass, x.Record.RecordId)).ToHashSet();
        var remaining = prepared.Records.Where(x => !committed.Contains((x.Record.RecordClass, x.Record.RecordId))).ToArray();
        // A new copy or changed linked record must invalidate the plan as well as a changed root.
        if (remaining.Length != records.Count || remaining.Any(item =>
            !records.TryGetValue((item.Record.RecordClass, item.Record.RecordId), out var current) ||
            current.Record != item.Record || !SameDependencies(current, item)))
            return new(planId, 0, plan.Checkpoint, false, ["record_changed_or_preserved"]);
        var batch = candidates.Skip(plan.Checkpoint).Take(RecordsGovernanceRules.MaximumBatch).ToArray();
        foreach (var item in batch)
            if (!records.TryGetValue((item.Record.RecordClass, item.Record.RecordId), out var current) ||
                current.Record.Fingerprint != item.Record.Fingerprint ||
                !SameDependencies(current, item) ||
                RecordsGovernanceRules.Evaluate(current, records, holds, legacyHeldPeople) != LegalHoldStatus.Clear)
                return new(planId, 0, plan.Checkpoint, false, ["record_changed_or_preserved"]);
        await store.DeleteAsync(db, batch, ct);
        db.Add(new RecordsRetentionBatch { PlanId = planId, AgencyId = actor.AgencyId, OperationId = operationId,
            ActorId = actor.UserId, Checkpoint = plan.Checkpoint, DeletedCount = batch.Length, RecordedAtUtc = nowUtc,
            Completed = plan.Checkpoint + batch.Length == candidates.Length, PreservationJson = JsonSerializer.Serialize(receipt) });
        state.Revision++;
        plan.Checkpoint += batch.Length; plan.Revision++; plan.GovernanceRevision = state.Revision; plan.Completed = plan.Checkpoint == candidates.Length;
        await db.SaveChangesAsync(ct);
        return new(planId, batch.Length, plan.Checkpoint, plan.Completed, []);
    }
    private static bool SameDependencies(RetentionCandidate first, RetentionCandidate second) =>
        first.Dependencies.OrderBy(x => x.RecordClass).ThenBy(x => x.RecordId)
            .SequenceEqual(second.Dependencies.OrderBy(x => x.RecordClass).ThenBy(x => x.RecordId));
    private static bool IsOwnedSyntheticDatabase(DbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            // SQLite reports an empty physical DataSource for in-memory connections.
            var settings = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connection.ConnectionString };
            return settings.TryGetValue("Data Source", out var source) && source is string value && value == ":memory:";
        }
        return db.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer" &&
            Regex.IsMatch(connection.DataSource, @"\A\(localdb\)\\SatiSqlTests_[0-9a-f]{32}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            Regex.IsMatch(connection.Database, @"\ASatiSyntheticPipeline_[0-9a-f]{32}\z", RegexOptions.CultureInvariant);
    }
}
