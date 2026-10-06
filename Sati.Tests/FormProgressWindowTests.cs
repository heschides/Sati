using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Sati.ViewModels.Children;
using Sati.Views;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class FormProgressWindowTests
{
    [Theory]
    [InlineData("PCP")]
    [InlineData("ComprehensiveAssessment")]
    public void UnopenedDocumentDefaultsToUnchangedAndCompletionExposesBothDates(string type)
    {
        WpfUiHarness.Run(() =>
        {
            var today = DateTime.Today;
            var request = new FormProgressConfirmationEventArgs(type, "Pending", today,
                today.AddDays(-30), today, today.AddDays(10), today.AddDays(100), null, null);
            var window = new FormProgressWindow(request);
            window.Measure(new Size(520, 800));
            window.Arrange(new Rect(0, 0, 520, 800));
            window.UpdateLayout();
            var action = (ComboBox)window.FindName("ActionChoice");
            Assert.Equal("Document action", AutomationProperties.GetName(action));
            Assert.Equal("Leave unchanged", action.SelectedItem);
            Assert.Contains("Not opened", ((TextBlock)window.FindName("StateLabel")).Text);
            Assert.Null(request.Progress);
            action.SelectedIndex = 2;
            Assert.Equal(Visibility.Visible, ((StackPanel)window.FindName("OpeningPanel")).Visibility);
            Assert.Equal(Visibility.Visible, ((StackPanel)window.FindName("CompletionPanel")).Visibility);
            Assert.Null(((DatePicker)window.FindName("OpeningDate")).SelectedDate);
            Assert.Equal(today, ((DatePicker)window.FindName("CompletionDate")).SelectedDate);
            window.Close();
        });
    }

    [Fact]
    public void CompletedDocumentOffersOnlyLeaveUnchanged()
    {
        WpfUiHarness.Run(() =>
        {
            var today = DateTime.Today;
            var window = new FormProgressWindow(new("PCP", "Pending", today,
                today.AddDays(-90), today, today, today, today.AddDays(-10), today.AddDays(-1)));
            Assert.Equal("Leave unchanged", Assert.Single(((ComboBox)window.FindName("ActionChoice")).Items.Cast<string>()));
            Assert.Contains("Completed", ((TextBlock)window.FindName("StateLabel")).Text);
            window.Close();
        });
    }
}
