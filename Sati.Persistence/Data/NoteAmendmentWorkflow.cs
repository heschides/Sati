using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using System.Text.Json;

namespace Sati.Data;

public sealed class NoteAmendmentWorkflowException(int statusCode, string code, string message) : InvalidOperationException(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

/// <summary>Shared transactional aggregate writer. The host must establish access and own a schedule transaction.</summary>
public static class NoteAmendmentWorkflow
{
    public static async Task<NoteAmendmentResultDto> ExecuteAsync(DbContext db, AgencyActor actor,
        NoteDto source, int ownerId, bool supervisorReach, NoteAmendmentRequest request, DateTime today,
        DateTime nowUtc, Func<NoteAmendmentContent, bool, Task> validateSchedule,
        Action<string, Guid, long> audit, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Amendments require an owned transaction.");
        if (source.AgencyId != actor.AgencyId) throw Missing();
        if (request.OperationId == Guid.Empty || !Enum.IsDefined(request.Action)) throw Invalid("Supply a unique operation identity and supported action.");
        if (request.ReviewReason?.Length > NoteAmendmentRules.ReasonLimit) throw Invalid("Review explanations must not exceed 1,000 characters.");
        var authorAccess = NoteAmendmentRules.CanAuthor(actor, ownerId);
        var reviewerAccess = NoteAmendmentRules.CanReview(actor, ownerId, ownerId, supervisorReach);
        if (!authorAccess && !reviewerAccess) throw Missing();
        var hash = NoteAmendmentRules.Fingerprint(source.Id, request);
        var replay = await db.Set<NoteAmendmentEvent>().AsNoTracking().SingleOrDefaultAsync(e =>
            e.AgencyId == actor.AgencyId && e.ActorId == actor.UserId && e.OperationId == request.OperationId, ct);
        if (replay is not null)
        {
            var replayAmendment = await db.Set<NoteAmendment>().AsNoTracking().SingleAsync(a => a.Id == replay.AmendmentId, ct);
            if (replayAmendment.NoteId != source.Id || replay.RequestHash != hash) throw Conflict("The operation identity was already used for another request.");
            return JsonSerializer.Deserialize<NoteAmendmentResultDto>(replay.ResponseJson)!;
        }
        if (source.Status != "Approved") throw Conflict("Only a currently approved note can be amended. The original correction routes remain available separately.");
        var amendments = await db.Set<NoteAmendment>().Where(a => a.AgencyId == actor.AgencyId && a.NoteId == source.Id).ToListAsync(ct);
        var effective = amendments.Where(a => a.Status == NoteAmendmentStatus.Approved)
            .OrderByDescending(a => a.ApprovedVersionId).FirstOrDefault();
        var effectiveId = effective?.ApprovedVersionId;
        if (source.Revision != request.ExpectedNoteRevision || effectiveId != request.ExpectedEffectiveVersionId)
            throw Conflict("The original note or its effective amendment changed. Reload the record.");
        NoteAmendment amendment;
        if (request.Action == NoteAmendmentAction.Create)
        {
            if (!authorAccess) throw Missing();
            if (request.AmendmentId is not null || request.ExpectedRevision != 0) throw Invalid("Create a new amendment without an existing amendment identity.");
            if (amendments.Any(a => a.Status is NoteAmendmentStatus.Draft or NoteAmendmentStatus.Returned or NoteAmendmentStatus.Submitted))
                throw Conflict("An unfinished amendment already exists for this note.");
            amendment = new NoteAmendment { Id = Guid.NewGuid(), AgencyId = actor.AgencyId, NoteId = source.Id,
                AuthorId = actor.UserId, OriginalNoteRevision = source.Revision, OriginalSnapshotJson = Snapshot(source),
                BaseApprovedVersionId = effectiveId, Status = NoteAmendmentStatus.Draft };
            db.Set<NoteAmendment>().Add(amendment);
            await db.SaveChangesAsync(ct);
        }
        else
        {
            amendment = amendments.SingleOrDefault(a => a.Id == request.AmendmentId) ?? throw Missing();
            if (amendment.Revision != request.ExpectedRevision || amendment.OriginalSnapshotJson != Snapshot(source) ||
                amendment.BaseApprovedVersionId != effectiveId)
                throw Conflict("The amendment or its source version changed. Reload before acting.");
            var isReview = request.Action is NoteAmendmentAction.Return or NoteAmendmentAction.Approve or NoteAmendmentAction.Reject;
            if (isReview ? !NoteAmendmentRules.CanReview(actor, ownerId, amendment.AuthorId, supervisorReach) :
                !authorAccess || amendment.AuthorId != actor.UserId) throw Missing();
            if (!NoteAmendmentRules.CanTransition(amendment.Status, request.Action))
                throw Conflict("That action is not available in the amendment's current state.");
            amendment.Revision++;
        }

        NoteAmendmentVersion version;
        if (request.Action is NoteAmendmentAction.Create or NoteAmendmentAction.Save)
        {
            if (NoteAmendmentRules.ValidateContent(request.Content, request.Reason, today) is string error) throw Invalid(error);
            version = new NoteAmendmentVersion { AmendmentId = amendment.Id,
                Number = await db.Set<NoteAmendmentVersion>().CountAsync(v => v.AmendmentId == amendment.Id, ct) + 1,
                Kind = "Draft", ContentJson = JsonSerializer.Serialize(request.Content),
                FinancialContentJson = JsonSerializer.Serialize(request.Content! with { Narrative = "" }), Reason = request.Reason!.Trim(),
                RecordedById = actor.UserId, RecordedAtUtc = nowUtc };
            db.Set<NoteAmendmentVersion>().Add(version);
            await db.SaveChangesAsync(ct);
            amendment.CurrentVersionId = version.Id;
        }
        else
        {
            if (request.Content is not null || request.Reason is not null) throw Invalid("Save content separately; workflow actions identify the exact saved version.");
            version = await db.Set<NoteAmendmentVersion>().SingleAsync(v => v.Id == amendment.CurrentVersionId && v.AmendmentId == amendment.Id, ct);
            var content = JsonSerializer.Deserialize<NoteAmendmentContent>(version.ContentJson)!;
            if (request.Action is NoteAmendmentAction.Submit or NoteAmendmentAction.Approve)
            {
                if (NoteAmendmentRules.ValidateContent(content, version.Reason, today) is string error) throw Invalid(error);
                var effectiveContent = effective is null ? NoteAmendmentContent.From(source) : JsonSerializer.Deserialize<NoteAmendmentContent>(
                    (await db.Set<NoteAmendmentVersion>().AsNoTracking().SingleAsync(v => v.Id == effective.ApprovedVersionId, ct)).ContentJson)!;
                await validateSchedule(content, NoteAmendmentRules.ChangesFinancialFacts(effectiveContent, content));
            }
            if (request.Action == NoteAmendmentAction.Submit)
            {
                // A submitted version is independently retained, including unchanged resubmissions.
                version = new NoteAmendmentVersion { AmendmentId = amendment.Id, Number = version.Number + 1,
                    Kind = "Submitted", ContentJson = version.ContentJson, FinancialContentJson = version.FinancialContentJson, Reason = version.Reason,
                    RecordedById = actor.UserId, RecordedAtUtc = nowUtc };
                db.Set<NoteAmendmentVersion>().Add(version);
                await db.SaveChangesAsync(ct);
                amendment.CurrentVersionId = version.Id;
                amendment.SubmittedVersionId = version.Id;
                amendment.Status = NoteAmendmentStatus.Submitted;
            }
            else
            {
                if (amendment.SubmittedVersionId != version.Id) throw Conflict("Review must identify the submitted version.");
                if (request.Action is NoteAmendmentAction.Return or NoteAmendmentAction.Reject &&
                    (string.IsNullOrWhiteSpace(request.ReviewReason) || request.ReviewReason.Length > NoteAmendmentRules.ReasonLimit))
                    throw Invalid("Explain the return or rejection in up to 1,000 characters.");
                amendment.Status = request.Action switch { NoteAmendmentAction.Approve => NoteAmendmentStatus.Approved,
                    NoteAmendmentAction.Return => NoteAmendmentStatus.Returned, _ => NoteAmendmentStatus.Rejected };
                if (request.Action == NoteAmendmentAction.Approve) amendment.ApprovedVersionId = version.Id;
            }
        }
        var proposed = JsonSerializer.Deserialize<NoteAmendmentContent>(version.ContentJson)!;
        var baseline = effective is null ? NoteAmendmentContent.From(source) : JsonSerializer.Deserialize<NoteAmendmentContent>(
            (await db.Set<NoteAmendmentVersion>().AsNoTracking().SingleAsync(v => v.Id == effective.ApprovedVersionId, ct)).ContentJson)!;
        amendment.ChangesFinancialFacts = NoteAmendmentRules.ChangesFinancialFacts(baseline, proposed);
        var result = new NoteAmendmentResultDto(amendment.Id, amendment.Revision, amendment.Status, version.Id, amendment.ApprovedVersionId);
        db.Set<NoteAmendmentEvent>().Add(new() { AmendmentId = amendment.Id, AgencyId = actor.AgencyId, ActorId = actor.UserId,
            OperationId = request.OperationId, RequestHash = hash, Action = request.Action, VersionId = version.Id,
            ResponseJson = JsonSerializer.Serialize(result), ReviewReason = request.ReviewReason?.Trim(), RecordedAtUtc = nowUtc });
        audit("note.amendment." + request.Action.ToString().ToLowerInvariant(), amendment.Id, version.Id);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public static string Snapshot(NoteDto source) => JsonSerializer.Serialize(source with
    { Person = null, ComplianceFailureReasons = null, ComplianceBlockers = null });

    public static async Task<NoteAmendmentWorkspaceDto> ReadAsync(DbContext db, AgencyActor actor, NoteDto source,
        int ownerId, bool supervisorReach, bool hasClaimLine, CancellationToken ct = default)
    {
        if (source.AgencyId != actor.AgencyId || !(NoteAmendmentRules.CanAuthor(actor, ownerId) || NoteAmendmentRules.CanReview(actor, ownerId, ownerId, supervisorReach))) throw Missing();
        var rows = await db.Set<NoteAmendment>().AsNoTracking().Where(a => a.NoteId == source.Id && a.AgencyId == actor.AgencyId).OrderBy(a => a.CurrentVersionId).ToListAsync(ct);
        var ids = rows.Select(a => a.Id).ToArray();
        var versions = await db.Set<NoteAmendmentVersion>().AsNoTracking().Where(v => ids.Contains(v.AmendmentId)).OrderBy(v => v.Id).ToListAsync(ct);
        var events = await db.Set<NoteAmendmentEvent>().AsNoTracking().Where(e => ids.Contains(e.AmendmentId)).OrderBy(e => e.Id).ToListAsync(ct);
        var effective = rows.Where(a => a.Status == NoteAmendmentStatus.Approved).OrderByDescending(a => a.ApprovedVersionId).FirstOrDefault();
        var content = effective is null ? NoteAmendmentContent.From(source) :
            JsonSerializer.Deserialize<NoteAmendmentContent>(versions.Single(v => v.Id == effective.ApprovedVersionId).ContentJson)!;
        return new(source, content, effective?.ApprovedVersionId, hasClaimLine, NoteAmendmentRules.CanAuthor(actor, ownerId),
            NoteAmendmentRules.CanReview(actor, ownerId, rows.LastOrDefault()?.AuthorId ?? ownerId, supervisorReach),
            await NoteAmendmentBilling.HeldNoteIds(db).AnyAsync(id => id == source.Id, ct), rows.Select(a => new NoteAmendmentDto(a.Id, a.AuthorId, a.Revision, a.OriginalNoteRevision,
                a.BaseApprovedVersionId, a.Status, a.CurrentVersionId, a.SubmittedVersionId, a.ApprovedVersionId,
                JsonSerializer.Deserialize<NoteDto>(a.OriginalSnapshotJson)!,
                versions.Where(v => v.AmendmentId == a.Id).Select(v => new NoteAmendmentVersionDto(v.Id, v.Number, v.Kind,
                    JsonSerializer.Deserialize<NoteAmendmentContent>(v.ContentJson)!, v.Reason, v.RecordedById, v.RecordedAtUtc)).ToArray(),
                events.Where(e => e.AmendmentId == a.Id).Select(e => new NoteAmendmentEventDto(e.Id, e.Action, e.VersionId,
                    e.ActorId, e.RecordedAtUtc, e.ReviewReason)).ToArray())).ToArray());
    }
    private static NoteAmendmentWorkflowException Missing() => new(404, "not_found", "The note or amendment is unavailable.");
    private static NoteAmendmentWorkflowException Invalid(string message) => new(400, "note_amendment_invalid", message);
    private static NoteAmendmentWorkflowException Conflict(string message) => new(409, NoteAmendmentRules.RevisionCode, message);
}
