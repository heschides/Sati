using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models.Assessments;

namespace Sati.Data;

public sealed class AssessmentWorkflowException(int status, string code, string message) : InvalidOperationException(message)
{ public int Status { get; } = status; public string Code { get; } = code; }
public sealed record AssessmentWorkflowState(int Id, int PersonId, int AuthorUserId, int Version,
    int Revision, string Status, string DocumentJson);

/// <summary>Staging owner; callers supply authorization and an enclosing serializable transaction.</summary>
public static class AssessmentReviewWorkflow
{
    public static void EnsureRevision(AssessmentWorkflowState state, int revision)
    {
        if (revision != state.Revision)
            throw new AssessmentWorkflowException(409, "stale_assessment", "The assessment changed. Reload before continuing.");
    }
    public static async Task<AssessmentSubmission> SubmitAsync(DbContext db, AgencyActor actor,
        AssessmentWorkflowState state, SubmitAssessmentRequest request, string consumerName,
        DateTime now, CancellationToken ct = default)
    {
        EnsureTransaction(db); EnsureRevision(state, request.ExpectedRevision);
        if (!AssessmentReviewRules.CanEdit(state.Status)) throw Conflict("This assessment is not editable.");
        if (request.ContentSha256 != AssessmentReviewRules.Hash(state.DocumentJson))
            throw Conflict("The submitted content does not match the saved revision. Save and reload first.");
        var issues = AssessmentReviewRules.Validate(AssessmentReviewRules.Parse(state.DocumentJson));
        if (issues.Count != 0) throw new AssessmentValidationException(issues);
        var cycle = (await db.Set<AssessmentSubmission>().Where(x => x.AssessmentId == state.Id)
            .MaxAsync(x => (int?)x.CycleNumber, ct) ?? 0) + 1;
        var snapshot = new AssessmentSubmission
        {
            AgencyId = actor.AgencyId, AssessmentId = state.Id, PersonId = state.PersonId,
            AuthorUserId = state.AuthorUserId, AssessmentVersion = state.Version,
            CycleNumber = cycle, DocumentRevision = state.Revision, FormId = request.FormId,
            TargetEffectiveDate = request.TargetEffectiveDate.Date, DueDate = request.DueDate.Date,
            RulesVersion = AssessmentReviewRules.Version, ContentSha256 = request.ContentSha256,
            DocumentJson = state.DocumentJson, ConsumerName = consumerName, SubmittedAtUtc = now
        };
        db.Add(snapshot);
        return snapshot;
    }

    public static async Task<AssessmentReviewEvent> ReviewAsync(DbContext db, AgencyActor actor,
        AssessmentWorkflowState state, AssessmentReviewRequest request, bool canReview, bool canAuthor,
        DateTime now, CancellationToken ct = default)
    {
        EnsureTransaction(db); EnsureRevision(state, request.ExpectedRevision);
        var snapshot = await db.Set<AssessmentSubmission>().AsNoTracking()
            .Where(x => x.AssessmentId == state.Id && x.AgencyId == actor.AgencyId)
            .OrderByDescending(x => x.CycleNumber).FirstOrDefaultAsync(ct);
        if (snapshot is null || snapshot.Id != request.SubmissionId || snapshot.ContentSha256 != request.ContentSha256)
            throw Conflict("Reload the current submitted version before reviewing.");
        var response = request.Action == "Respond";
        if (response ? !canAuthor || state.Status != "Returned" : !canReview || state.Status != "ReadyForReview")
            throw new AssessmentWorkflowException(403, "assessment_review_denied", "Independent review authority is required for this action.");
        if (!response && snapshot.ContentSha256 != AssessmentReviewRules.Hash(state.DocumentJson))
            throw Conflict("The submitted answers no longer match their immutable snapshot.");
        if (request.Location is null || request.Text is null || !AssessmentReviewRules.IsLocation(request.Location) || request.Text.Length > 4000 ||
            request.Action is not ("Comment" or "Flag" or "Resolve" or "Respond" or "Return" or "Approve"))
            throw new AssessmentWorkflowException(400, "assessment_review_invalid", "Choose a recognized review action and location, with text limited to 4,000 characters.");
        if (request.Action != "Approve" && string.IsNullOrWhiteSpace(request.Text))
            throw new AssessmentWorkflowException(400, "assessment_review_reason", "A comment or documented decision is required.");
        if (request.Action is "Resolve" or "Respond")
        {
            var flag = await db.Set<AssessmentReviewEvent>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.AssessmentId == state.Id && x.Id == request.FlagId && x.Action == "Flag", ct);
            if (flag is null || await db.Set<AssessmentReviewEvent>().AnyAsync(x =>
                x.AssessmentId == state.Id && x.FlagId == flag.Id && x.Action == "Resolve", ct))
                throw Conflict("Select an unresolved flag on this assessment.");
        }
        else if (request.FlagId is not null) throw Conflict("Only responses and resolutions reference a flag.");
        if (request.Blocking && request.Action != "Flag") throw Conflict("Only a flag may be marked blocking.");
        if (request.Action == "Approve")
        {
            var events = await db.Set<AssessmentReviewEvent>().AsNoTracking().Where(x => x.AssessmentId == state.Id).ToListAsync(ct);
            if (events.Any(flag => flag.Action == "Flag" && flag.Blocking && !events.Any(e => e.Action == "Resolve" && e.FlagId == flag.Id)))
                throw Conflict("Resolve every blocking flag before approval, including earlier review cycles.");
            var issues = AssessmentReviewRules.Validate(AssessmentReviewRules.Parse(snapshot.DocumentJson));
            if (issues.Count > 0) throw new AssessmentValidationException(issues);
        }
        if ((request.CompletedOn is not null || request.CompletionAttested) && request.Action != "Approve" ||
            request.CompletionAttested != (request.CompletedOn is not null))
            throw new AssessmentWorkflowException(400, "assessment_attestation_required", "Form completion requires both an explicit completion attestation and an actual date with approval.");
        var review = new AssessmentReviewEvent
        {
            AgencyId = actor.AgencyId, AssessmentId = state.Id, SubmissionId = snapshot.Id,
            Action = request.Action, Location = request.Location, Text = request.Text.Trim(),
            Blocking = request.Blocking, FlagId = request.FlagId, ActorUserId = actor.UserId,
            RecordedAtUtc = now, AssessmentRevision = state.Revision + 1, CompletedOn = request.CompletedOn?.Date
        };
        // Approval may generate an artifact before the event can cite its identity.
        // The caller adds this event once, after obtaining that identity.
        return review;
    }
    public static AssessmentSubmissionDto ToDto(AssessmentSubmission x) => new(x.Id, x.AssessmentId,
        x.AssessmentVersion, x.CycleNumber, x.DocumentRevision, x.FormId, x.TargetEffectiveDate, x.DueDate,
        x.RulesVersion, x.ContentSha256, x.DocumentJson, x.ConsumerName, x.SubmittedAtUtc);
    public static AssessmentReviewEventDto ToDto(AssessmentReviewEvent x) => new(x.Id, x.SubmissionId,
        x.Action, x.Location, x.Text, x.Blocking, x.FlagId, x.ActorUserId, x.RecordedAtUtc,
        x.AssessmentRevision, x.ArtifactId, x.CompletedOn);
    private static void EnsureTransaction(DbContext db)
    { if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Assessment review requires an enclosing transaction."); }
    private static AssessmentWorkflowException Conflict(string message) => new(409, "stale_assessment", message);
}
