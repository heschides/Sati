using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;

namespace Sati.ViewModels.ClientDocuments;

public partial class SafetyDeviceViewModel : ObservableObject
{
    private readonly ISafetyDeviceService service;
    private Person? person;
    private int personVersion;

    public SafetyDeviceViewModel(ISafetyDeviceService service,
        IFormWizardProgressService progressService)
    {
        this.service = service;
        Progress = new FormWizardProgressViewModel(progressService,
            () => FormWizardProgressJson.Capture(BuildRequest()), RestoreProgress);
        Progress.Watch(this);
        Progress.WatchChildren(Devices);
    }

    public FormWizardProgressViewModel Progress { get; }

    [ObservableProperty] private string personName = "Select a consumer";
    [ObservableProperty] private string memberOrGuardianContact = "";
    [ObservableProperty] private string programNameAndAddress = "";
    [ObservableProperty] private string programContacts = "";
    [ObservableProperty] private string programContactNumbers = "";
    [ObservableProperty] private string programContactEmails = "";
    [ObservableProperty] private string medicalProviderName = "";
    [ObservableProperty] private string lessRestrictiveStrategies = "";
    [ObservableProperty] private string evaluationPlan = "";
    [ObservableProperty] private string otherResidentsAccommodations = "";
    [ObservableProperty] private DateTime? planningTeamMeetingDate;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private string reviewItemsMessage = "";

    public ObservableCollection<SafetyDeviceRowViewModel> Devices { get; } =
        new(Enumerable.Range(1, SafetyDeviceRules.MaximumDevices)
            .Select(index => new SafetyDeviceRowViewModel(index)));
    public string SourceRevision => SafetyDeviceRules.SourceRevision;
    public bool CanGenerate => person is not null && !IsBusy;
    public event EventHandler<SafetyDevicePdfReadyEventArgs>? PdfReady;
    public event EventHandler<SafetyDeviceProblemEventArgs>? Problem;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
        ResetAnswersCommand.NotifyCanExecuteChanged();
    }

    public void SetPerson(Person? value)
    {
        personVersion++;
        person = value;
        IsBusy = false;
        PersonName = value?.FullName ?? "Select a consumer";
        ResetAnswers();
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
        Progress.SetPerson(value, "safety-device");
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (person is null) return;
        var request = BuildRequest();
        var errors = SafetyDeviceRules.Validate(request, DateOnly.FromDateTime(DateTime.Today));
        if (errors.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine,
                errors.Values.SelectMany(values => values).Select(message => $"- {message}"));
            return;
        }
        var id = person.Id;
        var version = personVersion;
        IsBusy = true;
        StatusMessage = "Preparing the OADS Safety Device Request Form...";
        ReviewItemsMessage = "";
        try
        {
            var result = await service.GenerateAsync(id, request);
            if (version != personVersion || person?.Id != id) return;
            StatusMessage = "The editable draft is ready to save. Review the official form and obtain the required signatures before submission.";
            ReviewItemsMessage = $"Review before submission: {string.Join(", ", result.ReviewItems)}.";
            PdfReady?.Invoke(this, new SafetyDevicePdfReadyEventArgs(result.Pdf, result.FileName));
        }
        catch (Exception exception)
        {
            if (version != personVersion || person?.Id != id) return;
            StatusMessage = "The safety device request could not be generated.";
            Problem?.Invoke(this, new SafetyDeviceProblemEventArgs(
                "Safety Device Request Not Generated",
                $"The request could not be generated.\n\n{exception.Message}"));
        }
        finally
        {
            if (version == personVersion) IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void ResetAnswers()
    {
        var value = person;
        MemberOrGuardianContact = string.Join("; ", new[] { value?.PhoneNumber, value?.Email }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        ProgramNameAndAddress = ProgramContacts = ProgramContactNumbers = ProgramContactEmails = "";
        MedicalProviderName = LessRestrictiveStrategies = EvaluationPlan = OtherResidentsAccommodations = "";
        PlanningTeamMeetingDate = null;
        foreach (var row in Devices) row.Clear();
        StatusMessage = ReviewItemsMessage = "";
    }

    private bool CanReset() => !IsBusy;

    internal SafetyDeviceRequest BuildRequest() => new(
        MemberOrGuardianContact, ProgramNameAndAddress, ProgramContacts,
        ProgramContactNumbers, ProgramContactEmails, MedicalProviderName,
        Devices.Select(row => row.ToEntry()).ToArray(),
        LessRestrictiveStrategies, EvaluationPlan, OtherResidentsAccommodations,
        PlanningTeamMeetingDate is DateTime date ? DateOnly.FromDateTime(date) : null);

    private void RestoreProgress(string json, int _)
    {
        var request = System.Text.Json.JsonSerializer.Deserialize<SafetyDeviceRequest>(json)
            ?? throw new InvalidOperationException("The saved Safety Device Request is empty.");
        FormWizardProgressJson.ApplyScalars(this, request);
        for (var index = 0; index < Devices.Count; index++)
        {
            var source = request.Devices is { Count: > 0 } && index < request.Devices.Count
                ? request.Devices[index] : null;
            Devices[index].NameAndType = source?.NameAndType ?? "";
            Devices[index].Purpose = source?.Purpose ?? "";
            Devices[index].WhenUsed = source?.WhenUsed ?? "";
            Devices[index].Level = source?.Level;
        }
    }
}

public partial class SafetyDeviceRowViewModel : ObservableObject
{
    public SafetyDeviceRowViewModel(int number) => Number = number;
    public int Number { get; }
    public string PageLabel => Number <= 5 ? "Main form" : "Appendix B";
    [ObservableProperty] private string nameAndType = "";
    [ObservableProperty] private string purpose = "";
    [ObservableProperty] private string whenUsed = "";
    [ObservableProperty] private string? level;
    public IReadOnlyList<string> Levels { get; } = ["1", "2"];

    public SafetyDeviceEntry ToEntry() => new(NameAndType, Purpose, WhenUsed, Level);
    public void Clear() => (NameAndType, Purpose, WhenUsed, Level) = ("", "", "", null);
}

public sealed class SafetyDevicePdfReadyEventArgs(byte[] content, string suggestedFileName) : EventArgs
{
    public byte[] Content { get; } = content;
    public string SuggestedFileName { get; } = suggestedFileName;
}

public sealed class SafetyDeviceProblemEventArgs(string title, string message) : EventArgs
{
    public string Title { get; } = title;
    public string Message { get; } = message;
}
