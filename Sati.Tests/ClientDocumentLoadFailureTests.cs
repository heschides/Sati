using Sati.Data;
using Sati.Models;
using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The assessment and PCP workspaces load on every client selection without awaiting the
/// load. In 1.3.14 a refused read (authoring switched off for the agency) escaped as an
/// unobserved task (production references A92EC7663A79, C7D6AB4D479A). A failed load now
/// finishes quietly, with a reference the case manager can read.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ClientDocumentLoadFailureTests
{
    [Fact]
    public async Task ARefusedPlanSourceReadIsReportedOnTheWorkspace()
    {
        var viewModel = new PersonCenteredPlanViewModel(new RefusingPlanSource(), SignedInCaseManager());

        await viewModel.LoadPersonAsync(Person.Rehydrate(123, 31));

        Assert.False(viewModel.HasSource);
        Assert.False(viewModel.IsLoading);
        Assert.StartsWith("The assessment source could not be loaded. Reference ", viewModel.SourceNotice);
    }

    [Fact]
    public void ARefusedAssessmentDraftLeavesTheWorkspaceReadOnly()
    {
        // The assessment ViewModel owns a DispatcherTimer, so it lives on the UI thread.
        // Every awaited call here completes synchronously.
        WpfUiHarness.Run(() =>
        {
            var viewModel = new ComprehensiveAssessmentViewModel(
                new RefusingAssessmentService(),
                SignedInCaseManager(),
                new StabilizationTests.SmokeConsumerProviderService(),
                new StabilizationTests.SmokeProviderService());

            var load = viewModel.LoadPersonAsync(Person.Rehydrate(123, 31));
            Assert.True(load.IsCompleted);
            load.GetAwaiter().GetResult();

            Assert.False(viewModel.CanEdit);
            Assert.StartsWith("The assessment could not be loaded. Reference ", viewModel.SaveStatus);
        });
    }

    private static SessionService SignedInCaseManager()
    {
        var session = new SessionService();
        session.SetUser(User.Create(31, "casemanager", "Case", "Manager", string.Empty,
            UserRole.CaseManager, null, 1));
        return session;
    }

    private sealed class RefusingPlanSource : IPersonCenteredPlanSourceService
    {
        public Task<PersonCenteredPlanSource?> GetSourceAsync(int personId, int preferredAuthorUserId) =>
            Task.FromException<PersonCenteredPlanSource?>(
                new NotSupportedException("Person-Centered Plan authoring is not enabled for this agency."));
    }

    private sealed class RefusingAssessmentService : IComprehensiveAssessmentService
    {
        private static Task<T> Refuse<T>() => Task.FromException<T>(
            new NotSupportedException("Comprehensive Assessment authoring is not enabled for this agency."));

        public Task<Models.Assessments.ComprehensiveAssessment?> GetLatestForAgendaAsync(int personId) =>
            Refuse<Models.Assessments.ComprehensiveAssessment?>();
        public Task<Models.Assessments.ComprehensiveAssessment> GetOrCreateDraftAsync(int personId, int authorUserId) =>
            Refuse<Models.Assessments.ComprehensiveAssessment>();
        public Task SaveDocumentAsync(
            Models.Assessments.ComprehensiveAssessment assessment,
            Models.Assessments.AssessmentDocument document) => Refuse<bool>();
        public Task SubmitForReviewAsync(Models.Assessments.ComprehensiveAssessment assessment) => Refuse<bool>();
    }
}
