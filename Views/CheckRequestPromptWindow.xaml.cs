using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Contracts.V1;
using System.Collections.ObjectModel;
using System.Windows;

namespace Sati.Views;

public partial class CheckRequestPromptWindow : Window
{
    private readonly CheckRequestPromptViewModel viewModel = new();

    public CheckRequestPromptWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public bool ReviewRequested { get; private set; }
    public GeneratedCheckRequestDraftDto? SelectedDraft => viewModel.SelectedDraft;

    public void Configure(
        IReadOnlyList<GeneratedCheckRequestDraftDto> drafts,
        CheckRequestPromptReason reason)
    {
        viewModel.Drafts.Clear();
        foreach (var draft in drafts) viewModel.Drafts.Add(draft);
        viewModel.SelectedDraft = viewModel.Drafts.FirstOrDefault();
        viewModel.Heading = drafts.Count == 1
            ? "A weekly check-request draft is ready"
            : $"{drafts.Count} weekly check-request drafts are ready";
        viewModel.Explanation = reason == CheckRequestPromptReason.Shutdown
            ? "These drafts have not yet been submitted. Review one now, or defer and close Sati."
            : "Sati created these drafts from your saved defaults. Review the details before preparing the PDF and submitting it to your supervisor.";
    }

    private void Review_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedDraft is null) return;
        ReviewRequested = true;
        DialogResult = true;
    }

    private void Defer_Click(object sender, RoutedEventArgs e)
    {
        ReviewRequested = false;
        DialogResult = false;
    }
}

public enum CheckRequestPromptReason
{
    SignIn,
    NewDay,
    Shutdown
}

internal partial class CheckRequestPromptViewModel : ObservableObject
{
    public ObservableCollection<GeneratedCheckRequestDraftDto> Drafts { get; } = [];
    [ObservableProperty] private GeneratedCheckRequestDraftDto? selectedDraft;
    [ObservableProperty] private string heading = "Weekly check-request drafts are ready";
    [ObservableProperty] private string explanation = string.Empty;
}
