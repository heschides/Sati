using Sati.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Sati.Views;

public partial class ScratchpadHistoryView : UserControl
{
    private const double StackedLayoutThreshold = 640;
    private bool _isRestoringCalendarSelection;
    private bool _usesStackedLayout;

    public ScratchpadHistoryView()
    {
        InitializeComponent();
    }

    internal void SynchronizeCalendarSelection()
    {
        if (DataContext is not ScratchpadHistoryViewModel viewModel)
            return;

        var start = viewModel.SelectionStart ?? viewModel.InitialSelectedDate;
        var end = viewModel.SelectionEnd ?? start;

        _isRestoringCalendarSelection = true;
        try
        {
            HistoryCalendar.DisplayDate = viewModel.InitialDisplayDate;
            HistoryCalendar.SelectedDates.Clear();
            HistoryCalendar.SelectedDates.AddRange(start, end);
        }
        finally
        {
            _isRestoringCalendarSelection = false;
        }
    }

    private void HistoryCalendar_SelectedDatesChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_isRestoringCalendarSelection ||
            DataContext is not ScratchpadHistoryViewModel viewModel)
        {
            return;
        }

        // A WPF Calendar in SingleRange mode can briefly clear SelectedDates
        // when focus moves to another control. Restore that transient clear after
        // input finishes, but still allow a real date change to replace the range.
        if (HistoryCalendar.SelectedDates.Count == 0 &&
            viewModel.SelectionStart is DateTime selectionStart &&
            viewModel.SelectionEnd is DateTime selectionEnd)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (HistoryCalendar.SelectedDates.Count > 0)
                    return;

                _isRestoringCalendarSelection = true;
                try
                {
                    HistoryCalendar.SelectedDates.AddRange(selectionStart, selectionEnd);
                }
                finally
                {
                    _isRestoringCalendarSelection = false;
                }
            }, DispatcherPriority.Input);
            return;
        }

        viewModel.SetSelectedDates(HistoryCalendar.SelectedDates);
    }

    private void HistoryView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var shouldStack = e.NewSize.Width < StackedLayoutThreshold;
        if (shouldStack == _usesStackedLayout)
            return;

        _usesStackedLayout = shouldStack;
        if (shouldStack)
        {
            HistoryLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            HistoryLayout.ColumnDefinitions[1].Width = new GridLength(0);
            HistoryLayout.ColumnDefinitions[2].Width = new GridLength(0);
            HistoryLayout.RowDefinitions[0].Height = GridLength.Auto;
            HistoryLayout.RowDefinitions[1].Height = new GridLength(10);
            HistoryLayout.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(PickerPanel, 0);
            Grid.SetRow(PickerPanel, 0);
            Grid.SetColumn(ResultsPanel, 0);
            Grid.SetRow(ResultsPanel, 2);
            PickerPanel.MaxHeight = 300;
        }
        else
        {
            HistoryLayout.ColumnDefinitions[0].Width = new GridLength(250);
            HistoryLayout.ColumnDefinitions[1].Width = new GridLength(14);
            HistoryLayout.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            HistoryLayout.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            HistoryLayout.RowDefinitions[1].Height = new GridLength(0);
            HistoryLayout.RowDefinitions[2].Height = new GridLength(0);
            Grid.SetColumn(PickerPanel, 0);
            Grid.SetRow(PickerPanel, 0);
            Grid.SetColumn(ResultsPanel, 2);
            Grid.SetRow(ResultsPanel, 0);
            PickerPanel.MaxHeight = double.PositiveInfinity;
        }
    }
}
