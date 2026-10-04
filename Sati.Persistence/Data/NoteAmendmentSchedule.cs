using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using System.Text.Json;

namespace Sati.Data;

public static class NoteAmendmentSchedule
{
    // Keep the original reservation as well until its financial lineage has been resolved.
    public static async Task<bool> ConflictsAsync(DbContext db, IQueryable<int> scopedNoteIds,
        int agencyId, DateTime date, ServiceBlock candidate, CancellationToken ct = default)
    {
        var rows = await (from a in db.Set<NoteAmendment>().AsNoTracking()
                          join v in db.Set<NoteAmendmentVersion>().AsNoTracking() on a.ApprovedVersionId equals (long?)v.Id
                          where a.AgencyId == agencyId && scopedNoteIds.Contains(a.NoteId) && a.NoteId != candidate.NoteId &&
                            a.Status == NoteAmendmentStatus.Approved
                          select new { a.NoteId, v.FinancialContentJson, v.Id }).ToListAsync(ct);
        var blocks = rows.GroupBy(v => v.NoteId).Select(g => g.OrderByDescending(v => v.Id).First()).Select(v =>
        {
            var content = JsonSerializer.Deserialize<NoteAmendmentContent>(v.FinancialContentJson)!;
            return content.EventDate?.Date == date.Date ? ServiceTimeline.TryCreateBlock(v.NoteId, content.StartTime, content.Minutes, "Approved") : null;
        }).OfType<ServiceBlock>();
        return ServiceTimeline.FindConflicts(candidate, blocks).Count > 0;
    }
}
