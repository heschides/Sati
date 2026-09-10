using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Sati.Reporting;
using Sati.Services;
using System.Collections.ObjectModel;
using System.Net;

namespace Sati.ViewModels.ClientDocuments;

public partial class CheckRequestsViewModel(
    ICheckRequestService service,
    ISessionService session,
    CheckRequestPdfExporter pdfExporter) : ObservableObject
{
    private readonly LatestRequestTracker requests = new();
    private Person? person;
    private CheckRequest? current;
    private bool suppressSelectionLoad;

    public ObservableCollection<CheckRequestListItem> Items { get; } = [];
    [ObservableProperty] private CheckRequestListItem? selectedItem;
    [ObservableProperty] private DateTime? requestDate;
    [ObservableProperty] private string payableTo = "";
    [ObservableProperty] private string mailingAddress = "";
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private DateTime? neededByDate;
    [ObservableProperty] private string reason = "";
    [ObservableProperty] private string message = "Select a consumer.";
    [ObservableProperty] private bool isBusy;

    public string ConsumerName => current?.ConsumerName ?? person?.FullName ?? "Consumer";
    public string AgencyName => current?.AgencyName ?? session.CurrentUser?.Agency?.Name ?? "";
    public string CaseManagerName => current?.CaseManagerName ?? session.CurrentUser?.DisplayName ?? "";
    public string SupervisorName => current?.SupervisorName ?? session.CurrentUser?.Supervisor?.DisplayName ?? "";
    public bool HasItems => Items.Count > 0;
    public bool HasSelection => current is not null;
    public bool CanCreate => person is not null && person.UserId == session.CurrentUser?.Id && !IsBusy;
    public bool CanEdit => CanCreate && current is { IsPublished: false };
    public bool CanPublish => CanEdit;
    public bool CanRegenerate => current?.IsPublished == true && !IsBusy;
    public string EditorPrompt
    {
        get
        {
            if (person is null) return "Select a consumer to view their check requests.";
            if (IsBusy) return "Loading check requests…";
            if (person.UserId != session.CurrentUser?.Id)
                return "You can review this consumer's requests, but only their assigned case manager can create or edit one.";
            return "Choose Create draft to start entering a new check request, or select an existing request from the history.";
        }
    }
    public string Status => current is null ? "No request selected" : current.IsPublished ? "PDF prepared · read-only" : "Draft";
    public string PublicationNote => current?.PublishedAtUtc is DateTime published
        ? $"PDF prepared {published.ToLocalTime():g} by {current.PublishedByName}. This records neither supervisor approval nor delivery to Finance."
        : "Publish PDF freezes this version and creates the attachment. The printed staff names preserve the existing form; they are not electronic signatures or supervisor approval.";
    public string DeliveryInstructions => current?.IsPublished == true
        ? "Save the PDF, attach it to your email to Finance, and CC your supervisor."
        : "Current process: prepare the PDF, email it to Finance, and CC your supervisor. Sati does not send or approve it yet.";

    public event Action<CheckRequestPdfReadyEventArgs>? PdfReady;

    public void SetPerson(Person? selected)
    {
        requests.Invalidate();
        person = selected;
        current = null;
        Items.Clear();
        SelectedItem = null;
        ClearEditor();
        Message = selected is null ? "Select a consumer." : "Loading check requests…";
        NotifyState();
        _ = ReloadAsync();
    }

    partial void OnSelectedItemChanged(CheckRequestListItem? value)
    {
        if (suppressSelectionLoad) return;
        if (value is null) { current = null; ClearEditor(); NotifyState(); return; }
        _ = LoadAsync(value.Id);
    }

    partial void OnIsBusyChanged(bool value) => NotifyState();

    private void NotifyState()
    {
        OnPropertyChanged(nameof(ConsumerName));
        OnPropertyChanged(nameof(AgencyName));
        OnPropertyChanged(nameof(CaseManagerName));
        OnPropertyChanged(nameof(SupervisorName));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanPublish));
        OnPropertyChanged(nameof(CanRegenerate));
        OnPropertyChanged(nameof(EditorPrompt));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(PublicationNote));
        OnPropertyChanged(nameof(DeliveryInstructions));
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        var selected = person;
        var account = session.CurrentUser;
        if (selected is null) return;
        var ticket = requests.Begin();
        IsBusy = true;
        try
        {
            var rows = await service.GetAllForPersonAsync(selected.Id);
            if (!requests.IsCurrent(ticket) || person?.Id != selected.Id || !ReferenceEquals(session.CurrentUser, account)) return;
            Items.Clear();
            foreach (var row in rows) Items.Add(row);
            Message = rows.Count == 0 ? "No check requests yet. Create the first draft when one is needed." : "";
            NotifyState();
        }
        catch (Exception error)
        {
            if (requests.IsCurrent(ticket)) Message = LoadFailure(error);
        }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        if (!CanCreate || person is null) return;
        var ticket = requests.Begin();
        IsBusy = true; Message = "";
        try
        {
            var created = await service.CreateDraftAsync(person.Id);
            if (!requests.IsCurrent(ticket)) return;
            current = created;
            Apply(created);
            await RefreshListAsync(created.Id, ticket);
            Message = "Draft created with the consumer and assigned staff details filled in.";
        }
        catch (Exception ex) { if (requests.IsCurrent(ticket)) Message = Friendly(ex); }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }

    private async Task LoadAsync(int id)
    {
        var ticket = requests.Begin();
        IsBusy = true; Message = "";
        try
        {
            var loaded = await service.GetByIdAsync(id);
            if (!requests.IsCurrent(ticket)) return;
            current = loaded;
            if (loaded is null) { Message = "That check request is no longer available."; ClearEditor(); }
            else Apply(loaded);
        }
        catch (Exception ex) { if (requests.IsCurrent(ticket)) Message = Friendly(ex); }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }

    [RelayCommand] private Task SaveAsync() => SaveCoreAsync(publish: false);
    [RelayCommand] private Task PublishAsync() => SaveCoreAsync(publish: true);

    private async Task SaveCoreAsync(bool publish)
    {
        if (current is null || (publish ? !CanPublish : !CanEdit)) return;
        CopyEditorTo(current);
        var blockers = publish
            ? CheckRequestPublication.FindPublicationBlockers(RequestDate, PayableTo, MailingAddress, Amount, NeededByDate, Reason, current.IsPublished)
            : CheckRequestPublication.FindDraftErrors(RequestDate, PayableTo, MailingAddress, Amount, NeededByDate, Reason);
        if (blockers.Count > 0) { Message = string.Join(" ", blockers); return; }

        var ticket = requests.Begin();
        IsBusy = true; Message = "";
        try
        {
            var saved = publish ? await service.PublishAsync(current) : await service.UpdateAsync(current);
            if (!requests.IsCurrent(ticket)) return;
            current = saved;
            Apply(saved);
            await RefreshListAsync(saved.Id, ticket);
            if (publish)
            {
                PdfReady?.Invoke(new CheckRequestPdfReadyEventArgs(
                    pdfExporter.Generate(saved), CheckRequestPdfExporter.SuggestedFileName(saved)));
                Message = "PDF prepared and locked. Choose where to save the attachment.";
            }
            else Message = "Draft saved.";
        }
        catch (Exception ex) { if (requests.IsCurrent(ticket)) Message = Friendly(ex); }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }

    [RelayCommand]
    private void Regenerate()
    {
        if (!CanRegenerate || current is null) return;
        PdfReady?.Invoke(new CheckRequestPdfReadyEventArgs(
            pdfExporter.Generate(current), CheckRequestPdfExporter.SuggestedFileName(current)));
        Message = "The PDF was regenerated from the frozen request.";
    }

    private async Task RefreshListAsync(int selectId, int ticket)
    {
        if (person is null) return;
        var rows = await service.GetAllForPersonAsync(person.Id);
        if (!requests.IsCurrent(ticket)) return;
        Items.Clear();
        foreach (var row in rows) Items.Add(row);
        suppressSelectionLoad = true;
        try { SelectedItem = Items.FirstOrDefault(x => x.Id == selectId); }
        finally { suppressSelectionLoad = false; }
        NotifyState();
    }

    private void Apply(CheckRequest request)
    {
        RequestDate = request.RequestDate;
        PayableTo = request.PayableTo ?? "";
        MailingAddress = request.MailingAddress ?? "";
        Amount = request.Amount;
        NeededByDate = request.NeededByDate;
        Reason = request.Reason ?? "";
        NotifyState();
    }

    private void CopyEditorTo(CheckRequest request)
    {
        request.RequestDate = RequestDate;
        request.PayableTo = PayableTo;
        request.MailingAddress = MailingAddress;
        request.Amount = Amount;
        request.NeededByDate = NeededByDate;
        request.Reason = Reason;
    }

    private void ClearEditor()
    {
        RequestDate = null; PayableTo = ""; MailingAddress = ""; Amount = 0;
        NeededByDate = null; Reason = "";
    }

    private static string Friendly(Exception error) => error switch
    {
        CheckRequestConcurrencyException => error.Message,
        CheckRequestLockedException => error.Message,
        UnauthorizedAccessException => error.Message,
        InvalidOperationException => error.Message,
        _ => "The check request operation could not be completed. Reload and try again."
    };

    private static string LoadFailure(Exception error) => error switch
    {
        CloudApiException { StatusCode: HttpStatusCode.NotFound } =>
            "Check Requests is not installed in this Demo environment yet. Its database migration and matching API release must be installed first.",
        CloudConnectivityException => "Sati could not reach the Demo service. Check the connection and try again.",
        _ => "Check requests could not be loaded. Reload and try again."
    };
}

public sealed record CheckRequestPdfReadyEventArgs(byte[] Content, string SuggestedFileName);
