using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Data;

namespace Sati.Api.Infrastructure;

internal static class NoteAmendmentDispatchGuard
{
    public static async Task ValidateAsync(ApiDbContext db, ServerEdiGeneration generation, CancellationToken ct)
    {
        if (generation.IsCorrection)
        {
            var corrections = await (from s in db.ClaimCorrectionSubmissions.AsNoTracking() join c in db.ClaimCorrections.AsNoTracking() on s.ClaimCorrectionId equals c.Id
                where s.EdiGenerationId == generation.Id && c.AgencyId == generation.AgencyId select c).ToListAsync(ct);
            foreach(var c in corrections) await NoteAmendmentBilling.ValidateLineVersionAsync(db, generation.AgencyId, c.NoteId, c.AmendedNoteVersionId, ct);
        }
        else
        {
            var lines = await db.ClaimLines.AsNoTracking().Where(l => l.BillingPeriodId == generation.BillingPeriodId).ToListAsync(ct);
            foreach(var l in lines) await NoteAmendmentBilling.ValidateLineVersionAsync(db, generation.AgencyId, l.NoteId, l.AmendedNoteVersionId, ct);
        }
    }
}
