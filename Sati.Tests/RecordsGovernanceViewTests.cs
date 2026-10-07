using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Admin;
using Sati.Views;
using System.Windows.Controls;
using Xunit;
namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class RecordsGovernanceViewTests
{
    [Fact]
    public void ViewOffersAccessibleScopesAndIndependentDecisionCommands()
    {
        WpfUiHarness.Run(() =>
        {
            var vm = Create(new Service()); var view = new RecordsGovernanceView { DataContext = vm };
            WpfUiHarness.Realize(view, 800, 1100);
            Assert.Equal(3, WpfUiHarness.FindByAutomationName<ComboBox>(view, "Preservation scope").Items.Count);
            Assert.Equal(6, WpfUiHarness.FindByAutomationName<ComboBox>(view, "Policy record class").Items.Count);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<TextBox>(view, "Preservation action reason"));
            Assert.False(vm.ApproveReleaseCommand.CanExecute(null));
            vm.SelectedHold = Hold(1, 1); Assert.False(vm.ApproveReleaseCommand.CanExecute(null));
            vm.SelectedHold = Hold(2, 2); Assert.True(vm.ApproveReleaseCommand.CanExecute(null));
            vm.SelectedHold = Hold(2, 1); Assert.False(vm.ApproveReleaseCommand.CanExecute(null));
        });
    }
    [Fact]
    public async Task AccountSwitchSuppressesAnOlderLoadAndClearsSensitiveReasons()
    {
        var service = new Service(); var vm = Create(service); var load = vm.LoadCommand.ExecuteAsync(null);
        vm.Reason = "Prior restricted reason"; vm.PolicyReason = "Prior restricted policy reason";
        vm.ClearForAccountSwitch(); service.Holds.SetResult([Hold(2, 2)]); await load;
        Assert.Empty(vm.Holds); Assert.Empty(vm.Policies); Assert.Empty(vm.Reason); Assert.Empty(vm.PolicyReason); Assert.False(vm.IsBusy);
    }
    [Fact]
    public async Task ReloadWithSelectedPolicyFinishesWithoutInvalidatingItsOwnRequest()
    {
        var service = new Service(); var vm = Create(service); vm.SelectedPolicy = Policy;
        var load = vm.LoadCommand.ExecuteAsync(null); service.Holds.SetResult([]); await load;
        Assert.False(vm.IsBusy); Assert.Single(vm.Policies); Assert.Null(vm.SelectedPolicy);
    }
    [Fact]
    public async Task ChangedSelectionDoesNotReceiveAnOlderPreview()
    {
        var service = new Service(); var vm = Create(service); vm.SelectedPolicy = Policy;
        var pending = vm.PreviewCommand.ExecuteAsync(null); vm.SelectedPolicy = Policy with { Id = 2, Version = 2 };
        service.Preview.SetResult(new(Guid.NewGuid(), 1, 1, 1, RetentionRecordClass.Clinical, DateTime.UtcNow, null,
            null, null, null, null, null, null, null, ["runtime_policy_only"], false, "PolicyOnly"));
        await pending; Assert.Null(vm.Preview); Assert.False(vm.IsBusy); Assert.Contains("No preview", vm.PreviewSummary);
    }
    private static readonly RetentionPolicyDto Policy = new(1, 1, RetentionRecordClass.Clinical, null, 1, DateTime.UtcNow, "Synthetic proposal", "PolicyOnly");
    [Fact]
    public async Task InvalidNumericTextCannotSendAnOlderBoundValue()
    {
        var vm = Create(new Service()); vm.RetentionDaysText = "invalid";
        await vm.SavePolicyCommand.ExecuteAsync(null);
        Assert.Contains("valid retention period", vm.StatusMessage);
        vm.Scope = PreservationScope.Person; vm.PersonIdText = "invalid";
        await vm.PlaceCommand.ExecuteAsync(null);
        Assert.Contains("positive person", vm.StatusMessage);
    }
    private static GovernanceHoldDto Hold(int placer, int requester) => new(Guid.NewGuid(), 2, PreservationScope.Agency, null, null, null,
        false, placer, requester, []);
    private static RecordsGovernanceViewModel Create(Service service)
    { var session = new SessionService(); session.SetUser(User.Create(1, "synthetic-admin", "Admin", "hash", "salt", UserRole.Admin, null, 1)); return new(service, session); }
    private sealed class Service : IRecordsGovernanceService
    {
        public TaskCompletionSource<IReadOnlyList<GovernanceHoldDto>> Holds { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<RetentionPreviewDto> Preview { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<GovernanceHoldDto>> GetHoldsAsync(CancellationToken ct = default) => Holds.Task;
        public Task<GovernanceHoldDto> ChangeHoldAsync(GovernanceHoldRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RetentionPolicyDto>> GetPoliciesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RetentionPolicyDto>>([Policy]);
        public Task<RetentionPolicyDto> SavePolicyAsync(RetentionPolicyRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RetentionPreviewDto> PreviewAsync(long id, CancellationToken ct = default) => Preview.Task;
    }
}
