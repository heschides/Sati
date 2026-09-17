using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Sati.Models;
using Sati.ViewModels.Children;
using Sati.Views;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The Overview's read-only month thumbnail. Its squares carry no legible text, so the fill
/// has to be right and the panel has to name the same counts for a screen reader.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ProductivityThumbnailTests
{
    private static readonly DateTime Today = new(2026, 9, 17);

    [Fact]
    public void TheThumbnailPaintsCountedDaysAndNothingElse()
    {
        var month = CalendarViewModel.BuildMonth(Today.Year, Today.Month,
        [
            Note.Create("Logged.", Today.AddDays(-2), NoteStatus.Logged, 60, 1, noteType: NoteType.Contact),
            Note.Create("Pending.", Today.AddDays(-1), NoteStatus.Pending, 30, 1, noteType: NoteType.Contact),
            Note.Create("Next week.", Today.AddDays(4), NoteStatus.Pending, 60, 1, noteType: NoteType.Visit)
        ], [], Today);

        WpfUiHarness.Run(() =>
        {
            var view = new ProductivityCalendarThumbnail { DataContext = month };
            WpfUiHarness.Realize(view, 280, 140);

            Assert.Equal(Resource(view, "ProductivityDayFillBrush"), Square(view, Today.AddDays(-2)).Background);
            var pendingOnly = Square(view, Today.AddDays(-1));
            Assert.Equal(Resource(view, "ProductivityPendingDayFillBrush"), pendingOnly.Background);
            Assert.Equal(Resource(view, "ProductivityPendingDayBorderBrush"), pendingOnly.BorderBrush);
            Assert.Equal(Resource(view, "SurfaceRaisedBrush"), Square(view, Today.AddDays(4)).Background);
            Assert.Equal(Resource(view, "SurfaceRaisedBrush"), Square(view, Today.AddDays(-3)).Background);

            // Read-only: the thumbnail offers nothing to click or tab to.
            Assert.Empty(WpfUiHarness.Descendants(view).OfType<ButtonBase>());
        });
    }

    [Fact]
    public void TheOverviewPanelHostsTheThumbnailAndNamesItsCounts()
    {
        var overview = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Views", "CaseManagerDashboardContentView.xaml"));

        Assert.Contains("<views:ProductivityCalendarThumbnail", overview);
        Assert.Contains("DataContext=\"{Binding ProductivityMonth}\"", overview);
        Assert.Contains("ProductivityMonthSummary", overview);
    }

    private static Border Square(ProductivityCalendarThumbnail view, DateTime date) =>
        WpfUiHarness.Descendants(view).OfType<Border>()
            .Single(border => border.DataContext is CalendarDay day &&
                              day.Date.Date == date.Date &&
                              border.Style is not null);

    private static Brush Resource(ProductivityCalendarThumbnail view, string key) =>
        (Brush)view.FindResource(key);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SatiLogica.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
