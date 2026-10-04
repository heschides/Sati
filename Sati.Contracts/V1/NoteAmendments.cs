using System.Security.Cryptography;
using System.Text.Json;

namespace Sati.Contracts.V1;

public enum NoteAmendmentStatus { Draft, Submitted, Returned, Approved, Rejected }
public enum NoteAmendmentAction { Create, Save, Submit, Return, Approve, Reject }

public sealed record NoteAmendmentContent(string Narrative, DateTime? EventDate, int? Minutes, int? StartTime, bool IsUnbilled)
{
    public static NoteAmendmentContent From(NoteDto note) => new(note.Narrative, note.EventDate, note.Minutes, note.StartTime, note.IsUnbilled);
}

public sealed record NoteAmendmentRequest(Guid OperationId, NoteAmendmentAction Action,
    Guid? AmendmentId, int ExpectedRevision, int ExpectedNoteRevision, long? ExpectedEffectiveVersionId,
    NoteAmendmentContent? Content = null, string? Reason = null, string? ReviewReason = null);
public sealed record NoteAmendmentResultDto(Guid Id, int Revision, NoteAmendmentStatus Status, long VersionId, long? ApprovedVersionId);
public sealed record NoteAmendmentVersionDto(long Id, int Number, string Kind, NoteAmendmentContent Content,
    string Reason, int RecordedById, DateTime RecordedAtUtc);
public sealed record NoteAmendmentEventDto(long Id, NoteAmendmentAction Action, long VersionId,
    int ActorId, DateTime RecordedAtUtc, string? ReviewReason);
public sealed record NoteAmendmentDto(Guid Id, int AuthorId, int Revision, int OriginalNoteRevision,
    long? BaseApprovedVersionId, NoteAmendmentStatus Status, long CurrentVersionId, long? SubmittedVersionId,
    long? ApprovedVersionId, NoteDto OriginalSnapshot, IReadOnlyList<NoteAmendmentVersionDto> Versions,
    IReadOnlyList<NoteAmendmentEventDto> Events);
public sealed record NoteAmendmentWorkspaceDto(NoteDto Original, NoteAmendmentContent EffectiveContent,
    long? EffectiveVersionId, bool HasClaimLine, bool CanAuthor, bool CanReview, bool RequiresFinancialReview,
    IReadOnlyList<NoteAmendmentDto> Amendments);
public sealed record NoteAmendmentQueueItem(int NoteId, int PersonId, string ClientName,
    DateTime? ServiceDate, string State, bool RequiresFinancialReview);
public sealed record NoteAmendmentQueuePage(IReadOnlyList<NoteAmendmentQueueItem> Items, int? NextAfterNoteId);
public sealed record NoteAmendmentFinancialReviewRequest(Guid OperationId, long ApprovedVersionId, int ExpectedNoteRevision, string Reason);
public sealed record NoteAmendmentFinancialItem(int NoteId, string ClientName, long ApprovedVersionId, int NoteRevision,
    NoteAmendmentContent OriginalService, NoteAmendmentContent CorrectedService, bool HasClaimLine, bool Reviewed);
public sealed record NoteAmendmentFinancialReviewDto(long Id, long ApprovedVersionId, int ReviewedById, DateTime ReviewedAtUtc, string Reason);

/// <summary>One owner for amendment permissions, transitions and content validity.</summary>
public static class NoteAmendmentRules
{
    public const string RevisionCode = "note_amendment_changed";
    public const int NarrativeLimit = 1_000_000;
    public const int ReasonLimit = 1000;
    public const int PageSize = 30;

    public static bool CanAuthor(AgencyActor actor, int ownerId) => actor.UserId == ownerId &&
        UserPermissionRules.HasCaseManagerPermissions(actor.Permissions);
    public static bool CanReview(AgencyActor actor, int ownerId, int authorId, bool supervisorReach) =>
        supervisorReach && UserPermissionRules.HasSupervisorPermissions(actor.Permissions) &&
        actor.UserId != ownerId && actor.UserId != authorId;
    public static bool CanReviewFinancial(AgencyActor actor, int ownerId, int authorId) =>
        UserPermissionRules.HasBillingPermissions(actor.Permissions) && actor.UserId != ownerId && actor.UserId != authorId;
    public static bool CanTransition(NoteAmendmentStatus status, NoteAmendmentAction action) => (status, action) switch
    {
        (NoteAmendmentStatus.Draft or NoteAmendmentStatus.Returned, NoteAmendmentAction.Save or NoteAmendmentAction.Submit) => true,
        (NoteAmendmentStatus.Submitted, NoteAmendmentAction.Return or NoteAmendmentAction.Approve or NoteAmendmentAction.Reject) => true,
        _ => false
    };
    public static bool ChangesFinancialFacts(NoteAmendmentContent original, NoteAmendmentContent proposed) =>
        original.EventDate != proposed.EventDate || original.Minutes != proposed.Minutes ||
        original.StartTime != proposed.StartTime || original.IsUnbilled != proposed.IsUnbilled;
    public static string Fingerprint(int noteId, NoteAmendmentRequest request) => Convert.ToHexString(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { noteId, request })));
    public static string? ValidateContent(NoteAmendmentContent? content, string? reason, DateTime today)
    {
        if (content is null || string.IsNullOrWhiteSpace(content.Narrative) || content.Narrative.Length > NarrativeLimit)
            return "Enter the corrected narrative within the note length limit.";
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5 || reason.Length > ReasonLimit)
            return "Explain the amendment in 5 to 1,000 characters.";
        if (content.EventDate is not DateTime date || date.Date > today.Date)
            return "Use the actual service date, no later than today.";
        if (content.Minutes is not (>= 1 and <= 1440)) return "Enter 1 to 1,440 service minutes.";
        if (content.StartTime is int start && !ServiceTimeline.IsWithinWindow(start, content.Minutes.Value))
            return "The corrected service time must fit between 7 AM and 7 PM.";
        return null;
    }
}
