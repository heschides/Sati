using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Contracts.V1;
using System.Collections.ObjectModel;
using System.Windows;

namespace Sati.Views;

public partial class TimeOffCheckRequestPromptWindow : Window
{
    private readonly TimeOffCheckRequestPromptViewModel viewModel = new();

    public TimeOffCheckRequestPromptWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public bool PrepareRequested { get; private set; }
    public TimeOffCheckRequestCollisionDto? SelectedCollision => viewModel.SelectedCollision;

    public void Configure(
        IReadOnlyList<TimeOffCheckRequestCollisionDto> collisions,
        bool newlyScheduled)
    {
        viewModel.Collisions.Clear();
        foreach (var collision in collisions) viewModel.Collisions.Add(collision);
        viewModel.SelectedCollision = viewModel.Collisions.FirstOrDefault();
        var date = collisions[0].TimeOffDate;
        viewModel.Heading = newlyScheduled
            ? "This day off overlaps weekly check requests"
            : "Weekly check requests are due during tomorrow's time off";
        viewModel.Explanation = newlyScheduled
            ? $"You marked {date:dddd, MMMM d} as time off. These consumers normally have check-request drafts scheduled that day."
            : $"You are scheduled off {date:dddd, MMMM d}. Prepare or review these requests before the day off.";
    }

    private void Prepare_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedCollision is null) return;
        PrepareRequested = true;
        DialogResult = true;
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        PrepareRequested = false;
        DialogResult = false;
    }
}

internal partial class TimeOffCheckRequestPromptViewModel : ObservableObject
{
    public ObservableCollection<TimeOffCheckRequestCollisionDto> Collisions { get; } = [];
    [ObservableProperty] private TimeOffCheckRequestCollisionDto? selectedCollision;
    [ObservableProperty] private string heading = string.Empty;
    [ObservableProperty] private string explanation = string.Empty;
}
