using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Forms;
using Sati.Models;
using Sati.Models.Assessments;

namespace Sati.Data;

public sealed partial class ComprehensiveAssessmentService
{
    private static AgencyActor Actor(User user) => new(user.Id, user.AgencyId, user.Permissions, user.SecurityVersion);
    private static AssessmentWorkflowState State(ComprehensiveAssessment x) =>
        new(x.Id, x.PersonId, x.AuthorUserId, x.Version, x.Revision, x.Status.ToString(), x.DocumentJson);
    public async Task<ComprehensiveAssessmentDto> ReopenLegacyAsync(int assessmentId, int expectedRevision)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var actor = await LocalTenantAccess.EnsureSessionAsync(db, Session);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var assessment = await LoadAsync(db, actor, assessmentId);
        await EnsureCanAuthorAsync(db, actor, assessment);
        AssessmentReviewWorkflow.EnsureRevision(State(assessment), expectedRevision);
        if (!AssessmentReviewRules.CanReopenLegacy(assessment.Status.ToString(),
                await db.Set<AssessmentSubmission>().AnyAsync(x => x.AssessmentId == assessmentId)))
            throw new AssessmentWorkflowException(409, "stale_assessment", "Only a legacy submission without review snapshots can be reopened.");
        assessment.Status = AssessmentStatus.Returned; assessment.Revision++; assessment.UpdatedAt = DateTime.UtcNow;
        LocalAuditTrail.Record(db, actor, "assessment.legacy-reopened", "ComprehensiveAssessment", assessment.Id,
            JsonSerializer.Serialize(new { assessmentVersion = assessment.Version, revision = assessment.Revision }));
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return ToDto(assessment);
    }
    private async Task SubmitCoreAsync(ComprehensiveAssessment assessment)
    {
        var actor = CurrentAuthor(assessment.AuthorUserId);
        await using var db = await Factory.CreateDbContextAsync();
        await LocalTenantAccess.EnsureSessionAsync(db, Session); await EnsureAuthoringEnabledAsync(db, actor.AgencyId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var stored = await db.ComprehensiveAssessments.Include(x => x.Person).SingleAsync(x => x.Id == assessment.Id);
        await EnsureCanAuthorAsync(db, actor, stored);
        AssessmentReviewWorkflow.EnsureRevision(State(stored), assessment.Revision);
        var issues = AssessmentReviewRules.Validate(AssessmentReviewRules.Parse(stored.DocumentJson));
        if (issues.Count > 0) throw new AssessmentValidationException(issues);
        var request = assessment.SubmissionRequest ?? throw new AssessmentValidationException([new("form", "Choose the exact annual assessment Form before submitting.")]);
        await ValidateTargetAsync(db, stored.Person, request);
        var snapshot = await AssessmentReviewWorkflow.SubmitAsync(db, Actor(actor), State(stored), request, stored.Person.FullName, DateTime.UtcNow);
        stored.Status = AssessmentStatus.ReadyForReview; stored.SubmittedAt = snapshot.SubmittedAtUtc;
        stored.UpdatedAt = snapshot.SubmittedAtUtc; stored.Revision++;
        await db.SaveChangesAsync();
        LocalAuditTrail.Record(db, actor, "assessment.submitted", "ComprehensiveAssessment", stored.Id, Audit(snapshot));
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        assessment.Status = stored.Status; assessment.SubmittedAt = stored.SubmittedAt;
        assessment.UpdatedAt = stored.UpdatedAt; assessment.Revision = stored.Revision;
    }
    public async Task<IReadOnlyList<AssessmentQueueItemDto>> GetReviewQueueAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        var actor = await LocalTenantAccess.EnsureSessionAsync(db, Session);
        if (!actor.HasSupervisorPermissions) throw new UnauthorizedAccessException();
        await EnsureAuthoringEnabledAsync(db, actor.AgencyId);
        var rows = await (from assessment in db.ComprehensiveAssessments.AsNoTracking()
            join person in db.People on assessment.PersonId equals person.Id
            join owner in db.Users on person.UserId equals owner.Id
            where assessment.Status == AssessmentStatus.ReadyForReview && person.UserId == assessment.AuthorUserId &&
                person.AgencyId == actor.AgencyId && owner.AgencyId == actor.AgencyId && assessment.AuthorUserId != actor.Id &&
                (UserPermissionRules.HasAgencyWideSupervisionPermissions(actor.Permissions) || owner.SupervisorId == actor.Id)
            orderby assessment.SubmittedAt, assessment.Id
            select new { assessment, person.FirstName, person.LastName, owner.Id, owner.AgencyId, owner.Permissions,
                owner.SupervisorId, AuthorName = owner.DisplayName }).Take(200).ToListAsync();
        var queue = new List<AssessmentQueueItemDto>();
        foreach (var row in rows)
        {
            if (!AssessmentReviewRules.CanReview(Actor(actor), new(row.Id, row.AgencyId, row.Permissions, row.SupervisorId), row.assessment.AuthorUserId)) continue;
            var snapshot = await db.Set<AssessmentSubmission>().AsNoTracking().Where(x => x.AssessmentId == row.assessment.Id && x.AgencyId == actor.AgencyId)
                .OrderByDescending(x => x.CycleNumber).FirstOrDefaultAsync();
            if (snapshot is null) continue;
            queue.Add(new(row.assessment.Id, row.assessment.PersonId, $"{row.FirstName} {row.LastName}".Trim(), row.AuthorName,
                row.assessment.Version, row.assessment.Revision, snapshot.Id, snapshot.SubmittedAtUtc, snapshot.TargetEffectiveDate));
        }
        return queue;
    }
    public async Task<AssessmentReviewDetailsDto> GetReviewAsync(int assessmentId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var actor = await LocalTenantAccess.EnsureSessionAsync(db, Session);
        var assessment = await LoadAsync(db, actor, assessmentId);
        return await DetailsAsync(db, assessment);
    }
    public async Task<AssessmentReviewDetailsDto> ReviewAsync(int assessmentId, AssessmentReviewRequest request)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var actor = await LocalTenantAccess.EnsureSessionAsync(db, Session);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var assessment = await LoadAsync(db, actor, assessmentId);
        var access = await AccessAsync(db, actor, assessment);
        var review = await AssessmentReviewWorkflow.ReviewAsync(db, Actor(actor), State(assessment), request,
            access.Reviewer, access.Author, DateTime.UtcNow);
        var snapshot = await db.Set<AssessmentSubmission>().AsNoTracking().SingleAsync(x => x.Id == review.SubmissionId);
        if (request.Action == "Approve")
        {
            var form = await ValidateTargetAsync(db, assessment.Person, new(snapshot.DocumentRevision, snapshot.ContentSha256,
                snapshot.FormId, snapshot.TargetEffectiveDate, snapshot.DueDate));
            if (request.CompletedOn is DateTime date)
            {
                var schedule = await ScheduleAsync(db, actor.AgencyId);
                var error = AssessmentReviewRules.ValidateCompletion(Fact(form), assessment.Person.EffectiveDate!.Value,
                    date, DateTime.Today, schedule);
                if (error is not null) throw new AssessmentValidationException([new("completedOn", error)]);
            }
            var pdf = AssessmentPdfGenerator.Generate(AssessmentReviewWorkflow.ToDto(snapshot), true);
            var artifact = await DocumentArtifactStore.StageGeneratedAsync(db, assessment.PersonId, actor.AgencyId,
                AnnualDocumentKind.ComprehensiveAssessment, snapshot.TargetEffectiveDate, DocumentArtifactOrigin.GeneratedInSati,
                review.RecordedAtUtc, actor.Id, pdf, FileName(snapshot), [], default,
                sourceContentId: snapshot.Id, sourceContentVersion: snapshot.CycleNumber);
            review.ArtifactId = artifact.Id;
            assessment.Status = AssessmentStatus.Approved; assessment.ApprovedAt = review.RecordedAtUtc; assessment.ApprovedByUserId = actor.Id;
            if (request.CompletedOn is DateTime completed)
            {
                form.Attest(FormAttestation.Attested(completed, AttestationActorKind.Supervisor, actor.Id, review.RecordedAtUtc,
                    prerequisiteStateJson: Evidence(snapshot, artifact.Id), reason: "Explicit staff attestation accompanying assessment approval."));
                LocalAuditTrail.Record(db, actor, "form.attested", "Form", form.Id, Audit(snapshot, artifact.Id));
            }
        }
        else if (request.Action == "Return") assessment.Status = AssessmentStatus.Returned;
        db.Add(review);
        assessment.Revision++; assessment.UpdatedAt = review.RecordedAtUtc;
        LocalAuditTrail.Record(db, actor, $"assessment.{request.Action.ToLowerInvariant()}", "ComprehensiveAssessment", assessment.Id, Audit(snapshot, review.ArtifactId));
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return await DetailsAsync(db, assessment);
    }
    public async Task<AssessmentPdfDto> GeneratePdfAsync(int assessmentId, int submissionId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var actor = await LocalTenantAccess.EnsureSessionAsync(db, Session);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var assessment = await LoadAsync(db, actor, assessmentId);
        var snapshot = await db.Set<AssessmentSubmission>().AsNoTracking().SingleAsync(x => x.Id == submissionId && x.AssessmentId == assessmentId && x.AgencyId == actor.AgencyId);
        var approved = await db.Set<AssessmentReviewEvent>().AnyAsync(x => x.SubmissionId == snapshot.Id && x.Action == "Approve");
        var pdf = AssessmentPdfGenerator.Generate(AssessmentReviewWorkflow.ToDto(snapshot), approved);
        var artifact = await DocumentArtifactStore.StageGeneratedAsync(db, assessment.PersonId, actor.AgencyId,
            AnnualDocumentKind.ComprehensiveAssessment, snapshot.TargetEffectiveDate,
            approved ? DocumentArtifactOrigin.GeneratedInSati : DocumentArtifactOrigin.Draft,
            DateTime.UtcNow, actor.Id, pdf, FileName(snapshot), [], default,
            sourceContentId: snapshot.Id, sourceContentVersion: snapshot.CycleNumber);
        LocalAuditTrail.Record(db, actor, "assessment.pdf-generated", "ComprehensiveAssessment", assessment.Id, Audit(snapshot, artifact.Id));
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return new(snapshot.Id, artifact.Id, snapshot.ContentSha256, Convert.ToHexString(SHA256.HashData(pdf)), FileName(snapshot), pdf);
    }
    private static async Task<(bool Author, bool Reviewer)> AccessAsync(SatiContext db, User actor, ComprehensiveAssessment assessment)
    {
        var author = assessment.AuthorUserId == actor.Id && await LocalTenantAccess.OwnsPersonAsync(db, actor, assessment.PersonId);
        var person = assessment.Person;
        var owner = await db.Users.AsNoTracking().Where(x => x.Id == person.UserId)
            .Select(x => new CaseloadParticipant(x.Id, x.AgencyId, x.Permissions, x.SupervisorId)).SingleOrDefaultAsync();
        var reviewer = person.AgencyId == actor.AgencyId && person.UserId == assessment.AuthorUserId &&
            AssessmentReviewRules.CanReview(Actor(actor), owner, assessment.AuthorUserId) &&
            await LocalTenantAccess.CanAccessUserAsync(db, actor, owner.UserId);
        return (author, reviewer);
    }
    private static async Task<ComprehensiveAssessment> LoadAsync(SatiContext db, User actor, int id)
    {
        await EnsureAuthoringEnabledAsync(db, actor.AgencyId);
        var assessment = await db.ComprehensiveAssessments.Include(x => x.Person).SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new UnauthorizedAccessException();
        var access = await AccessAsync(db, actor, assessment);
        if (!access.Author && !access.Reviewer) throw new UnauthorizedAccessException();
        return assessment;
    }
    private static ComprehensiveAssessmentDto ToDto(ComprehensiveAssessment x) => new(x.Id, x.PersonId, x.AuthorUserId,
        x.Status.ToString(), x.Version, x.CreatedAt, x.UpdatedAt, x.SubmittedAt, x.ApprovedAt, x.ApprovedByUserId, x.DocumentJson, x.Revision);
    private static async Task<AssessmentReviewDetailsDto> DetailsAsync(SatiContext db, ComprehensiveAssessment x) => new(ToDto(x),
        (await db.Set<AssessmentSubmission>().AsNoTracking().Where(s => s.AssessmentId == x.Id).OrderBy(s => s.CycleNumber).ToListAsync()).Select(AssessmentReviewWorkflow.ToDto).ToArray(),
        (await db.Set<AssessmentReviewEvent>().AsNoTracking().Where(e => e.AssessmentId == x.Id).OrderBy(e => e.Id).ToListAsync()).Select(AssessmentReviewWorkflow.ToDto).ToArray());
    private static FormFact Fact(Form form) => new(form.Id, form.PersonId, form.Type.ToString(), form.DueDate, form.CompletedDate, form.TargetEffectiveDate);
    private static async Task<ComplianceScheduleSettings> ScheduleAsync(SatiContext db, int agencyId)
    { var s = await db.Settings.AsNoTracking().SingleAsync(x => x.AgencyId == agencyId); return new(s.ReviewOpenDaysBefore, s.PcpOpenDaysBefore,
        s.CompAssessmentOpenDaysBefore, s.ReclassificationOpenDaysBefore, s.SafetyPlanOpenDaysBefore, s.PrivacyPracticesOpenDaysBefore,
        s.ReleaseAgencyOpenDaysBefore, s.ReleaseDhhsOpenDaysBefore, s.ReleaseMedicalOpenDaysBefore,
        s.PcpDaysBeforeAnniversary, s.CompAssessmentDaysBeforeAnniversary, s.ReclassificationDaysBeforeAnniversary,
        s.SafetyPlanDaysBeforeAnniversary, s.PrivacyPracticesDaysBeforeAnniversary, s.ReleaseAgencyDaysBeforeAnniversary,
        s.ReleaseDhhsDaysBeforeAnniversary, s.ReleaseMedicalDaysBeforeAnniversary); }
    private static async Task<Form> ValidateTargetAsync(SatiContext db, Person person, SubmitAssessmentRequest request)
    {
        var form = await db.Forms.SingleOrDefaultAsync(x => x.Id == request.FormId && x.PersonId == person.Id)
            ?? throw new AssessmentValidationException([new("form", "Choose an annual assessment Form on this consumer.")]);
        var error = AssessmentReviewRules.ValidateTarget(person.Id, person.EffectiveDate, Fact(form), request, await ScheduleAsync(db, person.AgencyId!.Value));
        if (error is not null) throw new AssessmentValidationException([new("form", error)]); return form;
    }
    private static async Task ValidateProvidersAsync(SatiContext db, int agencyId, int personId, AssessmentDocument document, AssessmentDocument previous)
    {
        var nodes = (await db.Providers.AsNoTracking().Where(x => x.AgencyId == agencyId).ToListAsync()).Select(x =>
            new ProviderAffiliationNode(x.Id, x.Name, x.ParentProviderId, x.MedicalKind)).ToDictionary(x => x.Id);
        var ids = await db.PersonProviders.AsNoTracking().Where(x => x.PersonId == personId && x.EndDate == null).Select(x => x.ProviderId).ToListAsync();
        var snapshots = ids.Where(nodes.ContainsKey).Distinct().ToDictionary(id => id, id => ProviderAffiliation.Snapshot(id, nodes.Values.ToArray()));
        var issues = AssessmentReviewRules.ValidateProviders(document, previous, snapshots);
        if (issues.Count > 0) throw new AssessmentValidationException(issues);
    }
    private static string Audit(AssessmentSubmission snapshot, int? artifactId = null) => JsonSerializer.Serialize(new
    { submissionId = snapshot.Id, assessmentVersion = snapshot.AssessmentVersion, cycleNumber = snapshot.CycleNumber,
      formId = snapshot.FormId, contentSha256 = snapshot.ContentSha256, rulesVersion = snapshot.RulesVersion, artifactId });
    private static string Evidence(AssessmentSubmission snapshot, int artifactId) => JsonSerializer.Serialize(new
    { assessmentSubmissionId = snapshot.Id, assessmentVersion = snapshot.AssessmentVersion, reviewCycle = snapshot.CycleNumber,
      contentSha256 = snapshot.ContentSha256, documentArtifactId = artifactId });
    private static string FileName(AssessmentSubmission snapshot) =>
        $"Assessment-{snapshot.AssessmentId}-v{snapshot.AssessmentVersion}-review-{snapshot.CycleNumber}-submission-{snapshot.Id}.pdf";
}
