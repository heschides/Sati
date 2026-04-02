using CommunityToolkit.Mvvm.ComponentModel;
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

        private readonly ObservableCollection<Note> _allNotes = [];

        // PROPERTIES
        public ICollectionView NotesView { get; }

        // null = "All Clients" sentinel; the ItemTemplate uses FallbackValue to display it
        public ObservableCollection<Person?> FilterPeople { get; } = [null];

        [ObservableProperty] private Person? selectedFilterPerson;
        [ObservableProperty] private Note? selectedNote;
        [ObservableProperty] private string? searchText;

        partial void OnSelectedFilterPersonChanged(Person? value) => NotesView.Refresh();
        partial void OnSearchTextChanged(string? value) => NotesView.Refresh();

        // CONSTRUCTOR
        public NotesWindowViewModel(IPersonService personService, ISessionService sessionService)
        {
            _sessionService = sessionService; 
            _personService = personService;
            NotesView = CollectionViewSource.GetDefaultView(_allNotes);
            NotesView.Filter = FilterNotes;
            _ = LoadAsync();
            
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
                || note.PersonId == SelectedFilterPerson.Id;

            var matchesSearch = string.IsNullOrWhiteSpace(SearchText)
                || note.Narrative.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

            return matchesPerson && matchesSearch;
        }
    }
}
