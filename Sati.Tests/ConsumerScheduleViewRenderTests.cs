using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Sati.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class ConsumerScheduleViewRenderTests
{
    [Fact]
    public void RideControlsAndSaveCommandFollowTheSelectedSchedule()
    {
        var model = new ConsumerScheduleViewModel(new StubScheduleService(), new SessionService());
        model.SetPerson(Person.Rehydrate(40, 1));

        WpfUiHarness.Run(() =>
        {
            var view = new ConsumerScheduleView { DataContext = model };
            WpfUiHarness.Realize(view);
            var save = WpfUiHarness.FindByAutomationName<Button>(
                view, "Save consumer schedule entry");
            var ride = WpfUiHarness.FindByAutomationName<ComboBox>(
                view, "ModivCare ride status");
            var pickup = WpfUiHarness.FindByAutomationName<TextBox>(
                view, "ModivCare outbound pickup time");
            var list = WpfUiHarness.FindByAutomationName<DataGrid>(
                view, "Consumer schedule entries");
            var calendar = WpfUiHarness.FindByAutomationName<ItemsControl>(
                view, "Selected consumer activity month");
            var previousMonth = WpfUiHarness.FindByAutomationName<Button>(
                view, "Previous month in consumer activity calendar");

            Assert.Same(model.SaveCommand, save.Command);
            Assert.Same(model.PreviousCalendarMonthCommand, previousMonth.Command);
            Assert.Equal(ModivcareRideStatus.NoRide, ride.SelectedValue);
            Assert.True(HiddenByCollapsedAncestor(pickup));
            Assert.Single(list.Items);
            Assert.Equal(model.CalendarDays.Count, calendar.Items.Count);

            model.RideStatus = ModivcareRideStatus.NeedsBooking;
            WpfUiHarness.Realize(view);
            Assert.False(HiddenByCollapsedAncestor(pickup));
            SaveCalendarPreviewIfRequested(view);
        });
    }

    private static void SaveCalendarPreviewIfRequested(ConsumerScheduleView view)
    {
        var path = Environment.GetEnvironmentVariable("SATI_CALENDAR_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(path)) return;
        var model = (ConsumerScheduleViewModel)view.DataContext;
        model.NextCalendarMonthCommand.Execute(null);
        model.SelectCalendarDayCommand.Execute(
            model.CalendarDays.First(x => x.Activities.Count > 0));
        WpfUiHarness.Realize(view, 1400, 900);
        var scroll = WpfUiHarness.FindByAutomationName<ScrollViewer>(
            view, "Consumer appointments and weekly schedule");
        scroll.ScrollToEnd();
        view.UpdateLayout();
        var image = new RenderTargetBitmap(1400, 900, 96, 96, PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static bool HiddenByCollapsedAncestor(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null;
             current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement { Visibility: Visibility.Collapsed }) return true;
        return false;
    }

    private sealed class StubScheduleService : IConsumerScheduleService
    {
        public Task<IReadOnlyList<ConsumerScheduleEntryDto>> GetAsync(int personId) =>
            Task.FromResult<IReadOnlyList<ConsumerScheduleEntryDto>>([
                new ConsumerScheduleEntryDto(1, personId, ConsumerScheduleKind.Work,
                    "Job", null, null, new DateTime(2026, 10, 1), null,
                    ScheduleWeekdays.Monday, 540, 1020, ModivcareRideStatus.NeedsBooking,
                    510, 1030, "ABC-123", 1)
            ]);

        public Task<ConsumerScheduleEntryDto> SaveAsync(int personId, int? entryId,
            SaveConsumerScheduleEntryRequest request) => throw new NotSupportedException();

        public Task DeleteAsync(int personId, int entryId, int expectedRevision) =>
            throw new NotSupportedException();
    }
}
