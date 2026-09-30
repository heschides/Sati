using System.Reflection;
using System.Text.Json;
using System.ComponentModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Sati.Services;

namespace Sati.ViewModels.ClientDocuments;

/// <summary>One resumable answer set for the selected client, form, and signed-in author.</summary>
public partial class FormWizardProgressViewModel : ObservableObject
{
    private readonly IFormWizardProgressService service;
    private readonly Func<string> capture;
    private readonly Action<string, int> restore;
    private readonly Func<int> currentStep;
    private int? personId;
    private string formKey = string.Empty;
    private readonly LatestRequestTracker selections = new();
    private int currentSelectionTicket;
    private int revision;
    private FormWizardProgressDto? saved;
    private readonly List<INotifyPropertyChanged> watchedChildren = [];
    private string baselineAnswers = "{}";
    private int baselineStep;

    public FormWizardProgressViewModel(IFormWizardProgressService service,
        Func<string> capture, Action<string, int> restore, Func<int>? currentStep = null)
    {
        this.service = service;
        this.capture = capture;
        this.restore = restore;
        this.currentStep = currentStep ?? (() => 0);
    }

    [ObservableProperty] private string status = "Select a consumer to load saved progress.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool mustResume;
    [ObservableProperty] private bool hasUnsavedChanges;
    public bool CanSave => personId is not null && !IsBusy && !MustResume;
    public string CurrentKey => formKey;
    public bool CanResume => saved is not null && !IsBusy;
    public bool CanReload => personId is not null && !IsBusy;

    public void Watch(INotifyPropertyChanged source) =>
        source.PropertyChanged += (_, _) => MarkEdited();

    public void WatchChildren(IEnumerable<INotifyPropertyChanged> children)
    {
        foreach (var child in watchedChildren) child.PropertyChanged -= ChildChanged;
        watchedChildren.Clear();
        watchedChildren.AddRange(children);
        foreach (var child in watchedChildren) child.PropertyChanged += ChildChanged;
    }

    private void ChildChanged(object? sender, PropertyChangedEventArgs args) => MarkEdited();

    private void MarkEdited()
    {
        if (personId is null || IsBusy || MustResume) return;
        var dirty = saved is null
            ? capture() != baselineAnswers || currentStep() != baselineStep
            : capture() != saved.AnswersJson || currentStep() != saved.StepIndex;
        if (dirty == HasUnsavedChanges) return;
        HasUnsavedChanges = dirty;
        Status = dirty
            ? "Unsaved changes. Choose Save progress before leaving this form."
            : saved is null ? "No saved progress for this form."
            : $"All changes saved {saved.UpdatedAtUtc.ToLocalTime():g}.";
    }

    public void SetPerson(Person? person, string key)
    {
        currentSelectionTicket = selections.Begin();
        personId = person?.Id;
        formKey = key;
        baselineAnswers = person is null ? "{}" : capture();
        baselineStep = currentStep();
        revision = 0;
        saved = null;
        HasUnsavedChanges = false;
        IsBusy = person is not null;
        MustResume = false;
        Status = person is null ? "Select a consumer to load saved progress." : "Loading saved progress...";
        NotifyCommands();
        if (person is not null)
            _ = LoadAsync(person.Id, key, currentSelectionTicket, baselineAnswers, baselineStep);
    }

    private async Task LoadAsync(int id, string key, int version, string baseline, int baselineStep)
    {
        try
        {
            var loaded = await service.GetAsync(id, key);
            if (!selections.IsCurrent(version) || personId != id || formKey != key) return;
            saved = loaded;
            revision = loaded?.Revision ?? 0;
            HasUnsavedChanges = false;
            if (loaded is null)
                Status = "No saved progress for this form. Choose Save progress as you work.";
            else if (capture() == baseline && currentStep() == baselineStep)
            {
                restore(loaded.AnswersJson, loaded.StepIndex);
                Status = $"Resumed draft saved {loaded.UpdatedAtUtc.ToLocalTime():g}.";
            }
            else
            {
                MustResume = true;
                Status = "A saved draft exists. Choose Resume saved progress before saving changes.";
            }
        }
        catch (Exception)
        {
            if (selections.IsCurrent(version))
            {
                MustResume = true;
                Status = "Saved progress could not be loaded. Try again or contact support.";
            }
        }
        finally
        {
            if (selections.IsCurrent(version)) { IsBusy = false; NotifyCommands(); MarkEdited(); }
        }
    }

