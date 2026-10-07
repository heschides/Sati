using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models.Assessments;
using System.Text.Json;
using Xunit;

namespace Sati.Api.Tests;

public sealed class AssessmentReviewApiTests
{
    private static readonly DateTime Today = new(2026, 10, 7);
    private static readonly DateTime Target = new(2026, 12, 31);
    [Fact]
    public async Task EmptyPersistedAssessmentCannotBypassCompletenessBySubmittingDirectly()
    {
        using var factory = new SatiApiFactory();
        using var author = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var response = await author.PostAsync(
            "/api/v1/assessments/701/submit?authorUserId=12&expectedRevision=1", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Equal("Draft", (await db.ComprehensiveAssessments.FindAsync(701))!.Status);
    }

    [Fact]
    public async Task IndependentApprovalAndExplicitAttestationCiteTheSameImmutableVersionAsPdf()
    {
        using var factory = Factory(); using var author = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var reviewer = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var (assessment, formId) = await SaveCompleteAsync(factory, author);
        var details = await SubmitAsync(author, assessment, formId);
        var snapshot = Assert.Single(details.Submissions);
        using var edited = await author.PutAsJsonAsync($"/api/v1/assessments/{assessment.Id}/document", new SaveAssessmentDocumentRequest("{}", details.Assessment.Revision));
        Assert.Equal(HttpStatusCode.Conflict, edited.StatusCode);
        var approved = await ActAsync(reviewer, details, "Approve", completedOn: new DateTime(2026, 10, 1));
        Assert.Equal("Approved", approved.Assessment.Status);
        using var pdfResponse = await author.PostAsJsonAsync($"/api/v1/assessments/{assessment.Id}/submissions/{snapshot.Id}/pdf", new { });
        await Success(pdfResponse); var pdf = (await pdfResponse.Content.ReadFromJsonAsync<AssessmentPdfDto>())!;
        Assert.Equal(snapshot.Id, pdf.SubmissionId); Assert.Equal(snapshot.ContentSha256, pdf.ContentSha256);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Content.AsSpan(0, 4)));
        using (var rendered = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf.Content), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
        {
            Assert.Contains($"Submission {snapshot.Id}", rendered.Info.Subject);
            Assert.Contains(snapshot.ContentSha256, rendered.Info.Subject);
            Assert.Contains($"v{snapshot.AssessmentVersion} review {snapshot.CycleNumber}", rendered.Info.Title);
        }
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var form = await db.Forms.SingleAsync(x => x.Id == formId);
        Assert.Equal(new DateTime(2026, 10, 1), form.CompletedDate);
        Assert.Equal(Target.AddDays(-80), form.DueDate);
        var attestation = await db.FormAttestations.SingleAsync(x => x.FormId == formId);
        using var evidence = JsonDocument.Parse(attestation.PrerequisiteStateJson!);
        Assert.Equal(snapshot.Id, evidence.RootElement.GetProperty("assessmentSubmissionId").GetInt32());
        Assert.Equal(snapshot.ContentSha256, evidence.RootElement.GetProperty("contentSha256").GetString());
        Assert.Equal(approved.Events.Single(x => x.Action == "Approve").ArtifactId, evidence.RootElement.GetProperty("documentArtifactId").GetInt32());
        var artifact = await db.DocumentArtifacts.SingleAsync(x => x.Id == pdf.ArtifactId);
        Assert.Equal(snapshot.Id, artifact.SourceContentId); Assert.Equal(snapshot.CycleNumber, artifact.SourceContentVersion);
        Assert.Equal(pdf.PdfSha256, artifact.ContentSha256);
        var audits = await db.AuditEvents.Where(x => x.Action == "assessment.approve" || x.Action == "assessment.pdf-generated").ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, audit => { Assert.Contains(snapshot.ContentSha256, audit.MetadataJson); Assert.DoesNotContain("Synthetic narrative", audit.MetadataJson); });
        var stored = await db.ComprehensiveAssessments.SingleAsync(x => x.Id == assessment.Id);
        stored.DocumentJson = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ApprovalAloneDoesNotCompleteTheFormOrClaimSignatures()
    {
        using var factory = Factory(); using var author = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var reviewer = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var (assessment, formId) = await SaveCompleteAsync(factory, author);
        var details = await SubmitAsync(author, assessment, formId);
        await ActAsync(reviewer, details, "Approve");
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Null((await db.Forms.FindAsync(formId))!.CompletedDate);
        Assert.False(await db.FormAttestations.AnyAsync(x => x.FormId == formId));
        Assert.False(await db.SignatureCompletions.AnyAsync());
    }

    [Theory]
    [InlineData("case-manager-one", HttpStatusCode.Forbidden)]
    [InlineData("billing-only-one", HttpStatusCode.Forbidden)]
    [InlineData("supervisor-two", HttpStatusCode.OK)]
    public async Task UnauthorizedReviewersCannotDiscoverSubmittedContent(string login, HttpStatusCode queueStatus)
    {
        using var factory = Factory(); using var author = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var (assessment, formId) = await SaveCompleteAsync(factory, author); await SubmitAsync(author, assessment, formId);
        using var intruder = await factory.CreateAuthenticatedClientAsync(login);
        using var queue = await intruder.GetAsync("/api/v1/assessments/review-queue"); Assert.Equal(queueStatus, queue.StatusCode);
        if (queueStatus == HttpStatusCode.OK) Assert.Empty((await queue.Content.ReadFromJsonAsync<List<AssessmentQueueItemDto>>())!);
        if (login != "case-manager-one")
        {
            using var read = await intruder.GetAsync($"/api/v1/assessments/{assessment.Id}/review"); Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        }
    }

    [Fact]
    public async Task SupervisorWithOwnCaseloadCanAuthorButCannotReviewSelf()
    {
        using var factory = Factory(); using var seedAuthor = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await SaveCompleteAsync(factory, seedAuthor);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            (await db.People.FindAsync(101))!.UserId = 13;
            var record = (await db.ComprehensiveAssessments.FindAsync(701))!; record.AuthorUserId = 13;
            (await db.Users.FindAsync(13))!.Permissions |= UserPermissions.AgencyWideSupervision;
            await db.SaveChangesAsync();
        }
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var row = (await supervisor.GetFromJsonAsync<AssessmentReviewDetailsDto>("/api/v1/assessments/701/review"))!.Assessment;
        var formId = await FormIdAsync(factory);
        var details = await SubmitAsync(supervisor, row, formId);
        using var self = await supervisor.PostAsJsonAsync("/api/v1/assessments/701/review", Request(details, "Approve"));
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.Empty((await supervisor.GetFromJsonAsync<List<AssessmentQueueItemDto>>("/api/v1/assessments/review-queue"))!);
    }

    [Fact]
    public async Task ReturnResubmissionAndFlagResolutionPreserveEveryCycleAndStaleApprovalFails()
    {
        using var factory = Factory(); using var author = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var reviewer = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var (assessment, formId) = await SaveCompleteAsync(factory, author); var first = await SubmitAsync(author, assessment, formId);
        var flagged = await ActAsync(reviewer, first, "Flag", "Explain this synthetic disposition", blocking: true);
        using var stale = await reviewer.PostAsJsonAsync($"/api/v1/assessments/{assessment.Id}/review", Request(first, "Approve"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var blocked = await reviewer.PostAsJsonAsync($"/api/v1/assessments/{assessment.Id}/review", Request(flagged, "Approve"));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var returned = await ActAsync(reviewer, flagged, "Return", "Clarify the highlighted response.");
        var flagId = returned.Events.Single(x => x.Action == "Flag").Id;
        var responded = await ActAsync(author, returned, "Respond", "The author supplied a clarification.", flagId: flagId);
        var doc = CompleteDocument(); doc.Answers["self-view"] = new() { Status = AssessmentAnswerStatus.Answered, Narrative = "Synthetic narrative changed only by its author." };
        using var save = await author.PutAsJsonAsync($"/api/v1/assessments/{assessment.Id}/document", new SaveAssessmentDocumentRequest(JsonSerializer.Serialize(doc, AssessmentReviewRules.JsonOptions), responded.Assessment.Revision));
        await Success(save); var updated = (await save.Content.ReadFromJsonAsync<ComprehensiveAssessmentDto>())!;
        var second = await SubmitAsync(author, updated, formId);
        Assert.Equal(2, second.Submissions.Count); Assert.NotEqual(second.Submissions[0].ContentSha256, second.Submissions[1].ContentSha256);
        using var stillBlocked = await reviewer.PostAsJsonAsync($"/api/v1/assessments/{assessment.Id}/review", Request(second, "Approve"));
        Assert.Equal(HttpStatusCode.Conflict, stillBlocked.StatusCode);
        var resolved = await ActAsync(reviewer, second, "Resolve", "Reviewed the author's clarification.", flagId: flagId);
        var approved = await ActAsync(reviewer, resolved, "Approve");
        Assert.Equal(first.Submissions[0], approved.Submissions[0]);
        Assert.Equal(new[] { "Flag", "Return", "Respond", "Resolve", "Approve" }, approved.Events.Select(x => x.Action));
        using var oldPdf = await author.PostAsJsonAsync($"/api/v1/assessments/{assessment.Id}/submissions/{first.Submissions[0].Id}/pdf", new { }); await Success(oldPdf);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var active = await db.DocumentArtifacts.SingleAsync(x => x.Kind == "ComprehensiveAssessment" && x.SupersededByArtifactId == null);
        Assert.Equal(second.Submissions[1].Id, active.SourceContentId);
        var old = await db.Set<AssessmentSubmission>().SingleAsync(x => x.Id == first.Submissions[0].Id);
        old.DocumentJson = "{}"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ChangedDeadlineAndInvalidAttestationLeaveApprovalAuditArtifactAndFormUnchanged()
    {
        using var factory = Factory(); using var author = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var reviewer = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var (assessment, formId) = await SaveCompleteAsync(factory, author); var details = await SubmitAsync(author, assessment, formId);
        using var badDate = await reviewer.PostAsJsonAsync("/api/v1/assessments/701/review", Request(details, "Approve", completedOn: Today.AddDays(1)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badDate.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        { var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>(); var settings = await db.Settings.SingleAsync(x => x.AgencyId == 1); settings.CompAssessmentDaysBeforeAnniversary = 90; await db.SaveChangesAsync(); }
        using var changed = await reviewer.PostAsJsonAsync("/api/v1/assessments/701/review", Request(details, "Approve"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, changed.StatusCode);
        await using var verification = factory.Services.CreateAsyncScope(); var stored = verification.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Equal("ReadyForReview", (await stored.ComprehensiveAssessments.FindAsync(701))!.Status);
        Assert.False(await stored.Set<AssessmentReviewEvent>().AnyAsync()); Assert.False(await stored.DocumentArtifacts.AnyAsync());
        Assert.False(await stored.AuditEvents.AnyAsync(x => x.Action == "assessment.approve"));
        Assert.Null((await stored.Forms.FindAsync(formId))!.CompletedDate);
    }

    [Fact]
    public async Task AConcurrentNonblockingCommentInvalidatesAnOlderApproval()
    {
        using var factory = Factory(); using var author = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var reviewer = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var (assessment, formId) = await SaveCompleteAsync(factory, author); var first = await SubmitAsync(author, assessment, formId);
        await ActAsync(reviewer, first, "Comment", "A newer nonblocking review observation.");
        using var stale = await reviewer.PostAsJsonAsync($"/api/v1/assessments/{assessment.Id}/review", Request(first, "Approve"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = (await reviewer.GetFromJsonAsync<AssessmentReviewDetailsDto>($"/api/v1/assessments/{assessment.Id}/review"))!;
        Assert.Equal("ReadyForReview", current.Assessment.Status);
        Assert.DoesNotContain(current.Events, x => x.Action == "Approve");
    }

    private static SatiApiFactory Factory() => new() { ClockOverride = new FrozenClock() };
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsumerDeletionCannotEraseAssessmentReviewEvidence(bool testDataTool)
    {
        using var factory = Factory(); using var author = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var (assessment, formId) = await SaveCompleteAsync(factory, author); await SubmitAsync(author, assessment, formId);
        int revision;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE People SET IsTestData=1 WHERE Id=101;");
            revision = (await db.People.FindAsync(101))!.Revision;
        }
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var response = testDataTool
            ? await admin.PostAsJsonAsync("/api/v1/admin/test-data/consumers/101/delete", new DeleteTestConsumerRequest(revision, TestDataDeletionRules.ConsumerAttestation))
            : await admin.PostAsJsonAsync("/api/v1/admin/consumers/101/delete-in-window", new DeleteConsumerInWindowRequest(revision, ConsumerDeletionRules.ConsumerAttestation, "Synthetic deletion attempt"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("consumer_has_assessment_review_history", (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        var details = (await author.GetFromJsonAsync<AssessmentReviewDetailsDto>("/api/v1/assessments/701/review"))!;
        Assert.Single(details.Submissions);
    }
    [Fact]
    public async Task LegacySubmissionCanBeReopenedOnlyByItsAuthorBeforeAnySnapshotExists()
    {
        using var factory = Factory(); using var author = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var reviewer = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var (assessment, formId) = await SaveCompleteAsync(factory, author);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            (await db.ComprehensiveAssessments.FindAsync(701))!.Status = "ReadyForReview";
            await db.SaveChangesAsync();
        }
        using var denied = await reviewer.PostAsJsonAsync("/api/v1/assessments/701/reopen-legacy", new ReopenLegacyAssessmentRequest(assessment.Revision));
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var reopened = await author.PostAsJsonAsync("/api/v1/assessments/701/reopen-legacy", new ReopenLegacyAssessmentRequest(assessment.Revision));
        await Success(reopened); var row = (await reopened.Content.ReadFromJsonAsync<ComprehensiveAssessmentDto>())!;
        Assert.Equal("Returned", row.Status); Assert.Equal(assessment.DocumentJson, row.DocumentJson);
        var details = await SubmitAsync(author, row, formId);
        using var tracked = await author.PostAsJsonAsync("/api/v1/assessments/701/reopen-legacy", new ReopenLegacyAssessmentRequest(details.Assessment.Revision));
        Assert.Equal(HttpStatusCode.Conflict, tracked.StatusCode);
    }
    [Fact]
    public async Task FailureWritingReviewEventRollsBackApprovalArtifactAttestationAndAudit()
    {
        using var factory = Factory(); using var author = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var reviewer = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var (assessment, formId) = await SaveCompleteAsync(factory, author);
        var details = await SubmitAsync(author, assessment, formId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER SyntheticAssessmentApprovalFailure BEFORE INSERT ON AssessmentReviewEvents
                WHEN NEW.Action = 'Approve' BEGIN SELECT RAISE(ABORT, 'Synthetic final-write failure'); END;
                """);
        }
        using var failed = await reviewer.PostAsJsonAsync("/api/v1/assessments/701/review",
            Request(details, "Approve", completedOn: Today.AddDays(-2)));
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await using var verification = factory.Services.CreateAsyncScope();
        var stored = verification.ServiceProvider.GetRequiredService<ApiDbContext>();
        var row = (await stored.ComprehensiveAssessments.FindAsync(701))!;
        Assert.Equal("ReadyForReview", row.Status); Assert.Equal(details.Assessment.Revision, row.Revision);
        Assert.False(await stored.Set<AssessmentReviewEvent>().AnyAsync());
        Assert.False(await stored.DocumentArtifacts.AnyAsync());
        Assert.False(await stored.FormAttestations.AnyAsync(x => x.FormId == formId));
        Assert.False(await stored.AuditEvents.AnyAsync(x => x.Action == "assessment.approve" || x.Action == "form.attested"));
        Assert.Null((await stored.Forms.FindAsync(formId))!.CompletedDate);
    }
    private sealed class FrozenClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Today.AddHours(16), TimeSpan.Zero); }
    internal static AssessmentDocument CompleteDocument() => new()
    {
        Contributors = [new() { Name = "Synthetic contributor", Relationship = "Self" }],
        NoIdentifiedNeedsReason = "No additional needs identified in this synthetic fixture.",
        Answers = AssessmentCatalog.Questions.ToDictionary(q => q.Key, q => new AssessmentAnswer
        { Status = AssessmentAnswerStatus.NotApplicable, ExceptionReason = "Documented synthetic disposition." })
    };
    private static async Task<(ComprehensiveAssessmentDto Assessment, int FormId)> SaveCompleteAsync(SatiApiFactory factory, HttpClient author)
    {
        int formId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var person = (await db.People.FindAsync(101))!; person.EffectiveDate = Target.AddYears(-1);
            var settings = await db.Settings.SingleAsync(x => x.AgencyId == 1); settings.CompAssessmentDaysBeforeAnniversary = 80;
            var form = new ServerForm { PersonId = 101, Type = "ComprehensiveAssessment", TargetEffectiveDate = Target, DueDate = Target.AddDays(-80) };
            db.Forms.Add(form); await db.SaveChangesAsync(); formId = form.Id;
        }
        using var response = await author.PutAsJsonAsync("/api/v1/assessments/701/document",
            new SaveAssessmentDocumentRequest(JsonSerializer.Serialize(CompleteDocument(), AssessmentReviewRules.JsonOptions), 1));
        await Success(response); return ((await response.Content.ReadFromJsonAsync<ComprehensiveAssessmentDto>())!, formId);
    }
    private static async Task<int> FormIdAsync(SatiApiFactory factory)
    { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<ApiDbContext>().Forms.Where(x => x.PersonId == 101 && x.TargetEffectiveDate == Target && x.Type == "ComprehensiveAssessment").Select(x => x.Id).SingleAsync(); }
    private static async Task<AssessmentReviewDetailsDto> SubmitAsync(HttpClient author, ComprehensiveAssessmentDto assessment, int formId)
    {
        using var submit = await author.PostAsJsonAsync($"/api/v1/assessments/{assessment.Id}/submit?authorUserId={assessment.AuthorUserId}&expectedRevision={assessment.Revision}",
            new SubmitAssessmentRequest(assessment.Revision, AssessmentReviewRules.Hash(assessment.DocumentJson), formId, Target, Target.AddDays(-80)));
        await Success(submit); return (await author.GetFromJsonAsync<AssessmentReviewDetailsDto>($"/api/v1/assessments/{assessment.Id}/review"))!;
    }
    private static AssessmentReviewRequest Request(AssessmentReviewDetailsDto details, string action, string text = "", bool blocking = false, long? flagId = null, DateTime? completedOn = null) =>
        new(details.Submissions.Last().Id, details.Assessment.Revision, details.Submissions.Last().ContentSha256, action,
            "self-view", text, blocking, flagId, completedOn, completedOn is not null);
    private static async Task<AssessmentReviewDetailsDto> ActAsync(HttpClient actor, AssessmentReviewDetailsDto details,
        string action, string text = "", bool blocking = false, long? flagId = null, DateTime? completedOn = null)
    {
        using var response = await actor.PostAsJsonAsync($"/api/v1/assessments/{details.Assessment.Id}/review", Request(details, action, text, blocking, flagId, completedOn));
        await Success(response); return (await response.Content.ReadFromJsonAsync<AssessmentReviewDetailsDto>())!;
    }
    private static async Task Success(HttpResponseMessage response) => Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
}
