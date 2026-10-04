using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Models.Billing;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sati.Data;

public static class NoteAmendmentBilling
{
    public static async Task EnsureTransportResolvedAsync(DbContext db, int agencyId, IQueryable<long> generationIds, CancellationToken ct=default)
    {
        if (await db.Set<ClearinghouseDispatch>().AnyAsync(d => d.AgencyId == agencyId && generationIds.Contains(d.EdiGenerationId) &&
            (d.State == ClearinghouseDispatchState.Sending || d.State == ClearinghouseDispatchState.OutcomeUnknown), ct))
            throw new NoteAmendmentWorkflowException(409, "note_amendment_transport_in_progress", "Resolve the in-progress or uncertain claim transmission before approving changed financial facts.");
    }
    public static async Task<NoteAmendmentContent?> LineContentAsync(DbContext db, long? versionId, CancellationToken ct = default)
    {
        if (versionId is null) return null;
        var json = await db.Set<NoteAmendmentVersion>().AsNoTracking().Where(v => v.Id == versionId).Select(v => v.FinancialContentJson).SingleAsync(ct);
        return JsonSerializer.Deserialize<NoteAmendmentContent>(json)!;
    }
    public static async Task ValidateLineVersionAsync(DbContext db, int agencyId, int noteId, long? lineVersion, CancellationToken ct = default)
    {
        var content = await AuthorizedContentAsync(db, agencyId, noteId, ct);
        if (content is { } c && c.VersionId != lineVersion)
            throw new NoteAmendmentWorkflowException(409, "note_amendment_claim_correction_required", "This immutable claim predates the approved financial amendment. Use its linked correction/replacement/void workflow; do not send the original again.");
    }
    public static IQueryable<int> HeldNoteIds(DbContext db) => db.Set<NoteAmendment>()
        .Where(a => a.Status == NoteAmendmentStatus.Approved && a.ChangesFinancialFacts &&
            !db.Set<NoteAmendment>().Any(later => later.NoteId == a.NoteId && later.Status == NoteAmendmentStatus.Approved && later.ChangesFinancialFacts && later.ApprovedVersionId > a.ApprovedVersionId) &&
            !db.Set<NoteAmendmentFinancialReview>().Any(r => r.ApprovedVersionId == a.ApprovedVersionId)).Select(a => a.NoteId);

    public static async Task<(long VersionId, NoteAmendmentContent Content)?> AuthorizedContentAsync(DbContext db, int agencyId, int noteId, CancellationToken ct = default)
    {
        var a = await db.Set<NoteAmendment>().AsNoTracking().Where(a => a.NoteId == noteId && a.AgencyId == agencyId && a.Status == NoteAmendmentStatus.Approved && a.ChangesFinancialFacts)
            .OrderByDescending(a => a.ApprovedVersionId).Select(a => new { a.Id, a.ApprovedVersionId, a.ChangesFinancialFacts }).FirstOrDefaultAsync(ct);
        if (a is null || !a.ChangesFinancialFacts) return null;
        if (!await db.Set<NoteAmendmentFinancialReview>().AnyAsync(r => r.AgencyId == agencyId && r.NoteId == noteId && r.ApprovedVersionId == a.ApprovedVersionId, ct))
            throw new NoteAmendmentWorkflowException(409, "note_amendment_financial_hold", "The approved amendment requires explicit financial review before billing can proceed.");
        var v = await db.Set<NoteAmendmentVersion>().AsNoTracking().Where(v => v.Id == a.ApprovedVersionId && v.AmendmentId == a.Id)
            .Select(v => new { v.Id, v.FinancialContentJson }).SingleAsync(ct);
        return (v.Id, JsonSerializer.Deserialize<NoteAmendmentContent>(v.FinancialContentJson)!);
    }
    public static async Task<NoteAmendmentFinancialReviewDto> ReviewAsync(DbContext db, AgencyActor actor, int noteId, int ownerId,
        int noteRevision, NoteAmendmentFinancialReviewRequest request, DateTime nowUtc, Action<long> audit, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Financial review requires an owned transaction.");
        var a = await db.Set<NoteAmendment>().AsNoTracking().Where(a => a.AgencyId == actor.AgencyId && a.NoteId == noteId && a.Status == NoteAmendmentStatus.Approved && a.ChangesFinancialFacts)
            .OrderByDescending(a => a.ApprovedVersionId).Select(a => new { a.AuthorId, a.ApprovedVersionId, a.ChangesFinancialFacts }).FirstOrDefaultAsync(ct);
        if (a is null || !NoteAmendmentRules.CanReviewFinancial(actor, ownerId, a.AuthorId)) throw new NoteAmendmentWorkflowException(404, "not_found", "The financial review is unavailable.");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { noteId, request })));
        var previous = await db.Set<NoteAmendmentFinancialReview>().AsNoTracking().SingleOrDefaultAsync(r => r.AgencyId == actor.AgencyId && r.ReviewedById == actor.UserId && r.OperationId == request.OperationId, ct);
        if (previous is not null)
        {
            if (previous.RequestHash != hash) throw new NoteAmendmentWorkflowException(409, NoteAmendmentRules.RevisionCode, "This operation identity was used for another financial review.");
            return ToDto(previous);
        }
        if (request.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5 || request.Reason.Length > NoteAmendmentRules.ReasonLimit)
            throw new NoteAmendmentWorkflowException(400, "note_amendment_invalid", "Explain financial review in 5 to 1,000 characters and supply an operation identity.");
        if (noteRevision != request.ExpectedNoteRevision || a.ApprovedVersionId != request.ApprovedVersionId || !a.ChangesFinancialFacts ||
            await db.Set<NoteAmendmentFinancialReview>().AnyAsync(r => r.ApprovedVersionId == request.ApprovedVersionId, ct))
            throw new NoteAmendmentWorkflowException(409, NoteAmendmentRules.RevisionCode, "The approved financial version changed or was already reviewed.");
        var row = new NoteAmendmentFinancialReview { AgencyId = actor.AgencyId, NoteId = noteId, ApprovedVersionId = request.ApprovedVersionId,
            ReviewedById = actor.UserId, ReviewedAtUtc = nowUtc, Reason = request.Reason.Trim(), OperationId = request.OperationId, RequestHash = hash };
        db.Add(row); audit(row.ApprovedVersionId); await db.SaveChangesAsync(ct); return ToDto(row);
    }
    public static NoteAmendmentFinancialReviewDto ToDto(NoteAmendmentFinancialReview r) => new(r.Id, r.ApprovedVersionId, r.ReviewedById, DateTime.SpecifyKind(r.ReviewedAtUtc, DateTimeKind.Utc), r.Reason);
}

