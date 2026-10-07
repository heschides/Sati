using Sati.Data;
using Sati.Models;
using Sati.Models.Assessments;
using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class AssessmentSubmissionRegressionTests
{
    [Fact]
    public void FailedSaveCannotSubmitThePreviouslySavedAssessment()
    {
        WpfUiHarness.Run(() =>
        {
            var session = new SessionService();
            session.SetUser(User.Create(31, "synthetic-author", "Synthetic", "Author", "",
                UserRole.CaseManager, null, 1));
            var service = new FailedSaveService();
            var vm = new ComprehensiveAssessmentViewModel(service, session,
                new StabilizationTests.SmokeConsumerProviderService(), new StabilizationTests.SmokeProviderService());
            var person = Person.Rehydrate(123, 31);
            person.Forms.Add(new Form(FormType.ComprehensiveAssessment, DateTime.Today,
                targetEffectiveDate: DateTime.Today.AddDays(90)) { Id = 11, PersonId = 123 });
            vm.LoadPersonAsync(person).GetAwaiter().GetResult();
            vm.AddContributor(); vm.Contributors[0].Name = "Synthetic person"; vm.Contributors[0].Relationship = "Self";
            vm.NoIdentifiedNeedsReason = "Synthetic fixture has no identified needs.";
            foreach (var question in vm.Sections.SelectMany(section => section.Questions))
            {
                question.Status = AssessmentAnswerStatus.NotApplicable;
                question.ExceptionReason = "Synthetic disposition with a documented reason.";
            }
            vm.SubmitForReviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Equal(0, service.SubmissionCount);
            Assert.Equal(1, service.SaveCount);
            Assert.True(vm.CanEdit);
        });
    }

    private sealed class FailedSaveService : IComprehensiveAssessmentService
    {
        public int SubmissionCount { get; private set; }
        public int SaveCount { get; private set; }
        public Task<ComprehensiveAssessment?> GetLatestForAgendaAsync(int personId) => Task.FromResult<ComprehensiveAssessment?>(null);
        public Task<ComprehensiveAssessment> GetOrCreateDraftAsync(int personId, int authorUserId) =>
            Task.FromResult(new ComprehensiveAssessment { Id = 1, PersonId = personId, AuthorUserId = authorUserId });
        public Task SaveDocumentAsync(ComprehensiveAssessment assessment, AssessmentDocument document)
        { SaveCount++; return Task.FromException(new InvalidOperationException("Synthetic refused save")); }
        public Task SubmitForReviewAsync(ComprehensiveAssessment assessment)
        { SubmissionCount++; return Task.CompletedTask; }
    }
}
