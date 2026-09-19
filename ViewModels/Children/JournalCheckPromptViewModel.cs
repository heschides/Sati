using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.ViewModels.Children;

/// <summary>What the case manager chose when a checked journal passage became a note.</summary>
public sealed record JournalCheckNoteRequest(int PersonId, string Text, DateTime Date, NoteType NoteType);

/// <summary>
/// The question asked after text in the journal is checked: should it also go on the
/// calendar? Declining leaves only the checkbox in the journal. Accepting writes one ordinary
/// Scheduled note through the owner's note service — the same save path, tenancy,
/// normalization, and audit as a note from the note panel — with the checked text as its
/// narrative. Nothing here writes to the journal.
/// </summary>
public partial class JournalCheckPromptViewModel : ObservableObject
{
    private readonly Func<JournalCheckNoteRequest, Task> _createNote;
    private readonly Func<DateTime> _today;
    private int _personId;

    public JournalCheckPromptViewModel(
        Func<JournalCheckNoteRequest, Task> createNote,
        Func<DateTime>? today = null)
    {
        _createNote = createNote;
        _today = today ?? (() => DateTime.Today);
    }

    /// <summary>Work types a Scheduled entry can be. Reminder is its own choice, not one of these.</summary>
    public IReadOnlyList<NoteType> ScheduledNoteTypes { get; } =
        [NoteType.Visit, NoteType.Phone, NoteType.Email, NoteType.Other];

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextPreview))]
    private string text = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignCommand))]
    private DateTime? reminderDate;

    // False is a Reminder; true is Scheduled work of ScheduledNoteType.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReminder))]
    private bool isScheduled;

    [ObservableProperty]
    private NoteType scheduledNoteType = NoteType.Other;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignCommand))]
    private bool isBusy;

    public bool IsReminder
    {
        get => !IsScheduled;
        set => IsScheduled = !value;
    }

    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

    public DateTime Today => _today().Date;

    public string TextPreview => Text.Length <= 120 ? Text : string.Concat(Text.AsSpan(0, 117), "…");

    /// <summary>
    /// Asks about <paramref name="checkedText"/> for the client whose journal it is in. The
    /// client is fixed here, when the text was checked, so a note can never land on whoever
    /// happens to be selected when the answer comes. Whitespace-only text is not a reminder,
    /// so nothing opens.
    /// </summary>
    public void Open(int personId, string? checkedText)
    {
        var trimmed = (checkedText ?? string.Empty).Trim();
        if (trimmed.Length == 0 || personId <= 0)
            return;
        _personId = personId;
        Text = trimmed;
        ReminderDate = Today;
        IsScheduled = false;
        ScheduledNoteType = NoteType.Other;
        ErrorMessage = null;
        IsOpen = true;
    }

    public int PersonId => _personId;

    [RelayCommand]
    private void Decline()
    {
        if (IsBusy)
            return;
        Close();
    }

    /// <summary>Withdraws the question — the client or the page it was asked about is gone.</summary>
    public void Close()
    {
        IsOpen = false;
        ErrorMessage = null;
    }

    private bool CanAssign() => !IsBusy && ReminderDate.HasValue;

    [RelayCommand(CanExecute = nameof(CanAssign))]
    private async Task Assign()
    {
        if (ReminderDate is not DateTime date)
        {
            ErrorMessage = "Choose a date.";
            return;
        }
        // A past date would be lapsed the moment it was written — off the calendar
        // and out of every count — so it is refused rather than silently lost.
        if (date.Date < Today)
        {
            ErrorMessage = "Choose today or a later date.";
            return;
        }
        if (Text.Length > JournalEntry.MaxTextLength)
        {
            ErrorMessage = $"A reminder is limited to {JournalEntry.MaxTextLength} characters. Check a shorter passage.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var noteType = IsScheduled ? ScheduledNoteType : NoteType.Reminder;
            await _createNote(new JournalCheckNoteRequest(_personId, Text, date.Date, noteType));
            IsOpen = false;
        }
        catch (UnauthorizedAccessException)
        {
            ErrorMessage = "This client is not on your caseload, so the note was not created.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception)
        {
            ErrorMessage = "The note could not be created. The checkbox stays in the journal; try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
