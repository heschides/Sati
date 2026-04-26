using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data.Billing;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Sati.ViewModels.Billing
{
    public partial class BillingQueueViewModel : ObservableObject
    {
        private readonly IBillingService _billingService;

        public ObservableCollection<BillingQueueItemViewModel> QueueItems { get; } = [];

        [ObservableProperty] private bool isBusy;
        public bool HasLoaded { get; private set; }

        public int ValidCount => QueueItems.Count(r => r.IsValid);
        public int InvalidCount => QueueItems.Count(r => !r.IsValid);
        public int SelectedValidCount => QueueItems.Count(r => r.IsSelected && r.IsValid);

        public BillingQueueViewModel(IBillingService billingService)
        {
            _billingService = billingService;
        }

        public async Task LoadAsync()
        {
            IsBusy = true;
            Debug.WriteLine($"[BillingQueue] LoadAsync started — {DateTime.Now:HH:mm:ss.fff}");
            var notes = await _billingService.GetApprovedUnbilledNotesAsync();
            Debug.WriteLine($"[BillingQueue] GetApprovedUnbilledNotesAsync returned {notes.Count()} notes — {DateTime.Now:HH:mm:ss.fff}");
            QueueItems.Clear();
            foreach (var note in notes)
                QueueItems.Add(new BillingQueueItemViewModel(_billingService.ValidateNoteForBilling(note)));
            RefreshCounts();
            HasLoaded = true;
            IsBusy = false;
            Debug.WriteLine($"[BillingQueue] LoadAsync complete — {DateTime.Now:HH:mm:ss.fff}");

        }

        [RelayCommand]
        private async Task PromoteAsync(BillingQueueItemViewModel item)
        {
            if (!item.IsValid)
                return;

            await _billingService.CreateClaimLineAsync(
                item.Result.Note.Id,
                item.Result.Note.ComplianceOverride,
                item.Result.Note.OverrideReason);

            QueueItems.Remove(item);
            RefreshCounts();
        }

        [RelayCommand]
        private async Task PromoteSelectedAsync()
        {
            var toPromote = QueueItems
                .Where(r => r.IsSelected && r.IsValid)
                .ToList();

            foreach (var item in toPromote)
            {
                await _billingService.CreateClaimLineAsync(
                    item.Result.Note.Id,
                    item.Result.Note.ComplianceOverride,
                    item.Result.Note.OverrideReason);

                QueueItems.Remove(item);
            }

            RefreshCounts();
        }

        [RelayCommand]
        private void SelectAllReady()
        {
            foreach (var item in QueueItems.Where(r => r.IsValid))
                item.IsSelected = true;
            RefreshCounts();
        }

        [RelayCommand]
        private async Task RefreshAsync() => await LoadAsync();

        private void RefreshCounts()
        {
            OnPropertyChanged(nameof(ValidCount));
            OnPropertyChanged(nameof(InvalidCount));
            OnPropertyChanged(nameof(SelectedValidCount));
        }
    }
}