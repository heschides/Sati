namespace Sati.Services;

/// <summary>Keeps the calendar ViewModel independent of the Windows file dialog.</summary>
public interface IOutlookCalendarFilePicker
{
    string? PickCalendarFile();
}
