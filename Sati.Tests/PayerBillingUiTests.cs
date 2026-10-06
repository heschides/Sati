using System.Windows.Controls;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Models;
using Sati.Services.Billing;
using Sati.TestFixtures;
using Sati.ViewModels.Billing;
using Sati.ViewModels.Admin;
using Sati.Views;
using Sati.Views.Billing;
using Xunit;

namespace Sati.Tests;
[Collection(WpfViewCollection.Name)]
public class PayerBillingUiTests
{
    [Fact] public void BillingReaderCanFocusConfigurationFieldsButCannotPublish()
    {
        WpfUiHarness.Run(() =>
        {
            var session = Session(UserPermissions.Billing); var model = new PayerBillingViewModel(new PreviewStub(), session);
            model.Versions.Add(PayerBillingSynthetic.Version()); model.SelectedVersion = model.Versions.Single();
            model.Notes.Add(new(99, 101, new(2026, 8, 1))); model.SelectedNote = model.Notes.Single();
            model.AuthorizationReference = "SYNTHPA123"; model.AuthorizationDecisionDate = "2026-04-28";
            model.AuthorizationEffectiveOn = "2026-04-28"; model.AuthorizationExpiresOn = "2027-04-27";
            model.AuthorizationEvidenceReference = "synthetic:authorization-evidence";
            var view = new PayerBillingView { DataContext = model }; WpfUiHarness.Realize(view, 1000, 900);
            var field = WpfUiHarness.FindByAutomationName<TextBox>(view, "Facility ID");
            Assert.NotNull(field); Assert.True(field.IsReadOnly); Assert.True(field.IsEnabled); Assert.True(field.Focusable);
            var publish = WpfUiHarness.Descendants(view).OfType<Button>().Single(b => Equals(b.Content, "Publish new immutable version")); Assert.False(publish.IsEnabled);
            Capture(view, "payer-configuration.png");
            var tabs = WpfUiHarness.Descendants(view).OfType<TabControl>().Single(); tabs.SelectedIndex = 1; WpfUiHarness.Realize(view, 1000, 900);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<TextBox>(view, "Authorization decision date"));
            Assert.NotNull(WpfUiHarness.FindByAutomationName<ComboBox>(view, "Approved unclaimed note"));
            Assert.NotNull(WpfUiHarness.FindByAutomationName<ListBox>(view, "Payer field validation errors"));
            var create = WpfUiHarness.Descendants(view).OfType<Button>().Single(b => Equals(b.Content, "Create claim from this preview")); Assert.False(create.IsEnabled);
            Capture(view, "payer-readiness.png");
        });
    }
    [Fact] public async Task EditedInputsDefeatAnOlderReadinessResponseAndReleaseBusyState()
    {
        var service = new PreviewStub(); var model = new PayerBillingViewModel(service, Session(UserPermissions.AllAgencyPermissions));
        await model.LoadAsync(); model.SelectedVersion = model.Versions.Single(); model.SelectedNote = model.Notes.Single();
        model.AuthorizationReference = "SYNTHPA123"; model.AuthorizationDecisionDate = "2026-04-28";
        model.AuthorizationEffectiveOn = "2026-04-28"; model.AuthorizationExpiresOn = "2027-04-27";
        model.AuthorizationEvidenceReference = "synthetic:authorization-evidence"; model.AuthorizationCoverageReviewed = true;
        var pending = model.PreviewCommand.ExecuteAsync(null); Assert.True(model.IsBusy);
        model.AuthorizationReference = "CHANGED_AFTER_REQUEST";
        service.Preview.TrySetResult(new(99, model.Versions.Single().VersionId, [], "G9012", ["HI"], 1, 25, "AB12-123"));
        await pending; Assert.False(model.PreviewReady); Assert.False(model.IsBusy);
        await model.CreateClaimCommand.ExecuteAsync(null); Assert.False(service.Created);
    }
    [Fact] public void AdministratorWithoutBillingCanReachEditorAndAccountClearDropsInputs()
    {
        WpfUiHarness.Run(() =>
        {
            var session = Session(UserPermissions.Administration);
            var payer = new PayerBillingViewModel(new PreviewStub(), session);
            payer.LoadAsync().GetAwaiter().GetResult(); Assert.Empty(payer.Notes);
            payer.SelectedVersion = payer.Versions.Single();
            var dashboard = new AdminDashboardViewModel(null!, session, payerBilling: payer);
            var view = new AdminDashboardView { DataContext = dashboard }; WpfUiHarness.Realize(view, 1000, 900);
            var tab = WpfUiHarness.FindByAutomationName<TabItem>(view, "Administrator payer configuration");
            Assert.NotNull(tab); tab.IsSelected = true; WpfUiHarness.Realize(view, 1000, 900);
            var editor = Assert.Single(WpfUiHarness.Descendants(view).OfType<PayerBillingView>());
            Assert.False(WpfUiHarness.FindByAutomationName<TextBox>(editor, "Facility ID")!.IsReadOnly);
            Assert.True(payer.CanEdit); Assert.False(payer.CanPrepare);
            dashboard.ClearForAccountSwitch();
            Assert.Empty(payer.Versions); Assert.All(payer.Fields, field => Assert.Equal("", field.Value));
            Assert.False(payer.PreviewReady);
        });
    }
    private static void Capture(PayerBillingView view, string fileName)
    {
        if (Environment.GetEnvironmentVariable("SATI_PAYER_QA_OUTPUT") is not { Length: > 0 } path) return;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1000, 900, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        System.IO.Directory.CreateDirectory(path); using var output = System.IO.File.Create(System.IO.Path.Combine(path, fileName)); encoder.Save(output);
    }
    private static SessionService Session(UserPermissions permissions)
    {
        var user = User.Create(11, "synthetic-payer-ui", "Synthetic", "hash", "salt", UserRole.Admin, null, 1); user.Permissions = permissions;
        var session = new SessionService(); session.SetUser(user); return session;
    }
    private sealed class PreviewStub() : BillingService(null!), IBillingService
    {
        public TaskCompletionSource<PayerClaimPreviewDto> Preview { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Created;
        public new Task<IReadOnlyList<PayerBillingVersionDto>> GetPayerConfigurationsAsync(AgencyActor actor) => Task.FromResult<IReadOnlyList<PayerBillingVersionDto>>([PayerBillingSynthetic.Version()]);
        public new Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync(AgencyActor actor)
        {
            var note = Note.Rehydrate(99); note.PersonId = 101; note.EventDate = new(2026, 8, 1); return Task.FromResult<IEnumerable<Note>>([note]);
        }
        public new Task<PayerClaimPreviewDto> PreviewPayerClaimAsync(AgencyActor actor, int noteId, PayerClaimPreparation preparation) => Preview.Task;
        public new Task<Sati.Models.Billing.ClaimLine> CreatePreparedClaimLineAsync(AgencyActor actor, int noteId, PayerClaimPreparation preparation)
        { Created = true; return Task.FromResult(new Sati.Models.Billing.ClaimLine()); }
    }
}
