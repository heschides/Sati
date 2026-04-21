using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace Sati.ViewModels
{
    public partial class NotesWindowViewModel : ObservableObject
    {
        // FIELDS
        private readonly IPersonService _personService;
        private readonly ISessionService _sessionService;
        private readonly INoteService _noteService;


        private readonly ObservableCollection<Note> _allNotes = [];
        public record StatusFilterOption(string Label, NoteStatus? Value);

        // PROPERTIES
        public ICollectionView NotesView { get; }
        public ObservableCollection<StatusFilterOption> FilterStatusOptions { get; } =
[
    new("All Statuses", null),
    new("Pending",      NoteStatus.Pending),
    new("Logged",       NoteStatus.Logged),
    new("Abandoned",    NoteStatus.Abandoned),
    new("Scheduled",    NoteStatus.Scheduled )

];

        private static readonly Person AllPersonsSentinel = Person.CreateSentinel("All Persons");

        public ObservableCollection<Person?> FilterPeople { get; } = [AllPersonsSentinel];


        [ObservableProperty] private Person? selectedFilterPerson;
        [ObservableProperty] private Note? selectedNote;
        [ObservableProperty] private string? searchText;
        [ObservableProperty] private StatusFilterOption? filterStatus;
        partial void OnFilterStatusChanged(StatusFilterOption? value) => NotesView.Refresh();

        partial void OnSelectedFilterPersonChanged(Person? value) => NotesView.Refresh();
        partial void OnSearchTextChanged(string? value) => NotesView.Refresh();

        // CONSTRUCTOR
        public NotesWindowViewModel(IPersonService personService, ISessionService sessionService, INoteService noteService)
        {
            _sessionService = sessionService; 
            _personService = personService;
            _noteService = noteService;
            NotesView = CollectionViewSource.GetDefaultView(_allNotes);
            NotesView.Filter = FilterNotes;
            _ = LoadAsync();
            
        }
        // RELAY COMMANDS
        [RelayCommand]
        private async Task MarkNoteLogged()
        {
            if (SelectedNote is null) return;
            SelectedNote.Status = NoteStatus.Logged;
            await _noteService.UpdateNoteAsync(SelectedNote);
            NotesView.Refresh();
        }
        // METHODS
        private async Task LoadAsync()
        {
            var user = _sessionService.CurrentUser!.Id;

            var people = await _personService.GetAllPeopleAsync(user);
            foreach (var person in people)
            {
                FilterPeople.Add(person);
                foreach (var note in person.Notes)
                    _allNotes.Add(note);
            }
        }

        private bool FilterNotes(object obj)
        {
            if (obj is not Note note) return false;

            var matchesPerson = SelectedFilterPerson is null
                || ReferenceEquals(SelectedFilterPerson, AllPersonsSentinel)
                || note.PersonId == SelectedFilterPerson.Id;

            var matchesSearch = string.IsNullOrWhiteSpace(SearchText)
                || note.Narrative.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

            var matchesStatus = FilterStatus is null || note.Status == FilterStatus.Value;

            return matchesPerson && matchesSearch && matchesStatus;
        }

        public async Task ReloadAsync()
        {
            _allNotes.Clear();
            FilterPeople.Clear();
            FilterPeople.Add(null);

            var user = _sessionService.CurrentUser!.Id;
            var people = await _personService.GetAllPeopleAsync(user);
            foreach (var person in people)
            {
                FilterPeople.Add(person);
                foreach (var note in person.Notes)
                    _allNotes.Add(note);
            }
            NotesView.Refresh();
        }
    }
}
