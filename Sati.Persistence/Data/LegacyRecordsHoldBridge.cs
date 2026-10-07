using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
namespace Sati.Data;

public static class LegacyRecordsHoldBridge
{
    public static async Task RequestReleaseAsync<TLegacy>(DbContext db, AgencyActor actor, int legacyId,
        string? reason, Action<string, Guid, int> audit, DateTime nowUtc, CancellationToken ct = default) where TLegacy : class
    {
        RecordsGovernanceRules.RequireAdmin(actor);
        await RecordsGovernanceWorkflow.LockAsync(db, actor.AgencyId, ct);
        var hold = await ImportActiveAsync<TLegacy>(db, actor, legacyId, ct);
        var request = new GovernanceHoldRequest(Guid.NewGuid(), GovernanceHoldAction.RequestRelease, hold.Id, hold.Revision,
            hold.Scope, hold.RecordClass, hold.PersonId, hold.RecordId,
            string.IsNullOrWhiteSpace(reason) ? "Release requested; independent approval required." : reason);
        await RecordsGovernanceWorkflow.HoldAsync(db, actor, request, _ => Task.FromResult(true), audit, nowUtc, ct);
    }
    public static async Task<RecordsHold> ImportActiveAsync<TLegacy>(DbContext db, AgencyActor actor, int legacyId,
        CancellationToken ct = default) where TLegacy : class
    {
        RecordsGovernanceRules.RequireAdmin(actor);
        await RecordsGovernanceWorkflow.LockAsync(db, actor.AgencyId, ct);
        var legacy = await db.Set<TLegacy>().SingleOrDefaultAsync(x => EF.Property<int>(x, "Id") == legacyId &&
            EF.Property<int>(x, "AgencyId") == actor.AgencyId, ct) ?? throw new KeyNotFoundException();
        var entry = db.Entry(legacy);
        if (entry.Property<bool>("IsReleased").CurrentValue) throw new InvalidOperationException("The hold was already released.");
        var hold = await db.Set<RecordsHold>().SingleOrDefaultAsync(x => x.AgencyId == actor.AgencyId && x.LegacyHoldId == legacyId, ct);
        if (hold is null)
        {
            hold = new RecordsHold { Id = Guid.NewGuid(), AgencyId = actor.AgencyId, LegacyHoldId = legacyId,
                PersonId = entry.Property<int>("PersonId").CurrentValue, Scope = PreservationScope.Person,
                PlacedById = entry.Property<int>("PlacedByUserId").CurrentValue, Revision = 1 };
            db.Add(hold);
            db.Add(new RecordsHoldEvent { HoldId = hold.Id, AgencyId = actor.AgencyId, OperationId = Guid.NewGuid(),
                RequestHash = new string('0',64), Revision = 1, Action = GovernanceHoldAction.Place, ActorId = hold.PlacedById,
                RecordedAtUtc = entry.Property<DateTime>("PlacedAtUtc").CurrentValue,
                Reason = entry.Property<string>("Reason").CurrentValue,
                CaseReference = entry.Property<string?>("CaseReference").CurrentValue,
                IssuedBy = entry.Property<string?>("IssuedBy").CurrentValue });
            await db.SaveChangesAsync(ct);
        }
        return hold;
    }
    public static async Task CompleteReleaseAsync<TLegacy>(DbContext db, AgencyActor actor, int legacyId,
        string reason, DateTime nowUtc, CancellationToken ct = default) where TLegacy : class
    {
        var legacy = await db.Set<TLegacy>().SingleOrDefaultAsync(x => EF.Property<int>(x, "Id") == legacyId &&
            EF.Property<int>(x, "AgencyId") == actor.AgencyId, ct) ?? throw new KeyNotFoundException();
        var entry = db.Entry(legacy);
        entry.Property<bool>("IsReleased").CurrentValue = true;
        entry.Property<int?>("ReleasedByUserId").CurrentValue = actor.UserId;
        entry.Property<DateTime?>("ReleasedAtUtc").CurrentValue = nowUtc;
        entry.Property<string?>("ReleaseNote").CurrentValue = reason;
    }
    public static async Task<LegalHoldStatus> PersonStatusAsync(DbContext db, int agencyId, int personId, bool legacyActive, CancellationToken ct = default)
    {
        var holds = await db.Set<RecordsHold>().AsNoTracking().Where(x => x.AgencyId == agencyId && !x.IsReleased).ToListAsync(ct);
        return RecordsGovernanceRules.EvaluatePerson(personId, legacyActive, holds.Select(x => RecordsGovernanceWorkflow.ToDto(x, [])).ToArray());
    }
}
