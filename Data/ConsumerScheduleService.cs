using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

// Transitional local Production implementation. Demo uses the API implementation.
public sealed class ConsumerScheduleService(
    IDbContextFactory<SatiContext> contextFactory, ISessionService session) : IConsumerScheduleService
{
    public async Task<IReadOnlyList<ConsumerScheduleEntryDto>> GetAsync(int personId)
    {
        await using var db = contextFactory.CreateDbContext();
        var actor = await EnsureAccessAsync(db, personId);
        var rows = await db.ConsumerScheduleEntries.AsNoTracking()
            .Where(x => x.PersonId == personId)
            .OrderBy(x => x.Kind).ThenBy(x => x.Date).ThenBy(x => x.EffectiveStart)
            .ThenBy(x => x.StartMinute).ThenBy(x => x.Id)
            .ToListAsync();
        LocalAuditTrail.Record(db, actor, LocalAuditActions.ConsumerScheduleViewed,
            "Person", personId);
        await db.SaveChangesAsync();
        return rows.Select(x => x.ToDto()).ToList();
    }

    public async Task<ConsumerScheduleEntryDto> SaveAsync(int personId, int? entryId,
        SaveConsumerScheduleEntryRequest request)
    {
        var errors = ConsumerScheduleRules.Validate(request);
        if (errors.Count != 0)
            throw new InvalidOperationException(string.Join(" ", errors.Values.SelectMany(x => x)));
        await using var db = contextFactory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await EnsureAccessAsync(db, personId);
        ConsumerScheduleEntry row;
        if (entryId is null)
        {
            if (request.ExpectedRevision != 0)
                throw new InvalidOperationException("Reload this schedule before saving.");
            row = new ConsumerScheduleEntry { PersonId = personId };
            db.ConsumerScheduleEntries.Add(row);
        }
        else
        {
            row = await db.ConsumerScheduleEntries.SingleOrDefaultAsync(x =>
                x.Id == entryId && x.PersonId == personId)
                ?? throw new InvalidOperationException("This schedule entry is no longer on this consumer's record.");
            if (row.Revision != request.ExpectedRevision)
                throw new InvalidOperationException("This schedule changed in another session. Reload it before saving.");
        }
        row.Apply(request);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("This schedule changed in another session. Reload it before saving.");
        }
        LocalAuditTrail.Record(db, actor, entryId is null
                ? LocalAuditActions.ConsumerScheduleCreated : LocalAuditActions.ConsumerScheduleUpdated,
            "Person", personId, JsonSerializer.Serialize(new { entryId = row.Id, row.Revision, row.Kind }));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return row.ToDto();
    }

    public async Task DeleteAsync(int personId, int entryId, int expectedRevision)
    {
        await using var db = contextFactory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await EnsureAccessAsync(db, personId);
        var row = await db.ConsumerScheduleEntries.SingleOrDefaultAsync(x =>
            x.Id == entryId && x.PersonId == personId)
            ?? throw new InvalidOperationException("This schedule entry is no longer on this consumer's record.");
        if (row.Revision != expectedRevision)
            throw new InvalidOperationException("This schedule changed in another session. Reload it before removing.");
        db.ConsumerScheduleEntries.Remove(row);
        LocalAuditTrail.Record(db, actor, LocalAuditActions.ConsumerScheduleDeleted,
            "Person", personId, JsonSerializer.Serialize(new { entryId, row.Revision, row.Kind }));
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("This schedule changed in another session. Reload it before removing.");
        }
        await transaction.CommitAsync();
    }

    private async Task<User> EnsureAccessAsync(SatiContext db, int personId)
    {
        await LocalTenantAccess.EnsureSessionAsync(db, session);
        var actor = session.CurrentUser ?? throw new UnauthorizedAccessException("Sign in to view schedules.");
        if (!await LocalTenantAccess.OwnsPersonAsync(db, actor, personId))
            throw new UnauthorizedAccessException("That consumer is not on your caseload.");
        return actor;
    }
}
