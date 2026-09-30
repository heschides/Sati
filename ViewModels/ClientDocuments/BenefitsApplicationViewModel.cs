using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;

namespace Sati.ViewModels.ClientDocuments;

public partial class BenefitsApplicationViewModel : ObservableObject
{
    private readonly IBenefitsApplicationService service;
    private Person? person;
    private int personVersion;
    private int stepIndex;

    public BenefitsApplicationViewModel(IBenefitsApplicationService service,
        IFormWizardProgressService progressService)
    {
        this.service = service;
        Fields = BenefitsApplicationRules.Fields.Select(field =>
            new BenefitsAnswerViewModel(field)).ToList();
        AssignExclusiveGroups();
        Steps = BenefitsApplicationRules.Steps;
        Progress = new FormWizardProgressViewModel(progressService,
            CaptureProgress, RestoreProgress, () => stepIndex);
        Progress.Watch(this);
        Progress.WatchChildren(Fields);
        RefreshStep();
    }

    public IReadOnlyList<BenefitsApplicationStep> Steps { get; }
    public FormWizardProgressViewModel Progress { get; }
    public IReadOnlyList<BenefitsAnswerViewModel> Fields { get; private set; }
    public IReadOnlyList<string> YesNoOptions { get; } = ["", "Yes", "No"];
    public string SourceRevision => BenefitsApplicationRules.SourceRevision;
    public string PersonName => person?.FullName ?? "Select a consumer";
    public string StepTitle => Steps[stepIndex].Title;
    public string StepProgress => $"Step {stepIndex + 1} of {Steps.Count} - source page {Steps[stepIndex].Page}";
    public IReadOnlyList<BenefitsAnswerViewModel> CurrentFields =>
        Fields.Where(item => item.Definition.Page == Steps[stepIndex].Page).ToList();
    public bool CanBack => stepIndex > 0;
    public bool CanNext => stepIndex < Steps.Count - 1;
    public bool IsFinalStep => stepIndex == Steps.Count - 1;
    public bool CanGenerate => person is not null && !IsBusy;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;

    public event EventHandler<BenefitsApplicationPdfReadyEventArgs>? PdfReady;
    public event EventHandler<BenefitsApplicationProblemEventArgs>? Problem;

