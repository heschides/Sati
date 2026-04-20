using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Sati.ViewModels.Supervisor
{
    public partial class PendingApprovalsViewModel : ObservableObject
    {
        private readonly ISupervisorService _supervisorService;
        private readonly ISessionService _sessionService;

        public PendingApprovalsViewModel(
            ISupervisorService supervisorService,
            ISessionService sessionService)
        {
            _supervisorService = supervisorService;
            _sessionService = sessionService;
        }

        public ObservableCollection<PendingNoteViewModel> PendingNotes { get; } = [];

        [ObservableProperty] private PendingNoteViewModel? selectedNote;
        [ObservableProperty] private string? returnReason;
        [ObservableProperty] private bool isReturnDialogVisible;

        public bool HasPending => PendingNotes.Count > 0;
        public string EmptyStateMessage => "No notes pending approval.";

        public async Task LoadAsync(int? filterByUserId = null)
        {
            try
            {
                PendingNotes.Clear();

                var supervisor = _sessionService.CurrentUser!;
                var notes = await _supervisorService.GetPendingNotesAsync(
                    supervisor.Id,
                    allSupervisees: filterByUserId is null);

                var filtered = filterByUserId is null
                    ? notes
                    : notes.Where(n => n.Person.UserId == filterByUserId);
                Debug.WriteLine($"notes.Count={notes.Count()}, filtered.Count={filtered.Count()}, filterByUserId={filterByUserId}");

                foreach (var note in filtered)
                    PendingNotes.Add(new PendingNoteViewModel(note));
                Debug.WriteLine($"After foreach: PendingNotes.Count={PendingNotes.Count}");

                OnPropertyChanged(nameof(HasPending));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PendingApprovalsViewModel.LoadAsync failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task Approve(PendingNoteViewModel note)
        {
            try
            {
                var supervisor = _sessionService.CurrentUser!;
                await _supervisorService.ApproveNoteAsync(note.NoteId, supervisor.Id);
                PendingNotes.Remove(note);
                OnPropertyChanged(nameof(HasPending));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Approve failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void OpenReturnDialog(PendingNoteViewModel note)
        {
            SelectedNote = note;
            ReturnReason = string.Empty;
            IsReturnDialogVisible = true;
        }

        [RelayCommand]
        private async Task ConfirmReturn()
        {
            if (SelectedNote is null || string.IsNullOrWhiteSpace(ReturnReason))
                return;

            try
            {
                var supervisor = _sessionService.CurrentUser!;
                await _supervisorService.ReturnNoteAsync(
                    SelectedNote.NoteId,
                    supervisor.Id,
                    ReturnReason);

                PendingNotes.Remove(SelectedNote);
                IsReturnDialogVisible = false;
                SelectedNote = null;
                ReturnReason = string.Empty;
                OnPropertyChanged(nameof(HasPending));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Return failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void CancelReturn()
        {
            IsReturnDialogVisible = false;
            SelectedNote = null;
            ReturnReason = string.Empty;
        }
    }

    public class PendingNoteViewModel
    {
        public int NoteId { get; }
        public string ClientName { get; }
        public DateTime? EventDate { get; }
        public NoteType? NoteType { get; }
        public decimal? Units { get; }
        public string Narrative { get; }

        public PendingNoteViewModel(Note note)
        {
            NoteId = note.Id;
            ClientName = $"{note.Person.FirstName} {note.Person.LastName}";
            EventDate = note.EventDate;
            NoteType = note.NoteType;
            Units = note.Units;
            Narrative = note.Narrative;
        }
    }
}