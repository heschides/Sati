namespace Sati.Services;

// Closed vocabulary only: never accept record IDs, names, narratives or exception messages.
public enum DiagnosticOperation
{
    CaseManagementNavigation = 0, SupervisorNavigation = 1, UserManagementNavigation = 2,
    BillingNavigation = 3, RepresentativePayeeNavigation = 4, AdminNavigation = 5,
    PlatformHealthNavigation = 6, ChatNavigation = 7, OtherNavigation = 8,
    SupervisorChartCleanup = 9, OverviewAgendaRegistration = 10, OverviewAgendaRemoval = 11,
    AgendaMoveToOverview = 12, AgendaMoveToSidebar = 13
}

public enum DiagnosticPhase { Started = 0, Completed = 1 }

internal sealed record DiagnosticBreadcrumb(long Sequence, DateTime TimestampUtc,
    DiagnosticOperation Operation, DiagnosticPhase Phase);

internal static class DiagnosticBreadcrumbs
{
    internal const int Limit = 32;
    internal static IReadOnlyList<DiagnosticBreadcrumb> Sanitize(IEnumerable<DiagnosticBreadcrumb>? entries) =>
        entries?.Where(entry => entry is not null && entry.Sequence > 0 &&
            entry.TimestampUtc.Kind == DateTimeKind.Utc && Enum.IsDefined(entry.Operation) &&
            Enum.IsDefined(entry.Phase)).TakeLast(Limit).ToArray() ?? [];
}
