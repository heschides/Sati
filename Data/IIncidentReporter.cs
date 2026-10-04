using Sati.Contracts.V1;

namespace Sati.Data;

/// <summary>Local delivery state only; no incident content or account identifiers.</summary>
public sealed record IncidentOutboxSupportStatus(
    int PendingForCurrentAccount,
    TimeSpan? OldestPendingAge,
    int HeldForOtherAccounts,
    int Quarantined);

public interface IIncidentReporter
{
    Task ReportAsync(
        Exception exception,
        string operation,
        string reference,
        string severity = "Error",
        CancellationToken cancellationToken = default);

    Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    IncidentOutboxSupportStatus? GetOutboxStatus() => null;

    Task ReportCrashAsync(
        Exception exception,
        string operation,
        string reference,
        CrashDiagnosticDto diagnostic,
        string severity = IncidentSeverities.Critical,
        CancellationToken cancellationToken = default) =>
        ReportAsync(exception, operation, reference, severity, cancellationToken);
}
