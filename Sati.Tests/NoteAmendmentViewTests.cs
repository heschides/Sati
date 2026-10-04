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
            var financial=new NoteAmendmentFinancialReviewsView {DataContext=new NoteAmendmentFinancialReviewsViewModel(new EmptyService())};
            WpfUiHarness.Realize(financial,700,600);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<TextBox>(financial,"Financial amendment review explanation"));
        });
    }
    [Fact]
    public async Task AccountChangeSuppressesAnOlderQueueResponse()
    {
        var service=new EmptyService(); var vm=new NoteAmendmentsViewModel(service); var pending=vm.LoadCommand.ExecuteAsync(null);
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
}
