using Microsoft.Win32;
using Sati.Services;

namespace Sati.Views;

internal sealed class OutlookCalendarFilePicker : IOutlookCalendarFilePicker
{
    public string? PickCalendarFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an Outlook calendar export",
            Filter = "Outlook calendar (*.ics)|*.ics|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
