using Sati.Contracts.V1;
using Sati.Data;
using Sati.ViewModels;
using Sati.ViewModels.Billing;
using Sati.Views;
using Sati.Views.Billing;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class NoteAmendmentViewTests
{
    [Fact]
    public void OriginalAndProposedNarrativeAreReadableAndImmutableWithoutAuthority()
    {
        WpfUiHarness.Run(() =>
        {
            var vm=new NoteAmendmentsViewModel(new EmptyService()); var view=new NoteAmendmentsView {DataContext=vm};
            WpfUiHarness.Realize(view,700,800);
            Assert.True(WpfUiHarness.FindByAutomationName<TextBox>(view,"Immutable approved original").IsReadOnly);
            Assert.True(WpfUiHarness.FindByAutomationName<TextBox>(view,"Proposed amended narrative").IsReadOnly);
            var queue = WpfUiHarness.FindByAutomationName<ListBox>(view,"Approved notes and amendment status");
            Assert.Equal(System.Windows.Visibility.Collapsed, queue.Visibility);
            vm.SetReviewMode();
            WpfUiHarness.Realize(view,700,800);
            Assert.Equal(System.Windows.Visibility.Visible, queue.Visibility);
            var financial=new NoteAmendmentFinancialReviewsView {DataContext=new NoteAmendmentFinancialReviewsViewModel(new EmptyService())};
            WpfUiHarness.Realize(financial,700,600);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<TextBox>(financial,"Financial amendment review explanation"));
        });
    }
    [Fact]
    public async Task AccountChangeSuppressesAnOlderQueueResponse()
    {
        var service=new EmptyService(); var vm=new NoteAmendmentsViewModel(service); vm.SetReviewMode(); var pending=vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedVersion=new(1,1,"Draft",new("Prior account narrative",DateTime.Today,15,null,false),"Synthetic reason",1,DateTime.UtcNow);
        vm.ClearForAccountSwitch(); service.QueueResult.SetResult(new([new(1,1,"Previous account client",DateTime.Today,"Approved",false)],null)); await pending;
        Assert.Empty(vm.Queue); Assert.Null(vm.Selected); Assert.Null(vm.SelectedVersion); Assert.Empty(vm.Original);
    }
    private sealed class EmptyService : INoteAmendmentService
    {
        public TaskCompletionSource<NoteAmendmentQueuePage> QueueResult {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<NoteAmendmentQueuePage> GetQueueAsync(bool review,int afterNoteId=0)=>QueueResult.Task;
        public Task<NoteAmendmentWorkspaceDto> GetAsync(int noteId)=>throw new NotSupportedException();
        public Task<NoteAmendmentResultDto> ActAsync(int noteId,NoteAmendmentRequest r)=>throw new NotSupportedException();
        public Task<IReadOnlyList<NoteAmendmentFinancialItem>> GetFinancialQueueAsync(int afterNoteId=0)=>Task.FromResult<IReadOnlyList<NoteAmendmentFinancialItem>>([]);
        public Task<NoteAmendmentFinancialReviewDto> ReviewFinancialAsync(int noteId,NoteAmendmentFinancialReviewRequest r)=>throw new NotSupportedException();
    }

    [Fact]
    public async Task NotesLogDoesNotLoadAnUnrelatedCorrectionQueue()
    {
        var vm = new NoteAmendmentsViewModel(new EmptyService());
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Empty(vm.Queue);
        Assert.Contains("Select an approved note", vm.StatusMessage);
    }

    [Fact]
    public async Task SelectedNoteHistoryIgnoresAnOlderSelectionResponse()
    {
        var service = new SelectedNoteService();
        var vm = new NoteAmendmentsViewModel(service);
        var first = vm.ShowNoteAsync(1);
        var second = vm.ShowNoteAsync(2);
        service.Results[2].SetResult(Workspace(2));
        await second;
        service.Results[1].SetResult(Workspace(1));
        await first;
        Assert.Contains("Synthetic note 2", vm.Original);
        Assert.DoesNotContain("Synthetic note 1", vm.Original);
        await vm.ShowNoteAsync(null);
        Assert.Empty(vm.Original);
        Assert.Empty(vm.Versions);
    }

    private static NoteAmendmentWorkspaceDto Workspace(int id)
    {
        var original = new NoteDto(id, $"Synthetic note {id}", DateTime.Today, "Approved", 15,
            null, id, null, "Visit", 1, null, null, 3, DateTime.Today, null, null, null,
            false, null, null, null, 1, null);
        return new(original, NoteAmendmentContent.From(original), null, false, true, false, false, []);
    }

    private sealed class SelectedNoteService : INoteAmendmentService
    {
        public Dictionary<int, TaskCompletionSource<NoteAmendmentWorkspaceDto>> Results { get; } = [];
        public Task<NoteAmendmentWorkspaceDto> GetAsync(int noteId)
        {
            var result = new TaskCompletionSource<NoteAmendmentWorkspaceDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            Results.Add(noteId, result);
            return result.Task;
        }
        public Task<NoteAmendmentQueuePage> GetQueueAsync(bool review, int afterNoteId = 0) => throw new NotSupportedException();
        public Task<NoteAmendmentResultDto> ActAsync(int noteId, NoteAmendmentRequest request) => throw new NotSupportedException();
        public Task<IReadOnlyList<NoteAmendmentFinancialItem>> GetFinancialQueueAsync(int afterNoteId = 0) => throw new NotSupportedException();
        public Task<NoteAmendmentFinancialReviewDto> ReviewFinancialAsync(int noteId, NoteAmendmentFinancialReviewRequest request) => throw new NotSupportedException();
    }
}
