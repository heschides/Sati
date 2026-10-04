namespace Sati.Contracts.V1;

/// <summary>The one decision for the system's Pending-note documentation-window transition.</summary>
public static class NoteAbandonmentRules
{
    public const int DesktopBatchSize = 1_000;

    public static bool IsEligible(int? status, DateTime? eventDate, DateTime localToday,
        int documentationWindowDays) =>
        NoteWorkflow.CanSystemAbandon(status) &&
        eventDate is DateTime date &&
        ProductivityForecast.IsDocumentationWindowClosed(
            date, localToday, documentationWindowDays);
}
