using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private int stepIndex;
    private int visibleDeviceCount = 1;

    public SafetyDeviceViewModel(ISafetyDeviceService service,
        IFormWizardProgressService progressService)
    {
        this.service = service;
        Progress = new FormWizardProgressViewModel(progressService,
            () => FormWizardProgressJson.Capture(BuildRequest()), RestoreProgress,
            () => stepIndex);
        Progress.Watch(this);
        Progress.WatchChildren(Devices);
        foreach (var device in Devices) device.PropertyChanged += OnDeviceChanged;
        SelectedDevice = Devices[0];
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
    [ObservableProperty] private SafetyDeviceRowViewModel? selectedDevice;
    public SafetyDeviceRowViewModel CurrentDevice => SelectedDevice ?? Devices[0];
    public IReadOnlyList<SafetyDeviceRowViewModel> VisibleDevices =>
        Devices.Take(visibleDeviceCount).ToArray();
    public string DevicePosition =>
        $"Device {CurrentDevice.Number} of {visibleDeviceCount} · {CurrentDevice.PageLabel}";
    public string DeviceCountSummary =>
        $"{Devices.Count(device => device.HasAnswers)} of {SafetyDeviceRules.MaximumDevices} device recommendations started";
    public bool CanPreviousDevice => CurrentDevice.Number > 1;
    public bool CanNextDevice => CurrentDevice.Number < visibleDeviceCount;
    public bool CanAddDevice => visibleDeviceCount < SafetyDeviceRules.MaximumDevices;
    public string StepProgress => $"Step {stepIndex + 1} of 4";
    public string StepTitle => stepIndex switch
    {
        0 => "Member and program",
        1 => "Medical provider and safety devices",
        2 => "Planning team questions",
        _ => "Review and create draft"
    };
    public bool IsMemberStep => stepIndex == 0;
    public bool IsDeviceStep => stepIndex == 1;
    public bool IsPlanningStep => stepIndex == 2;
    public bool IsReviewStep => stepIndex == 3;
    public bool CanBack => stepIndex > 0;
    public bool CanNext => stepIndex < 3;
    public string SourceRevision => SafetyDeviceRules.SourceRevision;
    public bool CanGenerate => person is not null && !IsBusy;
    public event EventHandler<SafetyDevicePdfReadyEventArgs>? PdfReady;
    public event EventHandler<SafetyDeviceProblemEventArgs>? Problem;

    partial void OnSelectedDeviceChanged(SafetyDeviceRowViewModel? value) => RefreshDeviceNavigation();

    private void OnDeviceChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(DeviceCountSummary));
    }

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

    [RelayCommand]
    private void Back() => SetStep(stepIndex - 1);

    [RelayCommand]
    private void Next() => SetStep(stepIndex + 1);

    [RelayCommand]
    private void ShowMemberStep() => SetStep(0);

    [RelayCommand]
    private void ShowDeviceStep() => SetStep(1);

    [RelayCommand]
    private void ShowPlanningStep() => SetStep(2);

    [RelayCommand]
    private void ShowReviewStep() => SetStep(3);

    private void SetStep(int value)
    {
        if (value < 0 || value > 3 || value == stepIndex) return;
        stepIndex = value;
        RefreshStep();
    }

    private void RefreshStep()
    {
        OnPropertyChanged(nameof(StepProgress));
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(IsMemberStep));
        OnPropertyChanged(nameof(IsDeviceStep));
        OnPropertyChanged(nameof(IsPlanningStep));
        OnPropertyChanged(nameof(IsReviewStep));
        OnPropertyChanged(nameof(CanBack));
        OnPropertyChanged(nameof(CanNext));
    }

    [RelayCommand]
    private void PreviousDevice()
    {
        if (CanPreviousDevice) SelectedDevice = Devices[CurrentDevice.Number - 2];
    }

    [RelayCommand]
    private void NextDevice()
    {
        if (CanNextDevice) SelectedDevice = Devices[CurrentDevice.Number];
    }

    [RelayCommand]
    private void AddDevice()
    {
        if (!CanAddDevice) return;
        visibleDeviceCount++;
        OnPropertyChanged(nameof(VisibleDevices));
        SelectedDevice = Devices[visibleDeviceCount - 1];
        RefreshDeviceNavigation();
    }

    private void RefreshDeviceNavigation()
    {
        OnPropertyChanged(nameof(CurrentDevice));
        OnPropertyChanged(nameof(DevicePosition));
        OnPropertyChanged(nameof(CanPreviousDevice));
        OnPropertyChanged(nameof(CanNextDevice));
        OnPropertyChanged(nameof(CanAddDevice));
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (person is null) return;
        var request = BuildRequest();
        var errors = SafetyDeviceRules.Validate(request, DateOnly.FromDateTime(DateTime.Today));
        if (errors.Count > 0)
        {
            var first = errors.Keys.First();
            if (first.StartsWith("Devices[", StringComparison.Ordinal))
            {
                var end = first.IndexOf(']');
                if (end > 8 && int.TryParse(first.AsSpan(8, end - 8), out var index) &&
                    index >= 0 && index < Devices.Count)
                {
                    visibleDeviceCount = Math.Max(visibleDeviceCount, index + 1);
                    OnPropertyChanged(nameof(VisibleDevices));
                    SelectedDevice = Devices[index];
                    RefreshDeviceNavigation();
                }
                SetStep(1);
            }
            else if (first == nameof(SafetyDeviceRequest.PlanningTeamMeetingDate) ||
                     first == nameof(SafetyDeviceRequest.LessRestrictiveStrategies) ||
                     first == nameof(SafetyDeviceRequest.EvaluationPlan) ||
                     first == nameof(SafetyDeviceRequest.OtherResidentsAccommodations))
                SetStep(2);
            else if (first == nameof(SafetyDeviceRequest.MedicalProviderName))
                SetStep(1);
            else
                SetStep(0);
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
        visibleDeviceCount = 1;
        OnPropertyChanged(nameof(VisibleDevices));
        SelectedDevice = Devices[0];
        stepIndex = 0;
        RefreshDeviceNavigation();
        RefreshStep();
        OnPropertyChanged(nameof(DeviceCountSummary));
        StatusMessage = ReviewItemsMessage = "";
    }

    private bool CanReset() => !IsBusy;

    internal SafetyDeviceRequest BuildRequest() => new(
        MemberOrGuardianContact, ProgramNameAndAddress, ProgramContacts,
        ProgramContactNumbers, ProgramContactEmails, MedicalProviderName,
        Devices.Select(row => row.ToEntry()).ToArray(),
        LessRestrictiveStrategies, EvaluationPlan, OtherResidentsAccommodations,
        PlanningTeamMeetingDate is DateTime date ? DateOnly.FromDateTime(date) : null);

    private void RestoreProgress(string json, int savedStep)
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
        var lastEntered = Devices.ToList().FindLastIndex(device => device.HasAnswers);
        visibleDeviceCount = Math.Max(1, lastEntered + 1);
        OnPropertyChanged(nameof(VisibleDevices));
        SelectedDevice = Devices[Math.Max(0, lastEntered)];
        stepIndex = Math.Clamp(savedStep, 0, 3);
        RefreshDeviceNavigation();
        RefreshStep();
        OnPropertyChanged(nameof(DeviceCountSummary));
    }
}

public partial class SafetyDeviceRowViewModel : ObservableObject
{
    public SafetyDeviceRowViewModel(int number) => Number = number;
    public int Number { get; }
    public string PageLabel => Number <= 5 ? "Main form" : "Appendix B";
    public string NavigationLabel => string.IsNullOrWhiteSpace(NameAndType)
        ? $"Device {Number} · Not filled in"
        : $"Device {Number} · {NameAndType.Trim()}";
    public bool HasAnswers => !string.IsNullOrWhiteSpace(NameAndType) ||
        !string.IsNullOrWhiteSpace(Purpose) || !string.IsNullOrWhiteSpace(WhenUsed) ||
        !string.IsNullOrWhiteSpace(Level);
    [ObservableProperty] private string nameAndType = "";
    [ObservableProperty] private string purpose = "";
    [ObservableProperty] private string whenUsed = "";
    [ObservableProperty] private string? level;
    partial void OnNameAndTypeChanged(string value) => OnPropertyChanged(nameof(NavigationLabel));
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
