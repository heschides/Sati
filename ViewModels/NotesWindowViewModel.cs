using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace Sati.ViewModels
{
    public record StatusOption(NoteStatus? Value, string Display);

    public partial class NotesWindowViewModel : ObservableObject
    {
        // FIELDS
        private readonly IPersonService _personService;
        private readonly ISessionService _sessionService;
        private readonly INoteService _noteService;

        private readonly ObservableCollection<Note> _allNotes = [];

        private static readonly Person AllPersonsSentinel = Person.CreateSentinel("All Persons");

        // FILTER OPTIONS
        public static IReadOnlyList<StatusOption> StatusOptions { get; } =
        [
            new(null, "All Statuses"),
            ..Enum.GetValues<NoteStatus>().Select(s => new StatusOption(s, s.ToString()))
        ];

        public ObservableCollection<Person?> FilterPeople { get; } = [AllPersonsSentinel];

        // PROPERTIES
        public ICollectionView NotesView { get; }

        [ObservableProperty] private Person? _selectedFilterPerson = AllPersonsSentinel;
        [ObservableProperty] private StatusOption _selectedStatusOption = StatusOptions[0];
        [ObservableProperty] private Note? _selectedNote;
        [ObservableProperty] private string? _searchText;

        // CALLBACKS
        partial void OnSelectedFilterPersonChanged(Person? value) => NotesView.Refresh();
        partial void OnSelectedStatusOptionChanged(StatusOption value) => NotesView.Refresh();
        partial void OnSearchTextChanged(string? value) => NotesView.Refresh();

        // CONSTRUCTOR
        public NotesWindowViewModel(IPersonService personService, ISessionService sessionService, INoteService noteService)
        {
            _personService = personService;
            _sessionService = sessionService;
            _noteService = noteService;
            NotesView = CollectionViewSource.GetDefaultView(_allNotes);
            NotesView.Filter = FilterNotes;
        }

        // COMMANDS
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
            var userId = _sessionService.CurrentUser!.Id;
            var people = await _personService.GetAllPeopleAsync(userId);

            foreach (var person in people)
            {
                FilterPeople.Add(person);
                foreach (var note in person.Notes)
                    _allNotes.Add(note);
            }
        }

        public async Task ReloadAsync()
        {
            _allNotes.Clear();
            FilterPeople.Clear();
            FilterPeople.Add(AllPersonsSentinel);

            var userId = _sessionService.CurrentUser!.Id;
            var people = await _personService.GetAllPeopleAsync(userId);

            foreach (var person in people)
            {
                FilterPeople.Add(person);
                foreach (var note in person.Notes)
                    _allNotes.Add(note);
            }

            NotesView.Refresh();
        }

        private bool FilterNotes(object obj)
        {
            if (obj is not Note note) return false;

            var matchesPerson = ReferenceEquals(SelectedFilterPerson, AllPersonsSentinel)
                || note.PersonId == SelectedFilterPerson?.Id;

            var matchesStatus = SelectedStatusOption.Value is null
                || note.Status == SelectedStatusOption.Value;

            var matchesSearch = string.IsNullOrWhiteSpace(SearchText)
                || note.Narrative.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

            return matchesPerson && matchesStatus && matchesSearch;
        }
    }
}