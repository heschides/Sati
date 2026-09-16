using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The client list's last-contact line, rendered in the real view. The binding runs
/// through a multi-value converter and a data trigger, which only a rendered view proves.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ClientListContactRenderTests
{
    [Fact]
    public void EachClientShowsItsLastContactAndOverdueOnesAreRed()
    {
        var today = DateTime.Today;
        var recent = PersonWith("Recent", "Contact", today.AddDays(-200), [today.AddDays(-5)]);
        var overdue = PersonWith("Overdue", "Contact", today.AddDays(-200), [today.AddDays(-45)]);
        var newClient = PersonWith("New", "Client", today.AddDays(-10), []);
        var unloaded = PersonWith("Unloaded", "History", today.AddDays(-200), []);
        unloaded.ContactFactsForCompliance = null;

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
            var view = new Sati.Views.ClientsView();
            view.DataContext = new ClientListHost([recent, overdue, newClient, unloaded]);
            WpfUiHarness.Realize(view, 1400, 900);

            var list = WpfUiHarness.FindByAutomationName<ListBox>(view, "Clients");
            var lines = WpfUiHarness.Descendants(list).OfType<TextBlock>()
                .Where(text => text.Text.StartsWith("Last contact", StringComparison.Ordinal) ||
                               text.Text.StartsWith("No contact", StringComparison.Ordinal))
                .ToList();
            var danger = (Brush)view.FindResource("DangerStrongBrush");
            var secondary = (Brush)view.FindResource("TextSecondaryBrush");

            var recentLine = Assert.Single(lines, text =>
                text.Text == $"Last contact {today.AddDays(-5):MM/dd/yy}");
            var overdueLine = Assert.Single(lines, text =>
                text.Text == $"Last contact {today.AddDays(-45):MM/dd/yy} · overdue");
            var newLine = Assert.Single(lines, text => text.Text == "No contact recorded");
            Assert.Equal(3, lines.Count);

            Assert.Same(secondary, recentLine.Foreground);
            Assert.Same(secondary, newLine.Foreground);
            Assert.Same(danger, overdueLine.Foreground);
            Assert.Equal(FontWeights.SemiBold, overdueLine.FontWeight);
            // The words carry the status too, so they must never be cut off.
            Assert.Equal(TextWrapping.Wrap, overdueLine.TextWrapping);
            // A consumer whose history was not loaded shows nothing, not a blank line.
            var emptyLine = Assert.Single(
                WpfUiHarness.Descendants(list).OfType<TextBlock>(),
                text => text.Text.Length == 0 &&
                        System.Windows.Data.BindingOperations.GetMultiBindingExpression(
                            text, TextBlock.TextProperty) is not null);
            Assert.Equal(Visibility.Collapsed, emptyLine.Visibility);

            SavePreview(list, "client-list-last-contact.png");
        });
    }

    private static Person PersonWith(
        string first, string last, DateTime effective, IReadOnlyList<DateTime> contacts)
    {
        var person = Person.CreatePerson(
            31, first, last, string.Empty, new DateTime(1990, 1, 1),
            effective, WaiverType.Section21, new Settings());
        person.ContactFactsForCompliance = contacts.Select(date => new ContactFact(date)).ToList();
        return person;
    }

    private static void SavePreview(FrameworkElement element, string fileName)
    {
        if (Environment.GetEnvironmentVariable("SATI_DASHBOARD_QA_OUTPUT") is not { Length: > 0 } directory)
            return;

        const int scale = 2;
        var image = new RenderTargetBitmap(
            (int)element.ActualWidth * scale,
            (int)element.ActualHeight * scale,
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        image.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, fileName));
        encoder.Save(output);
    }

    /// <summary>Only what the client list reads; every other binding in the view stays unresolved.</summary>
    public sealed class ClientListHost(IReadOnlyList<Person> people)
    {
        public IReadOnlyList<Person> PeopleView { get; } = people;
        public int CompliancePresentationRevision => 0;
        public BillingComplianceRequirements BillingComplianceRequirements =>
            BillingComplianceGate.DefaultRequirements;
        public int PcpOpenDaysBefore => 90;
    }
}
