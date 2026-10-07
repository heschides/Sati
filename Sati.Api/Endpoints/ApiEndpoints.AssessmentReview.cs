using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Forms;
using Sati.Models.Assessments;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapAssessmentReview(RouteGroupBuilder api)
    {
        api.MapPost("/assessments/{assessmentId:int}/reopen-legacy", async Task<IResult> (int assessmentId,
            ReopenLegacyAssessmentRequest request, ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail audit, ApiClock clock, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var assessment = await LoadAssessmentAsync(db, actor, assessmentId, ct);
            if (assessment is null || !await TenantAccess.CanAuthorAssessmentAsync(db, actor, assessment, ct)) return Results.NotFound();
            AssessmentReviewWorkflow.EnsureRevision(AssessmentState(assessment), request.ExpectedRevision);
            if (!AssessmentReviewRules.CanReopenLegacy(assessment.Status,
                    await db.Set<AssessmentSubmission>().AnyAsync(x => x.AssessmentId == assessmentId, ct)))
                return Results.Conflict(new ApiErrorDto("stale_assessment", "Only a legacy submission without review snapshots can be reopened.", ""));
            assessment.Status = "Returned"; assessment.Revision++; assessment.UpdatedAt = clock.UtcNow.UtcDateTime;
            audit.Record(actor, "assessment.legacy-reopened", "Assessment", assessment.Id,
                JsonSerializer.Serialize(new { assessmentVersion = assessment.Version, revision = assessment.Revision }));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(ContractMapper.ToAssessment(assessment));
        });
        api.MapPost("/assessments/{assessmentId:int}/submit", async Task<IResult> (
            int assessmentId, int authorUserId, int expectedRevision,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SubmitAssessmentRequest? request,
            ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, ApiClock clock, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var assessment = await LoadAssessmentAsync(db, actor, assessmentId, ct);
            if (assessment is null || actor.UserId != authorUserId || !await TenantAccess.CanAuthorAssessmentAsync(db, actor, assessment, ct))
                return Results.NotFound();
            var state = AssessmentState(assessment);
            AssessmentReviewWorkflow.EnsureRevision(state, expectedRevision);
            var issues = AssessmentReviewRules.Validate(AssessmentReviewRules.Parse(state.DocumentJson));
            if (issues.Count > 0) return AssessmentValidationResult(new(issues));
            if (request is null || request.ExpectedRevision != expectedRevision)
                return AssessmentValidationResult(new([new("form", "Select the exact annual Form and saved content before submitting.")]));
            var (person, _, _) = await AssessmentAccessAsync(db, actor, assessment, ct);
            await ValidateAssessmentTargetAsync(db, person!, request, ct);
            var snapshot = await AssessmentReviewWorkflow.SubmitAsync(db, actor.ToAgencyActor(), state, request,
                $"{person!.FirstName} {person.LastName}".Trim(), clock.UtcNow.UtcDateTime, ct);
            assessment.Status = "ReadyForReview"; assessment.SubmittedAt = snapshot.SubmittedAtUtc;
            assessment.UpdatedAt = snapshot.SubmittedAtUtc; assessment.Revision++;
            await db.SaveChangesAsync(ct);
            audit.Record(actor, "assessment.submitted", "Assessment", assessment.Id, AssessmentAudit(snapshot));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(ContractMapper.ToAssessment(assessment));
        });

        api.MapGet("/assessments/review-queue", async Task<IResult> (ClaimsPrincipal principal,
            ApiDbContext db, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            if (!actor.HasSupervisorPermissions) return Results.Forbid();
            if (!await IsComprehensiveAssessmentAuthoringEnabledAsync(db, actor.AgencyId, ct))
                return Results.Ok(Array.Empty<AssessmentQueueItemDto>());
            var rows = await (from assessment in db.ComprehensiveAssessments.AsNoTracking()
                join person in db.People on assessment.PersonId equals person.Id
                join owner in db.Users on person.UserId equals owner.Id
                where assessment.Status == "ReadyForReview" && person.UserId == assessment.AuthorUserId &&
                    person.AgencyId == actor.AgencyId && owner.AgencyId == actor.AgencyId &&
                    assessment.AuthorUserId != actor.UserId &&
                    (actor.HasAgencyWideSupervisionPermissions || owner.SupervisorId == actor.UserId)
                orderby assessment.SubmittedAt, assessment.Id
                select new { assessment, person.FirstName, person.LastName, owner.Id, owner.AgencyId,
                    owner.Permissions, owner.SupervisorId, AuthorName = owner.DisplayName }).Take(200).ToListAsync(ct);
            var queue = new List<AssessmentQueueItemDto>();
            foreach (var row in rows)
            {
                if (!AssessmentReviewRules.CanReview(actor.ToAgencyActor(),
                        new(row.Id, row.AgencyId, row.Permissions, row.SupervisorId), row.assessment.AuthorUserId)) continue;
                var snapshot = await db.Set<AssessmentSubmission>().AsNoTracking()
                    .Where(x => x.AssessmentId == row.assessment.Id && x.AgencyId == actor.AgencyId)
                    .OrderByDescending(x => x.CycleNumber).FirstOrDefaultAsync(ct);
                if (snapshot is null) continue; // Legacy submission has no reviewable version; author must resubmit explicitly.
                queue.Add(new(row.assessment.Id, row.assessment.PersonId, $"{row.FirstName} {row.LastName}".Trim(),
                    row.AuthorName, row.assessment.Version, row.assessment.Revision, snapshot.Id,
                    snapshot.SubmittedAtUtc, snapshot.TargetEffectiveDate));
            }
            return Results.Ok(queue);
        });

        api.MapGet("/assessments/{assessmentId:int}/review", async Task<IResult> (int assessmentId,
            ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            var assessment = await LoadAssessmentAsync(db, actor, assessmentId, ct);
            return assessment is null ? Results.NotFound() : Results.Ok(await AssessmentDetailsAsync(db, assessment, ct));
        });

        api.MapPost("/assessments/{assessmentId:int}/review", async Task<IResult> (int assessmentId,
            AssessmentReviewRequest request, ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail audit, ApiClock clock, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var assessment = await LoadAssessmentAsync(db, actor, assessmentId, ct);
            if (assessment is null) return Results.NotFound();
            var (person, canAuthor, canReview) = await AssessmentAccessAsync(db, actor, assessment, ct);
            var review = await AssessmentReviewWorkflow.ReviewAsync(db, actor.ToAgencyActor(), AssessmentState(assessment),
                request, canReview, canAuthor, clock.UtcNow.UtcDateTime, ct);
            var snapshot = await db.Set<AssessmentSubmission>().AsNoTracking().SingleAsync(x => x.Id == review.SubmissionId, ct);
            if (request.Action == "Approve")
            {
                var form = await ValidateAssessmentTargetAsync(db, person!, new(snapshot.DocumentRevision,
                    snapshot.ContentSha256, snapshot.FormId, snapshot.TargetEffectiveDate, snapshot.DueDate), ct);
                if (request.CompletedOn is DateTime date)
                {
                    var schedule = await AssessmentScheduleAsync(db, actor.AgencyId, ct);
                    var error = AssessmentReviewRules.ValidateCompletion(AssessmentFormFact(form), person!.EffectiveDate!.Value,
                        date, clock.Today, schedule);
                    if (error is not null) throw new AssessmentValidationException([new("completedOn", error)]);
                }
                var pdf = AssessmentPdfGenerator.Generate(AssessmentReviewWorkflow.ToDto(snapshot), true);
                var artifact = await DocumentArtifactPersistence.StageGeneratedAsync(db, assessment.PersonId, actor.AgencyId,
                    AnnualDocumentKind.ComprehensiveAssessment, snapshot.TargetEffectiveDate, DocumentArtifactOrigin.GeneratedInSati,
                    review.RecordedAtUtc, actor.UserId, pdf, AssessmentFileName(snapshot), [], ct,
                    sourceContentId: snapshot.Id, sourceContentVersion: snapshot.CycleNumber);
                review.ArtifactId = artifact.Id;
                assessment.Status = "Approved"; assessment.ApprovedAt = review.RecordedAtUtc; assessment.ApprovedByUserId = actor.UserId;
                if (request.CompletedOn is DateTime completed)
                {
                    form.ApplyAttestation(completed);
                    db.FormAttestations.Add(new ServerFormAttestation
                    {
                        FormId = form.Id, Kind = "Attested", CompletedOn = completed.Date, ActorKind = "Supervisor",
                        ActorUserId = actor.UserId, RecordedAtUtc = review.RecordedAtUtc,
                        PrerequisiteStateJson = AssessmentAttestationEvidence(snapshot, artifact.Id),
                        Reason = "Explicit staff attestation accompanying assessment approval."
                    });
                    audit.Record(actor, "form.attested", "Form", form.Id, AssessmentAudit(snapshot, artifact.Id));
                }
            }
            else if (request.Action == "Return") assessment.Status = "Returned";
            db.Add(review);
            assessment.Revision++; assessment.UpdatedAt = review.RecordedAtUtc;
            audit.Record(actor, $"assessment.{request.Action.ToLowerInvariant()}", "Assessment", assessment.Id,
                AssessmentAudit(snapshot, review.ArtifactId));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(await AssessmentDetailsAsync(db, assessment, ct));
        });

        api.MapPost("/assessments/{assessmentId:int}/submissions/{submissionId:int}/pdf", async Task<IResult> (
            int assessmentId, int submissionId, ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail audit, ApiClock clock, CancellationToken ct) =>
        {
            var actor = Actor.From(principal);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var assessment = await LoadAssessmentAsync(db, actor, assessmentId, ct);
            if (assessment is null) return Results.NotFound();
            var snapshot = await db.Set<AssessmentSubmission>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == submissionId && x.AssessmentId == assessmentId && x.AgencyId == actor.AgencyId, ct);
            if (snapshot is null) return Results.NotFound();
            var approved = await db.Set<AssessmentReviewEvent>().AnyAsync(x => x.SubmissionId == snapshot.Id && x.Action == "Approve", ct);
            var pdf = AssessmentPdfGenerator.Generate(AssessmentReviewWorkflow.ToDto(snapshot), approved);
            var artifact = await DocumentArtifactPersistence.StageGeneratedAsync(db, assessment.PersonId, actor.AgencyId,
                AnnualDocumentKind.ComprehensiveAssessment, snapshot.TargetEffectiveDate,
                approved ? DocumentArtifactOrigin.GeneratedInSati : DocumentArtifactOrigin.Draft,
                clock.UtcNow.UtcDateTime, actor.UserId, pdf, AssessmentFileName(snapshot), [], ct,
                sourceContentId: snapshot.Id, sourceContentVersion: snapshot.CycleNumber);
            audit.Record(actor, "assessment.pdf-generated", "Assessment", assessmentId, AssessmentAudit(snapshot, artifact.Id));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new AssessmentPdfDto(snapshot.Id, artifact.Id, snapshot.ContentSha256,
                Convert.ToHexString(SHA256.HashData(pdf)), AssessmentFileName(snapshot), pdf));
        });
    }

    private static AssessmentWorkflowState AssessmentState(ServerComprehensiveAssessment x) =>
        new(x.Id, x.PersonId, x.AuthorUserId, x.Version, x.Revision, x.Status, x.DocumentJson);
    private static async Task<(ServerPerson? Person, bool Author, bool Reviewer)> AssessmentAccessAsync(
        ApiDbContext db, Actor actor, ServerComprehensiveAssessment assessment, CancellationToken ct)
    {
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assessment.PersonId && x.AgencyId == actor.AgencyId, ct);
        if (person is null) return (null, false, false);
        var owner = await TenantAccess.LoadParticipantAsync(db, person.UserId, ct);
        var author = await TenantAccess.CanAuthorAssessmentAsync(db, actor, assessment, ct);
        var reviewer = person.UserId == assessment.AuthorUserId && owner is not null &&
            AssessmentReviewRules.CanReview(actor.ToAgencyActor(), owner.Value, assessment.AuthorUserId) &&
            await TenantAccess.CanAccessUserAsync(db, actor, owner.Value.UserId, ct);
        return (person, author, reviewer);
    }
    private static async Task<ServerComprehensiveAssessment?> LoadAssessmentAsync(ApiDbContext db, Actor actor, int id, CancellationToken ct)
    {
        if (!await IsComprehensiveAssessmentAuthoringEnabledAsync(db, actor.AgencyId, ct)) return null;
        var assessment = await db.ComprehensiveAssessments.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (assessment is null) return null;
        var access = await AssessmentAccessAsync(db, actor, assessment, ct);
        return access.Author || access.Reviewer ? assessment : null;
    }
    private static async Task<AssessmentReviewDetailsDto> AssessmentDetailsAsync(ApiDbContext db, ServerComprehensiveAssessment assessment, CancellationToken ct) =>
        new(ContractMapper.ToAssessment(assessment),
            (await db.Set<AssessmentSubmission>().AsNoTracking().Where(x => x.AssessmentId == assessment.Id).OrderBy(x => x.CycleNumber).ToListAsync(ct))
                .Select(AssessmentReviewWorkflow.ToDto).ToArray(),
            (await db.Set<AssessmentReviewEvent>().AsNoTracking().Where(x => x.AssessmentId == assessment.Id).OrderBy(x => x.Id).ToListAsync(ct))
                .Select(AssessmentReviewWorkflow.ToDto).ToArray());
    private static FormFact AssessmentFormFact(ServerForm form) => new(form.Id, form.PersonId, form.Type, form.DueDate, form.CompletedDate, form.TargetEffectiveDate);
    private static async Task<ComplianceScheduleSettings> AssessmentScheduleAsync(ApiDbContext db, int agencyId, CancellationToken ct)
    { var settings = await db.Settings.AsNoTracking().SingleAsync(x => x.AgencyId == agencyId, ct); return ToComplianceSchedule(settings); }
    private static async Task<ServerForm> ValidateAssessmentTargetAsync(ApiDbContext db, ServerPerson person, SubmitAssessmentRequest request, CancellationToken ct)
    {
        var form = await db.Forms.SingleOrDefaultAsync(x => x.Id == request.FormId && x.PersonId == person.Id, ct);
        if (form is null) throw new AssessmentValidationException([new("form", "Choose an annual assessment Form on this consumer.")]);
        var error = AssessmentReviewRules.ValidateTarget(person.Id, person.EffectiveDate, AssessmentFormFact(form), request,
            await AssessmentScheduleAsync(db, person.AgencyId!.Value, ct));
        if (error is not null) throw new AssessmentValidationException([new("form", error)]);
        return form;
    }
    private static async Task ValidateAssessmentProvidersAsync(ApiDbContext db, int agencyId, int personId,
        AssessmentDocument document, AssessmentDocument previous, CancellationToken ct)
    {
        var nodes = (await db.Providers.AsNoTracking().Where(x => x.AgencyId == agencyId).ToListAsync(ct)).Select(ToAffiliationNode).ToDictionary(x => x.Id);
        var ids = await db.PersonProviders.AsNoTracking().Where(x => x.PersonId == personId && x.EndDate == null).Select(x => x.ProviderId).ToListAsync(ct);
        var snapshots = ids.Where(nodes.ContainsKey).Distinct().ToDictionary(id => id, id => ProviderAffiliation.Snapshot(id, nodes.Values.ToArray()));
        var issues = AssessmentReviewRules.ValidateProviders(document, previous, snapshots);
        if (issues.Count > 0) throw new AssessmentValidationException(issues);
    }
    private static IResult AssessmentValidationResult(AssessmentValidationException exception) =>
        Results.Json(new { code = "assessment_incomplete", issues = exception.Issues }, statusCode: 422);
    private static string AssessmentAudit(AssessmentSubmission snapshot, int? artifactId = null) => JsonSerializer.Serialize(new
    { submissionId = snapshot.Id, assessmentVersion = snapshot.AssessmentVersion, cycleNumber = snapshot.CycleNumber,
      formId = snapshot.FormId, contentSha256 = snapshot.ContentSha256, rulesVersion = snapshot.RulesVersion, artifactId });
    private static string AssessmentAttestationEvidence(AssessmentSubmission snapshot, int artifactId) => JsonSerializer.Serialize(new
    { assessmentSubmissionId = snapshot.Id, assessmentVersion = snapshot.AssessmentVersion, reviewCycle = snapshot.CycleNumber,
      contentSha256 = snapshot.ContentSha256, documentArtifactId = artifactId });
    private static string AssessmentFileName(AssessmentSubmission snapshot) =>
        $"Assessment-{snapshot.AssessmentId}-v{snapshot.AssessmentVersion}-review-{snapshot.CycleNumber}-submission-{snapshot.Id}.pdf";
}

internal sealed class AssessmentReviewDomainFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (AssessmentValidationException ex)
        { return Results.Json(new { code = "assessment_incomplete", issues = ex.Issues }, statusCode: 422); }
        catch (AssessmentWorkflowException ex)
        { return Results.Json(new ApiErrorDto(ex.Code, ex.Message, string.Empty), statusCode: ex.Status); }
        catch (DbUpdateConcurrencyException) when (context.HttpContext.Request.Path.Value?.Contains("/assessments/") == true)
        { return Results.Conflict(new ApiErrorDto("stale_assessment", "The assessment changed. Reload before continuing.", string.Empty)); }
        catch (Exception ex) when (context.HttpContext.Request.Path.Value?.Contains("/assessments/") == true &&
            (ex is Microsoft.Data.SqlClient.SqlException { Number: 1205 or 1222 } ||
             ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 1205 or 1222 }))
        { return Results.Conflict(new ApiErrorDto("stale_assessment", "Another assessment write is in progress. Reload before continuing.", string.Empty)); }
    }
}
