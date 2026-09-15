using Sati.Services;
using System.Windows;

namespace Sati.Views;

public sealed class FormOpeningPrompt : IFormOpeningPrompt
{
    public DateTime? SelectActualOpeningDate(
        string formLabel,
        DateTime availableOn,
        DateTime latestAllowedDate)
    {
        var dialog = new FormOpeningDialog(
            formLabel,
            availableOn,
            latestAllowedDate)
        {
            Owner = Application.Current?.MainWindow
        };
        return dialog.ShowDialog() == true ? dialog.OpenedOn : null;
    }
}
