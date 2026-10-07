using Microsoft.EntityFrameworkCore;
using Sati.Models;
using Sati.Models.Assessments;
using System.Text.Json;
using Sati.Contracts.V1;

namespace Sati.Data;

public sealed partial class ComprehensiveAssessmentService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService)
    : IComprehensiveAssessmentService
{
    private IDbContextFactory<SatiContext> Factory => contextFactory;
    private ISessionService Session => sessionService;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ComprehensiveAssessment?> GetLatestForAgendaAsync(int personId)
    {
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in case manager is required.");
        if (!actor.HasCaseManagerPermissions)
            throw new UnauthorizedAccessException("A case manager account is required.");

        await using var db = await contextFactory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(db, sessionService);
        await EnsureAuthoringEnabledAsync(db, actor.AgencyId);
        var ownsPerson = await LocalTenantAccess.OwnsPersonAsync(db, actor, personId);
        if (!ownsPerson)
            throw new UnauthorizedAccessException("Only the assigned case manager may read this assessment.");

        return await db.ComprehensiveAssessments.AsNoTracking()
            .Where(assessment => assessment.PersonId == personId &&
                                 assessment.Status != AssessmentStatus.Superseded)
            .OrderByDescending(assessment => assessment.Version)
            .FirstOrDefaultAsync();
    }

    public async Task<ComprehensiveAssessment> GetOrCreateDraftAsync(int personId, int authorUserId)
    {
        var actor = CurrentAuthor(authorUserId);
        await using var db = await contextFactory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(db, sessionService);
        await EnsureAuthoringEnabledAsync(db, actor.AgencyId);
        var canAuthor = await LocalTenantAccess.OwnsPersonAsync(db, actor, personId);
        if (!canAuthor)
            throw new UnauthorizedAccessException("Only the assigned case manager may author this assessment.");
        var editableAssessment = await db.ComprehensiveAssessments
            .Where(a => a.PersonId == personId && a.AuthorUserId == authorUserId)
            .Where(a => a.Status == AssessmentStatus.Draft || a.Status == AssessmentStatus.Returned)
            .OrderByDescending(a => a.Version)
            .FirstOrDefaultAsync();

        var latestApproved = await db.ComprehensiveAssessments
            .Where(a => a.PersonId == personId && a.Status == AssessmentStatus.Approved)
            .OrderByDescending(a => a.Version)
            .FirstOrDefaultAsync();

        // A draft can predate an imported or newly approved assessment. Keep any
        // real author work, but retire an empty stale shell so it cannot hide the
        // newer approved content forever.
        if (editableAssessment is not null)
        {
            var isStaleEmptyDraft = latestApproved is not null
                && editableAssessment.Version <= latestApproved.Version
                && !HasDocumentContent(editableAssessment.DocumentJson);
            if (!isStaleEmptyDraft) return editableAssessment;

            editableAssessment.Status = AssessmentStatus.Superseded;
            editableAssessment.UpdatedAt = DateTime.UtcNow;
        }

        var latestVersion = await db.ComprehensiveAssessments
            .Where(a => a.PersonId == personId)
            .Select(a => (int?)a.Version).MaxAsync() ?? 0;
        var assessment = new ComprehensiveAssessment
        {
            PersonId = personId, AuthorUserId = authorUserId, Version = latestVersion + 1,
            DocumentJson = latestApproved?.DocumentJson
                ?? JsonSerializer.Serialize(new AssessmentDocument(), JsonOptions)
        };
        db.ComprehensiveAssessments.Add(assessment);
        LocalAuditTrail.Record(db, actor, LocalAuditActions.AssessmentCreated, "ComprehensiveAssessment");
        await db.SaveChangesAsync();
        return assessment;
    }

    private static bool HasDocumentContent(string documentJson)
    {
        try
        {
            var document = JsonSerializer.Deserialize<AssessmentDocument>(documentJson, JsonOptions);
            return document is not null
                && (document.Contributors.Count > 0
                    || document.Needs.Count > 0
                    || !string.IsNullOrWhiteSpace(document.NoIdentifiedNeedsReason)
                    || document.Answers.Values.Any(HasAnswerContent));
        }
        catch (JsonException)
        {
            // Never discard an unparseable draft automatically; it may contain
            // recoverable author work that needs manual review.
            return true;
        }
    }

    private static bool HasAnswerContent(AssessmentAnswer answer) =>
        answer.Status != AssessmentAnswerStatus.NotYetAnswered
        || !string.IsNullOrWhiteSpace(answer.Narrative)
        || answer.Supports != SupportMethod.None
        || !string.IsNullOrWhiteSpace(answer.SupportDetails)
        || !string.IsNullOrWhiteSpace(answer.ExceptionReason)
        || !string.IsNullOrWhiteSpace(answer.DissentingOpinion)
        || !string.IsNullOrWhiteSpace(answer.DissentContributor)
        || !string.IsNullOrWhiteSpace(answer.DissentDiscussion)
        || answer.DissentUnresolved.HasValue
        || answer.YesNoResponse.HasValue
        || answer.FollowUpYesNoResponse.HasValue
        || !string.IsNullOrWhiteSpace(answer.Details)
        || answer.TherapySessionFormat != TherapySessionFormat.NotSelected
        || answer.WantsOtherSessionFormat.HasValue
        || answer.WantsFrequencyChange.HasValue
        || answer.TherapyFrequencyDirection != TherapyFrequencyDirection.NotSelected
        || answer.ActivitySupportLevels.Values.Any(level => level != ActivitySupportLevel.Independent)
        || answer.ActivitySkillsTraining.Values.Any(selected => selected);

    public async Task SaveDocumentAsync(
        ComprehensiveAssessment assessment,
        AssessmentDocument document)
    {
        var actor = CurrentAuthor(assessment.AuthorUserId);
        await using var db = await contextFactory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(db, sessionService);
        await EnsureAuthoringEnabledAsync(db, actor.AgencyId);
        await LocalTenantAccess.EnsureCurrentActorAsync(db, actor);
        var stored = await db.ComprehensiveAssessments
            .Include(candidate => candidate.Person)
            .SingleAsync(candidate => candidate.Id == assessment.Id);
        await EnsureCanAuthorAsync(db, actor, stored);
        if (stored.Revision != assessment.Revision)
            throw new DbUpdateConcurrencyException("This assessment was changed by someone else. Reload it before saving.");
        if (!AssessmentReviewRules.CanEdit(stored.Status.ToString()))
            throw new InvalidOperationException("Approved assessment versions cannot be changed.");
        await ValidateProvidersAsync(db, actor.AgencyId, stored.PersonId, document, AssessmentReviewRules.Parse(stored.DocumentJson));
        stored.DocumentJson = JsonSerializer.Serialize(document, JsonOptions);
        stored.UpdatedAt = DateTime.UtcNow;
        stored.Revision++;
        LocalAuditTrail.Record(
            db, actor, LocalAuditActions.AssessmentUpdated, "ComprehensiveAssessment", stored.Id);
        await db.SaveChangesAsync();
        assessment.DocumentJson = stored.DocumentJson;
        assessment.UpdatedAt = stored.UpdatedAt;
        assessment.Revision = stored.Revision;
    }

    public Task SubmitForReviewAsync(ComprehensiveAssessment assessment) => SubmitCoreAsync(assessment);

    private User CurrentAuthor(int requestedAuthorUserId)
    {
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in case manager is required.");
        if (!actor.HasCaseManagerPermissions || actor.Id != requestedAuthorUserId)
            throw new UnauthorizedAccessException("Only the signed-in case manager may author an assessment.");
        return actor;
    }

    private static async Task EnsureCanAuthorAsync(SatiContext db, User actor, ComprehensiveAssessment assessment)
    {
        if (assessment.AuthorUserId != actor.Id ||
            !await LocalTenantAccess.OwnsPersonAsync(db, actor, assessment.PersonId))
        {
            throw new UnauthorizedAccessException("Only the assigned case manager may change this assessment.");
        }
    }

    private static async Task EnsureAuthoringEnabledAsync(SatiContext db, int agencyId)
    {
        var enabled = await db.Settings.AsNoTracking()
            .Where(settings => settings.AgencyId == agencyId)
            .Select(settings => (bool?)settings.IsComprehensiveAssessmentAuthoringEnabled)
            .SingleOrDefaultAsync() ?? false;
        if (!enabled)
            throw new NotSupportedException(
                "Sati Comprehensive Assessment authoring is turned off for this agency. Record the Evergreen completion through the form attestation workflow.");
    }
}
