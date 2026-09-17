using System.Windows.Automation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels;
using Sati.Views;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The annual-form rows in the real client profile. 1.3.14 gave the checkbox's
/// automation name a null fallback, which WPF cannot validate: whenever a row had no
/// slot to show, layout threw a NullReferenceException from inside the binding engine
/// (production reference C00DD4583B60).
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class AnnualFormRowRenderTests
{
    [Fact]
    public void RowsWithoutASlotRenderAndRowsWithOneNameTheirRecord()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISessionService, SessionService>();
                services.AddSingleton<IComprehensiveAssessmentService, StabilizationTests.SmokeAssessmentService>();
                services.AddSingleton<IPersonCenteredPlanSourceService, StabilizationTests.SmokePlanSourceService>();
                services.AddSingleton<IConsumerProviderService, StabilizationTests.SmokeConsumerProviderService>();
                services.AddSingleton<IProviderService, StabilizationTests.SmokeProviderService>();
            })
            .Build();

        WpfUiHarness.RunWithHost(host, () =>
        {
            var profile = new AnnualFormHost();
            var view = new ClientsView { DataContext = profile };

            // No client selected, or a client without an effective date: no slot.
            // Under the old template every layout pass threw here, so the panel never
            // finished measuring and stopped responding (production log 32440).
            WpfUiHarness.Realize(view, 1400, 900);
            WpfUiHarness.Realize(view, 1400, 900);
            Assert.True(view.IsMeasureValid);
            Assert.True(view.IsArrangeValid);

            var target = new DateTime(2026, 10, 15);
            var slot = new AnnualFormSlotViewModel(
                FormType.PCP, AnnualFormSlotRole.Current, target,
                new Form(FormType.PCP, target, targetEffectiveDate: target), target.AddDays(-30));
            profile.AnnualFormRows[0].Current = slot;
            view.UpdateLayout();
            var checkBox = WpfUiHarness.Descendants(view).OfType<AttestationCheckBox>()
                .Single(box => ReferenceEquals(box.CommandParameter, slot));
            Assert.Equal(slot.AutomationName, AutomationProperties.GetName(checkBox));

            // Clearing the selection takes the slot away again.
            profile.AnnualFormRows[0].Current = null;
            view.UpdateLayout();
            Assert.Equal(string.Empty, AutomationProperties.GetName(checkBox));

            // Switching profiles swaps every row's content at once.
            view.DataContext = new AnnualFormHost();
            view.UpdateLayout();
        });
    }

    /// <summary>Only what the annual rows read; every other binding in the view stays unresolved.</summary>
    public sealed class AnnualFormHost
    {
        public IReadOnlyList<AnnualFormRowViewModel> AnnualFormRows { get; } =
            AnnualFormSlots.Types.Select(type => new AnnualFormRowViewModel(type)).ToArray();
        public bool ShowClientWorkspace => true;
        public int ClientWorkspaceTabIndex { get; set; }
        public int CompliancePresentationRevision => 0;
        public BillingComplianceRequirements BillingComplianceRequirements =>
            BillingComplianceGate.DefaultRequirements;
        public int PcpOpenDaysBefore => 90;
    }
}
