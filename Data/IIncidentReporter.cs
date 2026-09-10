using Sati.Contracts.V1;

namespace Sati.Data;

public interface IIncidentReporter
{
    Task ReportAsync(
        Exception exception,
        string operation,
        string reference,
        string severity = "Error",
        CancellationToken cancellationToken = default);

    Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task ReportCrashAsync(
        Exception exception,
        string operation,
        string reference,
        CrashDiagnosticDto diagnostic,
        string severity = IncidentSeverities.Critical,
        CancellationToken cancellationToken = default) =>
        ReportAsync(exception, operation, reference, severity, cancellationToken);
}
