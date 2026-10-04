using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Security;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal sealed record NoteAbandonmentResult(int ChangedCount, int CandidateCount, bool AtLimit);

/// <summary>Audited, revision-checked transition shared by the desktop route and scheduled worker.</summary>
internal sealed class NoteAbandonmentSweep(
    IDbContextFactory<ApiDbContext> contexts, ApiClock clock)
{
    internal const int WorkerBatchSize = 100;

    internal async Task<NoteAbandonmentResult> RunAsync(int agencyId, int? userId,
        int batchSize, string trigger, CancellationToken token)
    {
        if (agencyId <= 0 || batchSize is < 1 or > NoteAbandonmentRules.DesktopBatchSize ||
            trigger is not ("worker" or "desktop"))
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        var today = clock.Today;
        await using var strategyContext = await contexts.CreateDbContextAsync(token);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var configuredDays = await db.Settings.AsNoTracking()
                .Where(settings => settings.AgencyId == agencyId)
                .Select(settings => (int?)settings.AbandonedAfterDays)
                .SingleOrDefaultAsync(token);
            var windowDays = ProductivityForecast.NormalizeDocumentationWindowDays(configuredDays);
            var threshold = today.AddDays(-windowDays);
            var eligible =
                from note in db.Notes.AsNoTracking()
                join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                where note.AgencyId == agencyId && person.AgencyId == agencyId &&
                      (userId == null || person.UserId == userId.Value) &&
                      note.Status == NoteWorkflow.Pending &&
                      note.EventDate.HasValue && note.EventDate.Value < threshold
                orderby note.EventDate, note.Id
                select new NoteAbandonmentCandidate(note.Id, note.Revision,
                    note.Status, note.EventDate);
            var candidates = await eligible.Take(batchSize).ToListAsync(token);
            var changedIds = new List<int>(candidates.Count);
            foreach (var candidate in candidates)
            {
                if (!NoteAbandonmentRules.IsEligible(candidate.Status,
                        candidate.EventDate, today, windowDays))
                    continue;
                // The candidate is a snapshot. The conditional update is the authority:
                // a save, date move, reassignment or workflow change wins the race.
                var changed = await db.Notes.Where(note =>
                        note.Id == candidate.Id && note.AgencyId == agencyId &&
                        note.Revision == candidate.Revision &&
                        note.Status == candidate.Status &&
                        note.EventDate == candidate.EventDate &&
                        db.People.Any(person => person.Id == note.PersonId &&
                            person.AgencyId == agencyId &&
                            (userId == null || person.UserId == userId.Value)))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(note => note.Status, NoteWorkflow.Abandoned)
                        .SetProperty(note => note.Revision, note => note.Revision + 1), token);
                if (changed == 1) changedIds.Add(candidate.Id);
            }

            if (changedIds.Count > 0)
            {
                db.AuditEvents.Add(new ServerAuditEvent
                {
                    AgencyId = agencyId,
                    ActorUserId = SystemActor.UserId,
                    Action = AuditActions.NoteAbandonedBySystem,
                    ResourceType = "Note",
                    OccurredAtUtc = clock.UtcNow.UtcDateTime,
                    CorrelationId = $"note-abandonment-{Guid.NewGuid():N}",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        actorKind = "system",
                        jobName = "NoteAbandonmentSweep",
                        trigger,
                        localDate = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                        noteCount = changedIds.Count,
                        noteIds = changedIds.Take(100).ToArray(),
                        idsTruncated = changedIds.Count > 100
                    })
                });
                await db.SaveChangesAsync(token);
            }
            await transaction.CommitAsync(token);
            return new NoteAbandonmentResult(changedIds.Count, candidates.Count,
                candidates.Count == batchSize);
        });
    }

    private sealed record NoteAbandonmentCandidate(int Id, int Revision,
        int? Status, DateTime? EventDate);
}
