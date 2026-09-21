using Sati.ViewModels;
using Sati.ViewModels.Children;
using Sati.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.IO;

namespace Sati.Views
{
    public partial class ClientsView : UserControl
    {
        public ClientsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            SizeChanged += OnSizeChanged;
        }

        private NewClientViewModel? _viewModel;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Detach from the outgoing view model before wiring the incoming one,
            // or a re-hosted view accumulates handlers and a single regenerate
            // opens as many save dialogs as the view has been re-bound.
            if (_viewModel is not null)
            {
                _viewModel.AtRequestPdfReady -= SaveAtRequestPdf;
                _viewModel.AtRequestProblem -= ShowAtRequestProblem;
                _viewModel.ClientSaveProblemOccurred -= ShowClientSaveProblem;
                _viewModel.PersonPhoto.PhotoPickerRequested -= ChoosePersonPhoto;
                _viewModel.PersonPhoto.ProblemOccurred -= ShowPersonPhotoProblem;
                _viewModel = null;
            }

            if (e.NewValue is NewClientViewModel vm)
            {
                _viewModel = vm;
                vm.AtRequestPdfReady += SaveAtRequestPdf;
                vm.AtRequestProblem += ShowAtRequestProblem;
                vm.ClientSaveProblemOccurred += ShowClientSaveProblem;
                vm.PersonPhoto.PhotoPickerRequested += ChoosePersonPhoto;
                vm.PersonPhoto.ProblemOccurred += ShowPersonPhotoProblem;

                vm.ComplianceReviewRequested += (forms) =>
                {
                    var reviewVm = new ComplianceReviewViewModel(forms)
                    {
                        ClientName = $"{vm.FirstName} {vm.LastName}"
                    };

                    var dialog = new ComplianceReviewWindow(reviewVm)
                    {
                        Owner = Application.Current.MainWindow
                    };

                    var confirmed = dialog.ShowDialog() == true;
                    if (confirmed)
                        reviewVm.Commit();

                    return confirmed;
                };

                ApplyResponsiveLayout(vm, ActualWidth);
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_viewModel is null || !double.IsFinite(e.NewSize.Width) || e.NewSize.Width <= 0)
                return;

            ApplyResponsiveLayout(_viewModel, e.NewSize.Width);
        }

        private static void ApplyResponsiveLayout(NewClientViewModel viewModel, double width)
        {
            const double compactBoundary = 1150;
            const double expansionMargin = 48;
            var compact = viewModel.IsCompactDisplayMode
                ? width < compactBoundary + expansionMargin
                : width < compactBoundary;
            viewModel.SetCompactDisplayMode(compact);
        }

        // Regenerated from the stored record by the view model; the view only owns
        // the file dialog. Same helper the AT request editor uses, so saving a
        // freshly published request and re-saving a filed one behave identically.
        private async void SaveAtRequestPdf(object? sender, ATRequestPdfReadyEventArgs e) =>
            await PdfFileSaver.SaveAsync(
                "Save AT request", e.SuggestedFileName, e.Content, "The AT request was saved.");

        private void ShowAtRequestProblem(object? sender, ATRequestProblemEventArgs e) =>
            MessageBox.Show(e.Message, e.Title, MessageBoxButton.OK, MessageBoxImage.Warning);

        private void ShowClientSaveProblem(object? sender, ClientSaveProblemEventArgs e) =>
            MessageBox.Show(
                e.Problem.Message,
                e.Problem.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);

        private async void ChoosePersonPhoto(object? sender, EventArgs e)
        {
            if (_viewModel is null)
                return;

            var dialog = new OpenFileDialog
            {
                Title = "Choose a consumer profile photo",
                Filter = "JPG or PNG image (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                if (new FileInfo(dialog.FileName).Length > ProfilePhotoPreparer.MaximumInputBytes)
                {
                    ShowPersonPhotoProblem(this, new PersonPhotoProblemEventArgs(
                        "Photo not saved", "That file is larger than 40 MB. Choose a smaller photo."));
                    return;
                }

                var bytes = await File.ReadAllBytesAsync(dialog.FileName);
                // Any size, shape, or orientation is made upright, cropped square by the case
                // manager, and re-encoded at 512 pixels before it reaches the photo rules.
                var upright = ProfilePhotoPreparer.LoadUpright(bytes, out var problem);
                if (upright is null)
                {
                    ShowPersonPhotoProblem(this, new PersonPhotoProblemEventArgs(
                        "Photo not saved", problem ?? "Sati could not open that image."));
                    return;
                }

                var crop = new PhotoCropWindow { Owner = Window.GetWindow(this) };
                crop.Configure(upright);
                if (crop.ShowDialog() != true || crop.PreparedPhoto is not { } prepared)
                    return;

                await _viewModel.PersonPhoto.SaveSelectedAsync(prepared);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"Sati could not read that image. {exception.Message}",
                    "Photo not saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ShowPersonPhotoProblem(object? sender, PersonPhotoProblemEventArgs e) =>
            MessageBox.Show(
                e.Message,
                e.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

        private void ClientList_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (DataContext is NewClientViewModel vm && vm.SelectedPerson is Person person)
            {
                vm.LoadPersonForEdit(person);
                vm.IsEditMode = true;
            }
        }
    }
}
