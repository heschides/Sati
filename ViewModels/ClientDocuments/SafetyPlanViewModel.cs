using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Sati.ViewModels.ClientDocuments;

public partial class SafetyPlanViewModel(
    ISafetyPlanService service,
    ISessionService session,
    ISettingsService? settings = null) : ObservableObject
{
    private readonly LatestRequestTracker requests = new();
    private Person? person;
    private SafetyPlanDto? plan;
    private int personVersion;
    private int openDaysBefore = 90;
    private int dueDaysBeforeEffective;
    [ObservableProperty] private string personName = "Select a consumer";
    [ObservableProperty] private DateTime? cycleStart;
    [ObservableProperty] private string message = "Select a consumer.";
    [ObservableProperty] private string returnReason = "";
    [ObservableProperty] private bool isBusy;
    public ObservableCollection<SafetyPlanSectionViewModel> Sections { get; } = [];
    public bool CanAuthor => person is not null && IsSelectedCycleAvailable &&
        session.CurrentUser is { } actor &&
        SafetyPlanRules.CanAuthor(actor.Id, actor.Permissions, person.UserId);
    public bool CanEdit => CanAuthor && plan?.Status == "Draft" && !IsBusy;
    public bool CanReview => plan is not null && session.CurrentUser is { } actor &&
        SafetyPlanRules.CanReview(actor.Id, actor.Permissions, plan.AuthorUserId) && plan.Status == "ReadyForReview" && !IsBusy;
    public string Status => plan is null ? "No plan for this cycle" : $"Version {plan.Version} · {plan.Status}";
    public string AvailabilityMessage => CycleStart is DateTime target && !IsSelectedCycleAvailable
        ? $"This safety plan becomes available on {target.Date.AddDays(-dueDaysBeforeEffective).AddDays(-openDaysBefore):MMM d, yyyy}."
        : string.Empty;
    private bool IsSelectedCycleAvailable => CycleStart is DateTime target &&
        AnnualDocumentCycle.IsAvailable(
            target, DateTime.Today, openDaysBefore, dueDaysBeforeEffective);
    public bool IsApproved => plan?.Status == "Approved";
    public event Action<AgencyReleaseResult>? PdfReady;
    partial void OnIsBusyChanged(bool value) => NotifyState();
    partial void OnCycleStartChanged(DateTime? value)
    {
        requests.Invalidate(); plan = null; Sections.Clear(); ReturnReason = ""; IsBusy = false;
        Message = value is DateTime target && !IsSelectedCycleAvailable
            ? $"This safety plan becomes available on {target.Date.AddDays(-dueDaysBeforeEffective).AddDays(-openDaysBefore):MMM d, yyyy}."
            : "Open the selected annual period to view its saved plan, or start a new revision.";
        NotifyState();
    }
    public void SetPerson(Person? selected)
    {
        var version = ++personVersion;
        PersonName = selected?.FullName ?? "Select a consumer";
        requests.Invalidate(); person = selected; plan = null; Sections.Clear(); IsBusy = false;
        CycleStart = selected?.EffectiveDate is DateTime effective
            ? AnnualDocumentCycle.SuggestedStart(
                effective, DateTime.Today, openDaysBefore, dueDaysBeforeEffective)
            : null;
        ReturnReason = "";
        Message = selected is null
            ? "Select a consumer."
            : IsSelectedCycleAvailable
                ? ""
                : $"This safety plan becomes available on {CycleStart!.Value.Date.AddDays(-dueDaysBeforeEffective).AddDays(-openDaysBefore):MMM d, yyyy}.";
        NotifyState();
        _ = ReloadAsync();
        if (selected?.EffectiveDate is DateTime selectedEffective && settings is not null)
            _ = ApplyConfiguredCycleAsync(selected.Id, selectedEffective, version);
    }
    private async Task ApplyConfiguredCycleAsync(int personId, DateTime effective, int version)
    {
        try
        {
            var configured = await settings!.LoadAsync();
            if (version != personVersion || person?.Id != personId)
                return;

            var configuredOpenDays = Math.Max(0, configured.SafetyPlanOpenDaysBefore);
            var configuredDueDays = Math.Max(0, configured.SafetyPlanDaysBeforeAnniversary);
            var suggested = AnnualDocumentCycle.SuggestedStart(
                effective, DateTime.Today, configuredOpenDays, configuredDueDays);
            openDaysBefore = configuredOpenDays;
            dueDaysBeforeEffective = configuredDueDays;
            if (CycleStart?.Date != suggested)
            {
                CycleStart = suggested;
                await ReloadAsync();
            }
            else
            {
                NotifyState();
            }
        }
        catch (Exception) when (version != personVersion || person?.Id != personId)
        {
            // A late settings failure belongs to a consumer that is no longer selected.
        }
        catch (Exception)
        {
            Message = "Sati could not load the configured safety-plan window. The default 90-day window remains selected.";
        }
    }
    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanAuthor)); OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanReview)); OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(AvailabilityMessage));
        OnPropertyChanged(nameof(IsApproved));
    }
    private void Apply(SafetyPlanDto? value)
    {
        plan = value; Sections.Clear();
        if (value is not null)
        {
            var document = JsonSerializer.Deserialize<SafetyPlanDocument>(value.DocumentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            foreach (var section in document.Sections) Sections.Add(new(section.Id, section.Text));
            ReturnReason = value.ReturnReason ?? "";
        }
        NotifyState();
    }
    private async Task Run(
        Func<int, DateTime, Task<SafetyPlanDto?>> operation,
        Func<DateTime, string>? successMessage = null)
    {
        if (IsBusy) return;
        if (person is null || CycleStart is null) { Message = "Select a consumer with an effective date."; return; }
        var id = person.Id; var cycle = CycleStart.Value.Date; var ticket = requests.Begin();
        IsBusy = true; Message = "";
        try
        {
            var value = await operation(id, cycle);
            if (requests.IsCurrent(ticket))
            {
                Apply(value);
                if (successMessage is not null)
                    Message = successMessage(cycle);
            }
        }
        catch (SafetyPlanWorkflowException error) { if (requests.IsCurrent(ticket)) Message = error.Message; }
        catch (Exception) { if (requests.IsCurrent(ticket)) Message = "The operation could not be completed. Check the cycle and permissions, then reload to check the latest version."; }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }
    [RelayCommand] private Task ReloadAsync() => Run(
        service.GetAsync,
        cycle => $"Safety-plan status loaded for the service year beginning {cycle:MMMM d, yyyy}.");
    [RelayCommand] private Task StartAsync() => Run(async (id, cycle) => await service.StartAsync(id, cycle));
    [RelayCommand] private Task SaveAsync() => Change("save");
    [RelayCommand] private Task SubmitAsync() => Change("submit");
    [RelayCommand] private Task ApproveAsync() => Change("approve");
    [RelayCommand] private Task ReturnAsync() => Change("return");
    private Task Change(string action)
    {
        if (plan is null || IsBusy) return Task.CompletedTask;
        var snapshot = plan; var reason = ReturnReason;
        var document = JsonSerializer.Serialize(new SafetyPlanDocument(1, Sections.Select(x => new SafetyPlanSection(x.Id, x.Text)).ToList()));
        return Run(async (_, _) =>
        {
            if (action == "submit") snapshot = await service.ChangeAsync(snapshot, "save", document);
            return await service.ChangeAsync(snapshot, action, action == "save" ? document : null, reason);
        });
    }
    [RelayCommand] private async Task GenerateAsync()
    {
        if (person is null || CycleStart is null || IsBusy) return;
        var ticket = requests.Begin(); var id = person.Id; var cycle = CycleStart.Value; IsBusy = true;
        try { var pdf = await service.GenerateAsync(id, cycle); if (requests.IsCurrent(ticket)) PdfReady?.Invoke(pdf); }
        catch (Exception) { if (requests.IsCurrent(ticket)) Message = "The PDF could not be generated. Save and reload the plan, then try again."; }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }
}

public partial class SafetyPlanSectionViewModel(string id, string text) : ObservableObject
{
    public string Id { get; } = id;
    public string Title => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Id.Replace('-', ' '));
    [ObservableProperty]
    private string text = text;
}
