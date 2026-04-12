using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace Sati.ViewModels
{
    public partial class ScratchpadHistoryViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services & private state
        // -------------------------------------------------------------------------

        private readonly IScratchpadService _scratchpadService;
        private readonly ISessionService _sessionService;

        private readonly ObservableCollection<Scratchpad> _entries = [];

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public ScratchpadHistoryViewModel(IScratchpadService scratchpadService, ISessionService sessionService)
        {
            _scratchpadService = scratchpadService;
            _sessionService = sessionService;

            EntriesView = CollectionViewSource.GetDefaultView(_entries);
            EntriesView.Filter = Filter;
        }

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private Scratchpad? selectedEntry;
        [ObservableProperty] private string? searchText;

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnSelectedEntryChanged(Scratchpad? value) => OnPropertyChanged(nameof(SelectedContent));
        partial void OnSearchTextChanged(string? value) => EntriesView.Refresh();

        // -------------------------------------------------------------------------
        // Computed properties & views
        // -------------------------------------------------------------------------

        public ICollectionView EntriesView { get; }
        public string SelectedContent => SelectedEntry?.Content ?? string.Empty;

        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            var userId = _sessionService.CurrentUser!.Id;
            var entries = await _scratchpadService.GetHistoryAsync(userId);
            foreach (var entry in entries)
                _entries.Add(entry);
        }

        // -------------------------------------------------------------------------
        // Private methods
        // -------------------------------------------------------------------------

        private bool Filter(object obj)
        {
            if (obj is not Scratchpad entry) return false;
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            return entry.Content.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }
    }
}