using System.Security.Cryptography;
using System.Text.Json;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
namespace Sati.Data;

public sealed class RecordsGovernanceConflictException() : InvalidOperationException(RecordsGovernanceRules.ConflictCode);

/// <summary>Portable writers. Hosts validate current identity and own the serializable transaction.</summary>
public static class RecordsGovernanceWorkflow
{
    public static async Task<RecordsGovernanceState> LockAsync(DbContext db, int agencyId, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null ||
            Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(db.Database.CurrentTransaction).IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Governance writes require an owned serializable transaction.");
        var state = await db.Set<RecordsGovernanceState>().SingleOrDefaultAsync(x => x.AgencyId == agencyId, ct);
        if (state is null) { state = new() { AgencyId = agencyId }; db.Add(state); }
        else db.Entry(state).Property(x => x.Revision).IsModified = true;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new RecordsGovernanceConflictException(); }
        return state;
    }
    public static async Task<IReadOnlyList<GovernanceHoldDto>> HoldsAsync(DbContext db, int agencyId, CancellationToken ct = default)
    {
        var holds = await db.Set<RecordsHold>().AsNoTracking().Where(x => x.AgencyId == agencyId).OrderBy(x => x.Id).ToListAsync(ct);
        var events = await db.Set<RecordsHoldEvent>().AsNoTracking().Where(x => x.AgencyId == agencyId).OrderBy(x => x.Revision).ToListAsync(ct);
        return holds.Select(hold => ToDto(hold, events.Where(e => e.HoldId == hold.Id))).ToArray();
    }
    public static async Task<GovernanceHoldDto> HoldAsync(DbContext db, AgencyActor actor, GovernanceHoldRequest request,
        Func<int, Task<bool>> personExists, Action<string, Guid, int> audit, DateTime nowUtc, CancellationToken ct = default,
        Func<int, string, Task>? releaseLegacy = null)
    {
        RecordsGovernanceRules.RequireAdmin(actor); RecordsGovernanceRules.Validate(request);
        var state = await LockAsync(db, actor.AgencyId, ct);
        var hash = Hash(request);
        var replay = await db.Set<RecordsHoldEvent>().AsNoTracking().SingleOrDefaultAsync(x => x.AgencyId == actor.AgencyId && x.OperationId == request.OperationId, ct);
        if (replay is not null)
        {
            if (replay.ActorId != actor.UserId || replay.RequestHash != hash) throw new RecordsGovernanceConflictException();
            return (await HoldsAsync(db, actor.AgencyId, ct)).Single(x => x.Id == replay.HoldId);
        }
        RecordsHold hold;
        if (request.Action == GovernanceHoldAction.Place)
        {
            if (request.ExpectedRevision != 0) throw new RecordsGovernanceConflictException();
            if (request.PersonId is int person && !await personExists(person)) throw new KeyNotFoundException();
            hold = new() { Id = Guid.NewGuid(), AgencyId = actor.AgencyId, Scope = request.Scope,
                RecordClass = request.RecordClass, PersonId = request.PersonId, RecordId = request.RecordId, PlacedById = actor.UserId };
            db.Add(hold);
        }
        else
        {
            hold = await db.Set<RecordsHold>().SingleOrDefaultAsync(x => x.AgencyId == actor.AgencyId && x.Id == request.HoldId, ct) ?? throw new KeyNotFoundException();
            RecordsGovernanceRules.ValidateTransition(ToDto(hold, []), actor, request);
        }
        hold.Revision++;
        state.Revision++;
        if (request.Action == GovernanceHoldAction.RequestRelease) { hold.ReleaseRequestedById = actor.UserId; hold.ReleaseRequestId = request.OperationId; }
        if (request.Action == GovernanceHoldAction.ApproveRelease)
        {
            if (hold.LegacyHoldId is int legacyId)
            {
                if (releaseLegacy is null) throw new InvalidOperationException("Legacy preservation synchronization is unavailable.");
                await releaseLegacy(legacyId, request.Reason);
            }
            hold.IsReleased = true;
        }
        if (request.Action == GovernanceHoldAction.Amend) { hold.ReleaseRequestedById = null; hold.ReleaseRequestId = null; }
        if (request.Action is GovernanceHoldAction.ApproveRelease or GovernanceHoldAction.RejectRelease) { hold.ReleaseRequestedById = null; hold.ReleaseRequestId = null; }
        db.Add(new RecordsHoldEvent { HoldId = hold.Id, AgencyId = actor.AgencyId, OperationId = request.OperationId,
            RequestHash = hash, Revision = hold.Revision, Action = request.Action, ActorId = actor.UserId,
            RecordedAtUtc = nowUtc, Reason = request.Reason.Trim(), CaseReference = request.CaseReference, IssuedBy = request.IssuedBy });
        audit("records.hold." + request.Action.ToString().ToLowerInvariant(), hold.Id, hold.Revision);
        await db.SaveChangesAsync(ct);
        return (await HoldsAsync(db, actor.AgencyId, ct)).Single(x => x.Id == hold.Id);
    }
    public static async Task<RetentionPolicyDto> PolicyAsync(DbContext db, AgencyActor actor, RetentionPolicyRequest request,
        Action<long, int> audit, DateTime nowUtc, CancellationToken ct = default)
    {
        RecordsGovernanceRules.RequireAdmin(actor);
        RecordsGovernanceRules.Validate(request);
        var state = await LockAsync(db, actor.AgencyId, ct);
        var hash = Hash(request);
        var replay = await db.Set<RecordsRetentionPolicy>().AsNoTracking().SingleOrDefaultAsync(x => x.AgencyId == actor.AgencyId && x.OperationId == request.OperationId, ct);
        if (replay is not null) { if (replay.AuthorId != actor.UserId || replay.RequestHash != hash) throw new RecordsGovernanceConflictException(); return ToDto(replay); }
        var latest = await db.Set<RecordsRetentionPolicy>().Where(x => x.AgencyId == actor.AgencyId && x.RecordClass == request.RecordClass).MaxAsync(x => (int?)x.Version, ct) ?? 0;
        if (latest != request.ExpectedVersion) throw new RecordsGovernanceConflictException();
        var policy = new RecordsRetentionPolicy { AgencyId = actor.AgencyId, OperationId = request.OperationId, RequestHash = hash,
            Version = latest + 1, RecordClass = request.RecordClass, RetentionDays = request.RetentionDays,
            Reason = request.Reason.Trim(), AuthorId = actor.UserId, RecordedAtUtc = nowUtc };
        state.Revision++; db.Add(policy); await db.SaveChangesAsync(ct); audit(policy.Id, policy.Version); await db.SaveChangesAsync(ct);
        return ToDto(policy);
    }
    public static RetentionPolicyDto ToDto(RecordsRetentionPolicy policy) => new(policy.Id, policy.Version, policy.RecordClass,
        policy.RetentionDays, policy.AuthorId, policy.RecordedAtUtc, policy.Reason, "PolicyOnly");
    internal static GovernanceHoldDto ToDto(RecordsHold hold, IEnumerable<RecordsHoldEvent> events) => new(hold.Id, hold.Revision,
        hold.Scope, hold.RecordClass, hold.PersonId, hold.RecordId, hold.IsReleased, hold.PlacedById, hold.ReleaseRequestedById,
        events.Select(e => new GovernanceHoldVersionDto(e.Revision, e.Action, e.ActorId, e.RecordedAtUtc, e.Reason, e.CaseReference, e.IssuedBy)).ToArray());
    internal static string Hash<T>(T request) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
}