    [RelayCommand(CanExecute = nameof(CanReload))]
    private void Reload()
    {
        if (personId is not int id) return;
        currentSelectionTicket = selections.Begin();
        IsBusy = true;
        MustResume = false;
        Status = "Reloading saved progress...";
        _ = LoadAsync(id, formKey, currentSelectionTicket, capture(), currentStep());
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (personId is not int id) return;
        var version = currentSelectionTicket;
        var key = formKey;
        var request = new SaveFormWizardProgressRequest(revision, currentStep(), capture());
        if (!FormWizardProgressRules.Valid(key, request))
        {
            Status = "The draft could not be saved because its answers are invalid or too large.";
            return;
        }
        IsBusy = true;
        Status = "Saving progress...";
        try
        {
            var updated = await service.SaveAsync(id, key, request);
            if (!selections.IsCurrent(version) || personId != id || formKey != key) return;
            saved = updated;
            revision = updated.Revision;
            HasUnsavedChanges = false;
            Status = $"Progress saved {updated.UpdatedAtUtc.ToLocalTime():g}. You can return later.";
        }
        catch (Exception error) when (error is DbUpdateConcurrencyException or
            CloudApiException { StatusCode: HttpStatusCode.Conflict })
        {
            if (selections.IsCurrent(version))
            {
                MustResume = true;
                Status = "This draft changed in another session. Choose Reload saved before editing it.";
            }
        }
        catch (Exception)
        {
            if (selections.IsCurrent(version))
                Status = "Progress was not saved. Try Save progress again.";
        }
        finally
        {
            if (selections.IsCurrent(version)) { IsBusy = false; NotifyCommands(); MarkEdited(); }
        }
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume()
    {
        if (saved is null) return;
        try
        {
            restore(saved.AnswersJson, saved.StepIndex);
            MustResume = false;
            HasUnsavedChanges = false;
            Status = $"Resumed draft saved {saved.UpdatedAtUtc.ToLocalTime():g}.";
        }
        catch (Exception)
        {
            MustResume = true;
            Status = "Saved progress could not be applied. Contact support.";
        }
        NotifyCommands();
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnMustResumeChanged(bool value) => NotifyCommands();
    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanReload));
        SaveCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
    }
}

internal static class FormWizardProgressJson
{
    public static string Capture<T>(T value) => JsonSerializer.Serialize(value);

    /// <summary>Apply only scalar properties explicitly present on the form request contract.</summary>
    public static void ApplyScalars<T>(object viewModel, T request)
    {
        if (request is null) return;
        var targetType = viewModel.GetType();
        foreach (var source in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var target = targetType.GetProperty(source.Name);
            if (target?.CanWrite != true || target.SetMethod?.IsPublic != true) continue;
            var value = source.GetValue(request);
            if (source.PropertyType == target.PropertyType &&
                (source.PropertyType.IsPrimitive || source.PropertyType == typeof(string) ||
                 source.PropertyType == typeof(decimal?) || source.PropertyType == typeof(bool?) ||
                 source.PropertyType == typeof(DateTime?) || source.PropertyType.IsEnum))
                target.SetValue(viewModel, value);
            else if (source.PropertyType == typeof(DateOnly?) && target.PropertyType == typeof(DateTime?))
                target.SetValue(viewModel, value is DateOnly date ? date.ToDateTime(TimeOnly.MinValue) : null);
        }
    }
}