    public void SetPerson(Person? value)
    {
        personVersion++;
        person = value;
        IsBusy = false;
        Fields = BenefitsApplicationRules.Fields.Select(field =>
            new BenefitsAnswerViewModel(field)).ToList();
        Progress.WatchChildren(Fields);
        AssignExclusiveGroups();
        SetDefault("person1.homeAddress", value?.Address ?? value?.BillingStreet);
        SetDefault("person1.mailingAddress", value?.Address ?? value?.BillingStreet);
        SetDefault("person1.phone", value?.PhoneNumber);
        SetDefault("person1.email", value?.Email);
        if (value?.Gender == global::Sati.Gender.Male) SetDefault("person1.male", "True");
        if (value?.Gender == global::Sati.Gender.Female) SetDefault("person1.female", "True");
        if (value?.Gender == global::Sati.Gender.NonBinary) SetDefault("person1.nonbinary", "True");
        stepIndex = 0;
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(PersonName));
        OnPropertyChanged(nameof(Fields));
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
        RefreshStep();
        Progress.SetPerson(value, "benefits-application");
    }

    [RelayCommand]
    private void Back()
    {
        if (!CanBack) return;
        stepIndex--;
        RefreshStep();
    }

    [RelayCommand]
    private void Next()
    {
        if (!CanNext) return;
        stepIndex++;
        RefreshStep();
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (person is null) return;
        var id = person.Id;
        var version = personVersion;
        var answers = Fields.Where(field => !string.IsNullOrWhiteSpace(field.Value))
            .ToDictionary(field => field.Definition.Key, field => field.Value, StringComparer.Ordinal);
        var request = new BenefitsApplicationRequest(answers);
        var validation = BenefitsApplicationRules.Validate(request);
        if (validation.Count > 0)
        {
            StatusMessage = string.Join(" ", validation.SelectMany(entry =>
                entry.Value.Select(message => $"{entry.Key}: {message}")));
            return;
        }

        IsBusy = true;
        StatusMessage = "Preparing the 20-page application draft...";
        try
        {
            var result = await service.GenerateAsync(id, request);
            if (version != personVersion || person?.Id != id) return;
            StatusMessage = "Draft ready. Review all 20 pages, complete unanswered questions and required signatures, then submit through an approved channel.";
            PdfReady?.Invoke(this, new BenefitsApplicationPdfReadyEventArgs(result.Pdf, result.FileName));
        }
        catch (Exception exception)
        {
            if (version != personVersion || person?.Id != id) return;
            StatusMessage = "The application draft could not be generated.";
            Problem?.Invoke(this, new BenefitsApplicationProblemEventArgs(
                "Application Draft Not Generated", exception.Message));
        }
        finally
        {
            if (version == personVersion) IsBusy = false;
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    private void SetDefault(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var field = Fields.First(item => item.Definition.Key == key);
        field.Value = value.Length <= field.Definition.MaxLength
            ? value : value[..field.Definition.MaxLength];
    }

    private void AssignExclusiveGroups()
    {
        for (var index = 1; index <= 6; index++)
        {
            var prefix = $"person{index}.";
            AssignGroup(prefix + "male", prefix + "female", prefix + "nonbinary");
            AssignGroup(prefix + "single", prefix + "married");
            AssignGroup(prefix + "hispanic", prefix + "notHispanic");
        }
    }

    private void AssignGroup(params string[] keys)
    {
        var members = Fields.Where(item => keys.Contains(item.Definition.Key,
            StringComparer.Ordinal)).ToArray();
        foreach (var member in members)
            member.ExclusivePeers = members.Where(peer => peer != member).ToArray();
    }

    private void RefreshStep()
    {
        OnPropertyChanged(nameof(CurrentFields));
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(StepProgress));
        OnPropertyChanged(nameof(CanBack));
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(IsFinalStep));
        BackCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    private string CaptureProgress() => FormWizardProgressJson.Capture(new BenefitsApplicationRequest(
        Fields.Where(field => !string.IsNullOrWhiteSpace(field.Value))
            .ToDictionary(field => field.Definition.Key, field => field.Value, StringComparer.Ordinal)));

    private void RestoreProgress(string json, int savedStep)
    {
        var request = System.Text.Json.JsonSerializer.Deserialize<BenefitsApplicationRequest>(json)
            ?? throw new InvalidOperationException("The saved application is empty.");
        if (request.Answers is null)
            throw new InvalidOperationException("The saved application has no answers.");
        foreach (var field in Fields)
            field.Value = request.Answers.TryGetValue(field.Definition.Key, out var answer) &&
                          answer.Length <= field.MaxLength ? answer : string.Empty;
        stepIndex = Math.Clamp(savedStep, 0, Steps.Count - 1);
        RefreshStep();
    }
}

public partial class BenefitsAnswerViewModel(BenefitsAnswerField definition) : ObservableObject
{
    internal IReadOnlyList<BenefitsAnswerViewModel> ExclusivePeers { get; set; } = [];
    public BenefitsAnswerField Definition { get; } = definition;
    public string Label => Definition.Label;
    public int MaxLength => Definition.MaxLength;
    public bool IsText => Definition.Kind == BenefitsAnswerKind.Text && !Definition.Sensitive;
    public bool IsSensitive => Definition.Kind == BenefitsAnswerKind.Text && Definition.Sensitive;
    public bool IsYesNo => Definition.Kind == BenefitsAnswerKind.YesNo;
    public bool IsCheck => Definition.Kind == BenefitsAnswerKind.Check;
    [ObservableProperty] private string value = string.Empty;
    public bool IsChecked
    {
        get => Value == "True";
        set { if (Value != (value ? "True" : "False")) Value = value ? "True" : "False"; }
    }
    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsChecked));
        if (value == "True")
            foreach (var peer in ExclusivePeers)
                peer.Value = "False";
    }
}

public sealed class BenefitsApplicationPdfReadyEventArgs(byte[] pdf, string fileName) : EventArgs
{
    public byte[] Pdf { get; } = pdf;
    public string FileName { get; } = fileName;
}

public sealed class BenefitsApplicationProblemEventArgs(string title, string message) : EventArgs
{
    public string Title { get; } = title;
    public string Message { get; } = message;
}
