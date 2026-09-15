using System.Xml.Linq;
using Xunit;

namespace Sati.Tests;

public sealed class ClientCompliancePresentationUiTests
{
    private static readonly string Root = RenderedViews.RepositoryRoot();

    [Fact]
    public void EveryRosterComplianceBindingObservesTheSharedPresentationRevision()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var document = XDocument.Load(Path.Combine(Root, "Views", "ClientsView.xaml"));

        var complianceBindings = document
            .Descendants(presentation + "MultiBinding")
            .Where(binding => binding.Attribute("Converter")?.Value.Contains(
                "PersonBillingComplianceConverter", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(complianceBindings);
        Assert.All(complianceBindings, binding => Assert.Contains(
            binding.Elements(presentation + "Binding"),
            input => input.Attribute("Path")?.Value ==
                "DataContext.CompliancePresentationRevision"));
    }

    [Fact]
    public void BillingComplianceAlertIsOutsideTheRecordTabs()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var document = XDocument.Load(Path.Combine(Root, "Views", "ClientsView.xaml"));

        var alert = document.Descendants(presentation + "Border").Single(element =>
            element.Attribute("AutomationProperties.Name")?.Value ==
                "Selected client billing compliance alert");
        var tabs = document.Descendants(presentation + "TabControl").Single(element =>
            element.Attribute("AutomationProperties.Name")?.Value ==
                "Consumer record sections");

        Assert.DoesNotContain(alert.Ancestors(), ancestor => ancestor == tabs);
        Assert.Same(tabs.Parent, alert.Parent);
        Assert.True(alert.ElementsBeforeSelf().Count() < tabs.ElementsBeforeSelf().Count());
    }
}
