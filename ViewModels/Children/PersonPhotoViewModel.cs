using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;

namespace Sati.ViewModels.Children;

/// <summary>
/// Owns the selected consumer's photo without putting image bytes on Person or in a
/// caseload result. Every selection clears first, then a latest-request check prevents
/// an outgoing consumer's photo from appearing under the incoming consumer.
/// </summary>
public partial class PersonPhotoViewModel(
    IPersonPhotoService? photoService,
    ISessionService sessionService) : ObservableObject
{
    private readonly LatestRequestTracker _loads = new();
    private int? _personId;
    private long? _revision;
    private bool _canChange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    private byte[]? photoBytes;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? statusMessage;

    public bool HasPhoto => PhotoBytes is { Length: > 0 };
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool CanChoosePhoto => _personId is not null && _canChange && !IsLoading && !IsSaving;
    public bool CanRemovePhoto => CanChoosePhoto && HasPhoto && _revision is not null;

    public event EventHandler? PhotoPickerRequested;
    public event EventHandler<PersonPhotoProblemEventArgs>? ProblemOccurred;

    public async Task LoadForAsync(Person? person)
    {
        var account = sessionService.CurrentUser;
        var request = _loads.Begin();
        _personId = person?.Id;
        _revision = null;
        _canChange = person is not null && account?.HasCaseManagerPermissions == true &&
                     person.UserId == account.Id;
        PhotoBytes = null;
        StatusMessage = null;
        IsLoading = person is not null && photoService is not null;
        RefreshCommands();

        if (person is null || photoService is null)
        {
            IsLoading = false;
            RefreshCommands();
            return;
        }

        try
        {
            var photo = await photoService.GetAsync(person.Id);
            if (!_loads.IsCurrent(request) || _personId != person.Id ||
                !ReferenceEquals(sessionService.CurrentUser, account))
                return;

            PhotoBytes = photo?.Content;
            _revision = photo?.Revision;
        }
        catch
        {
            if (_loads.IsCurrent(request) && _personId == person.Id)
                StatusMessage = "The profile photo could not be loaded.";
            throw;
        }
        finally
        {
            if (_loads.IsCurrent(request))
            {
                IsLoading = false;
                RefreshCommands();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanChoosePhoto))]
    private void ChoosePhoto() => PhotoPickerRequested?.Invoke(this, EventArgs.Empty);

    public async Task SaveSelectedAsync(byte[] content)
    {
        if (photoService is null || _personId is not int personId || !CanChoosePhoto)
            return;

        var inspection = PersonPhotoRules.Inspect(content, null, out var problem);
        if (inspection is null)
        {
            ShowProblem("Photo not saved", problem ?? "Choose a valid JPG or PNG photo.");
            return;
        }

        var account = sessionService.CurrentUser;
        var revision = _revision;
        IsSaving = true;
        StatusMessage = "Saving photo…";
        RefreshCommands();
        try
        {
            var saved = await photoService.SaveAsync(
                personId,
                inspection.ContentType,
                content,
                revision);
            if (_personId != personId || !ReferenceEquals(sessionService.CurrentUser, account))
                return;

            PhotoBytes = saved.Content;
            _revision = saved.Revision;
            StatusMessage = "Photo saved.";
        }
        catch (Exception exception)
        {
            if (_personId == personId && ReferenceEquals(sessionService.CurrentUser, account))
            {
                StatusMessage = "The photo was not saved.";
                ShowProblem("Photo not saved", exception.Message);
            }
        }
        finally
        {
            IsSaving = false;
            RefreshCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemovePhoto))]
    private async Task RemovePhotoAsync()
    {
        if (photoService is null || _personId is not int personId || _revision is not long revision)
            return;

        var account = sessionService.CurrentUser;
        IsSaving = true;
        StatusMessage = "Removing photo…";
        RefreshCommands();
        try
        {
            await photoService.DeleteAsync(personId, revision);
            if (_personId != personId || !ReferenceEquals(sessionService.CurrentUser, account))
                return;

            PhotoBytes = null;
            _revision = null;
            StatusMessage = "Photo removed.";
        }
        catch (Exception exception)
        {
            if (_personId == personId && ReferenceEquals(sessionService.CurrentUser, account))
            {
                StatusMessage = "The photo was not removed.";
                ShowProblem("Photo not removed", exception.Message);
            }
        }
        finally
        {
            IsSaving = false;
            RefreshCommands();
        }
    }

    partial void OnPhotoBytesChanged(byte[]? value) => RefreshCommands();
    partial void OnIsLoadingChanged(bool value) => RefreshCommands();
    partial void OnIsSavingChanged(bool value) => RefreshCommands();

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanChoosePhoto));
        OnPropertyChanged(nameof(CanRemovePhoto));
        ChoosePhotoCommand.NotifyCanExecuteChanged();
        RemovePhotoCommand.NotifyCanExecuteChanged();
    }

    private void ShowProblem(string title, string message) =>
        ProblemOccurred?.Invoke(this, new PersonPhotoProblemEventArgs(title, message));
}

public sealed class PersonPhotoProblemEventArgs(string title, string message) : EventArgs
{
    public string Title { get; } = title;
    public string Message { get; } = message;
}
