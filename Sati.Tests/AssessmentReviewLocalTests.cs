using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Models.Assessments;
using Xunit;

namespace Sati.Tests;

public sealed class AssessmentReviewLocalTests
{
    [Fact]
    public async Task NewNoNeedsReasonIsAuthoredWorkAndCannotBeRetiredAsAnEmptyDraft()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync(); await PrepareAsync(fixture);
        var service = new ComprehensiveAssessmentService(fixture.Factory, Session(fixture.CaseManagerOne));
        var draft = await service.GetOrCreateDraftAsync(fixture.PersonOneId, fixture.CaseManagerOne.Id);
        await service.SaveDocumentAsync(draft, new AssessmentDocument { NoIdentifiedNeedsReason = "Synthetic author reasoning in progress." });
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.ComprehensiveAssessments.Add(new ComprehensiveAssessment { PersonId = fixture.PersonOneId,
                AuthorUserId = fixture.CaseManagerOne.Id, Version = draft.Version + 1, Status = AssessmentStatus.Approved });
            await db.SaveChangesAsync();
        }
        var retained = await service.GetOrCreateDraftAsync(fixture.PersonOneId, fixture.CaseManagerOne.Id);
        Assert.Equal(draft.Id, retained.Id);
        Assert.Equal("Synthetic author reasoning in progress.", AssessmentReviewRules.Parse(retained.DocumentJson).NoIdentifiedNeedsReason);
    }
    [Fact]
    public async Task LocalReviewUsesTheSameImmutableSubmissionAndExplicitFormAttestation()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var author = new ComprehensiveAssessmentService(fixture.Factory, Session(fixture.CaseManagerOne));
        var (form, supervisor) = await PrepareAsync(fixture);
        var draft = await author.GetOrCreateDraftAsync(fixture.PersonOneId, fixture.CaseManagerOne.Id);
        await author.SaveDocumentAsync(draft, Complete());
        draft.SubmissionRequest = new(draft.Revision, AssessmentReviewRules.Hash(draft.DocumentJson), form.Id, form.TargetEffectiveDate, form.DueDate);
        await author.SubmitForReviewAsync(draft);
        await Assert.ThrowsAsync<InvalidOperationException>(() => author.SaveDocumentAsync(draft, Complete()));
        var review = new ComprehensiveAssessmentService(fixture.Factory, Session(supervisor));
        var queue = await review.GetReviewQueueAsync(); Assert.Equal(draft.Id, Assert.Single(queue).AssessmentId);
        var details = await review.GetReviewAsync(draft.Id); var snapshot = Assert.Single(details.Submissions);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => review.SaveDocumentAsync(draft, Complete()));
        var approved = await review.ReviewAsync(draft.Id, new(snapshot.Id, details.Assessment.Revision, snapshot.ContentSha256,
            "Approve", CompletedOn: DateTime.Today.AddDays(-2), CompletionAttested: true));
        Assert.Equal("Approved", approved.Assessment.Status);
        var pdf = await author.GeneratePdfAsync(draft.Id, snapshot.Id); Assert.Equal(snapshot.ContentSha256, pdf.ContentSha256);
        await using var db = fixture.Factory.CreateDbContext();
        var saved = await db.Forms.Include(f => f.Attestations).SingleAsync(f => f.Id == form.Id);
        Assert.Equal(DateTime.Today.AddDays(-2), saved.CompletedDate);
        Assert.Contains(snapshot.ContentSha256, Assert.Single(saved.Attestations).PrerequisiteStateJson!);
        var stored = await db.ComprehensiveAssessments.FindAsync(draft.Id); stored!.DocumentJson = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task LocalFailedRevisionCannotSubmitAndCompletenessCannotBeBypassed()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync(); await PrepareAsync(fixture);
        var service = new ComprehensiveAssessmentService(fixture.Factory, Session(fixture.CaseManagerOne));
        var record = await service.GetOrCreateDraftAsync(fixture.PersonOneId, fixture.CaseManagerOne.Id);
        await Assert.ThrowsAsync<AssessmentValidationException>(() => service.SubmitForReviewAsync(record));
        var stale = new ComprehensiveAssessment { Id = record.Id, AuthorUserId = record.AuthorUserId, Revision = record.Revision };
        await service.SaveDocumentAsync(record, Complete());
        var error = await Assert.ThrowsAsync<AssessmentWorkflowException>(() => service.SubmitForReviewAsync(stale));
        Assert.Equal(409, error.Status);
        await using var db = fixture.Factory.CreateDbContext(); Assert.Empty(await db.Set<AssessmentSubmission>().ToArrayAsync());
    }
    [Fact]
    public async Task LocalUnrelatedAgencyCannotReadReviewEvidence()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync(); await PrepareAsync(fixture);
        var author = new ComprehensiveAssessmentService(fixture.Factory, Session(fixture.CaseManagerOne));
        var draft = await author.GetOrCreateDraftAsync(fixture.PersonOneId, fixture.CaseManagerOne.Id);
        await using (var db = fixture.Factory.CreateDbContext())
        { db.Settings.Add(new Settings { AgencyId = fixture.CaseManagerTwo.AgencyId, IsComprehensiveAssessmentAuthoringEnabled = true }); await db.SaveChangesAsync(); }
        var other = new ComprehensiveAssessmentService(fixture.Factory, Session(fixture.CaseManagerTwo));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.GetReviewAsync(draft.Id));
    }
    private static SessionService Session(User user) { var session = new SessionService(); session.SetUser(user); return session; }
    private static AssessmentDocument Complete() => new()
    {
        Contributors = [new() { Name = "Synthetic contributor", Relationship = "Self" }], NoIdentifiedNeedsReason = "No needs identified in this synthetic fixture.",
        Answers = AssessmentCatalog.Questions.ToDictionary(q => q.Key, _ => new AssessmentAnswer { Status = AssessmentAnswerStatus.NotApplicable, ExceptionReason = "Synthetic disposition." })
    };
    private static async Task<(Form Form, User Supervisor)> PrepareAsync(NoteEntryFixture fixture)
    {
        await using var db = fixture.Factory.CreateDbContext();
        var supervisor = User.Create(await db.Users.MaxAsync(u => u.Id) + 1, "synthetic-reviewer", "Synthetic", "Reviewer", "", UserRole.Supervisor, null, fixture.CaseManagerOne.AgencyId);
        db.Users.Add(supervisor); (await db.Users.FindAsync(fixture.CaseManagerOne.Id))!.SupervisorId = supervisor.Id;
        db.Settings.Add(new Settings { AgencyId = fixture.CaseManagerOne.AgencyId, IsComprehensiveAssessmentAuthoringEnabled = true });
        var target = DateTime.Today.AddDays(110); var person = (await db.People.FindAsync(fixture.PersonOneId))!; person.EffectiveDate = target.AddYears(-1);
        var form = new Form(FormType.ComprehensiveAssessment, target.AddDays(-90), targetEffectiveDate: target) { PersonId = person.Id };
        db.Forms.Add(form); await db.SaveChangesAsync(); return (form, supervisor);
    }
}
