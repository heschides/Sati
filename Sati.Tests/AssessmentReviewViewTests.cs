using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Models.Assessments;
using Sati.ViewModels.Supervisor;
using Sati.Views.Supervisor;
using Sati.Views.ClientDocuments;
using Sati.ViewModels.ClientDocuments;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class AssessmentReviewViewTests
{
    [Fact]
    public void ReviewerAnswersAreReadOnlyAndCompletionRequiresASeparateNamedControl()
    {
        WpfUiHarness.Run(() =>
        {
            var vm = new AssessmentReviewsViewModel(new DelayedService(), Session());
            vm.Answers.Add(new("self-view", "The person’s priorities", "Synthetic author answer"));
            var view = new AssessmentReviewsView { DataContext = vm }; WpfUiHarness.Realize(view, 1050, 900);
            Assert.True(WpfUiHarness.FindByAutomationName<TextBox>(view, "Immutable assessment answer").IsReadOnly);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<CheckBox>(view, "Explicit assessment Form completion attestation"));
            Assert.NotNull(WpfUiHarness.FindByAutomationName<DatePicker>(view, "Actual assessment completion date"));
            Assert.True(WpfUiHarness.FindByAutomationName<ListBox>(view, "Assessment review validation summary").Focusable);
            Capture(view, "assessment-review.png");
        });
    }
    [Fact]
    public void AuthorWorkspaceReceivesInjectedModelAndItsSubmitAndPdfCommands()
    {
        WpfUiHarness.Run(() =>
        {
            var vm = new ComprehensiveAssessmentViewModel(new DelayedService(), Session(),
                new StabilizationTests.SmokeConsumerProviderService(), new StabilizationTests.SmokeProviderService()) { HasPerson = true };
            var view = new ComprehensiveAssessmentWorkspace { Workspace = vm, Background = Brushes.White };
            WpfUiHarness.Realize(view, 1280, 900);
            Assert.Same(vm.SubmitForReviewCommand, WpfUiHarness.Descendants(view).OfType<Button>()
                .Single(b => Equals(b.Content, "Submit for supervisor review")).Command);
            Assert.True(WpfUiHarness.FindByAutomationName<ListBox>(view, "Assessment submission validation summary").Focusable);
            vm.Submissions.Add(new(1, 71, 1, 1, 2, 33, DateTime.Today, DateTime.Today, 1, new string('A', 64), "{}", "Synthetic", DateTime.UtcNow));
            WpfUiHarness.Descendants(view).OfType<Expander>()
                .Single(e => Equals(e.Header, "Submissions and review history")).IsExpanded = true;
            WpfUiHarness.Realize(view, 1280, 900);
            Assert.Same(vm.GeneratePdfCommand, WpfUiHarness.Descendants(view).OfType<Button>()
                .Single(b => Equals(b.Content, "Save version PDF")).Command);
            Capture(view, "assessment-author.png");
        });
    }
    private static void Capture(System.Windows.FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("SATI_DOCUMENT_QA_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(view); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
    [Fact]
    public async Task SelectionAndAccountChangesSuppressOlderClinicalReads()
    {
        var service = new DelayedService(); var vm = new AssessmentReviewsViewModel(service, Session());
        var first = vm.LoadSelectedAsync(Item(1)); var second = vm.LoadSelectedAsync(Item(2));
        service.Second.SetResult(Details(2)); await second;
        service.First.SetResult(Details(1)); await first; Assert.Equal(2, vm.Details!.Assessment.Id);
        var pending = vm.LoadCommand.ExecuteAsync(null); vm.ClearForAccountSwitch();
        service.QueueResult.SetResult([Item(1)]); await pending;
        Assert.Empty(vm.Queue); Assert.Empty(vm.Answers); Assert.Null(vm.Details);
    }
    private static AssessmentQueueItemDto Item(int id) => new(id, id, "Synthetic consumer", "Synthetic author", 1, 2, id, DateTime.UtcNow, DateTime.Today);
    private static AssessmentReviewDetailsDto Details(int id) => new(new(id, id, 12, "ReadyForReview", 1, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null, null, "{}", 2), [], []);
    private static SessionService Session() { var s = new SessionService(); s.SetUser(User.Create(13, "synthetic", "Synthetic", "Reviewer", "", UserRole.Supervisor, null, 1)); return s; }
    private sealed class DelayedService : IComprehensiveAssessmentService
    {
        public TaskCompletionSource<AssessmentReviewDetailsDto> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AssessmentReviewDetailsDto> Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<AssessmentQueueItemDto>> QueueResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AssessmentReviewDetailsDto> GetReviewAsync(int id) => id == 1 ? First.Task : Second.Task;
        public Task<IReadOnlyList<AssessmentQueueItemDto>> GetReviewQueueAsync() => QueueResult.Task;
        public Task<ComprehensiveAssessment?> GetLatestForAgendaAsync(int personId) => throw new NotSupportedException();
        public Task<ComprehensiveAssessment> GetOrCreateDraftAsync(int personId, int authorUserId) => throw new NotSupportedException();
        public Task SaveDocumentAsync(ComprehensiveAssessment assessment, AssessmentDocument document) => throw new NotSupportedException();
        public Task SubmitForReviewAsync(ComprehensiveAssessment assessment) => throw new NotSupportedException();
    }
}
