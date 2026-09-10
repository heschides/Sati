using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;

namespace Sati.Services;

internal sealed record ApplicationRunMarker(
    int AgencyId,
    string Scope,
    string Role,
    string Release,
    DateTime StartedAtUtc,
    int ProcessId = 0,
    string ProcessName = "unknown",
    DateTime LastHeartbeatUtc = default,
    string SessionReference = "",
    string? LastReportedDiagnosticStatus = null);

internal sealed class UnexpectedApplicationTerminationException : Exception
{
    public UnexpectedApplicationTerminationException()
        : base("The preceding Sati session did not record a graceful exit.")
    {
    }
}

/// <summary>
/// Owns the durable fact that an authenticated Sati session is running. Fatal-crash details are
/// never produced live by the dead process: a later authenticated launch reads the surviving
/// marker and correlates it with the Windows Application log.
/// </summary>
public sealed class ApplicationRunState
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EventLogRetryDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan PendingMarkerRetention = TimeSpan.FromDays(30);
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly IWindowsCrashEventReader _eventReader;
    private readonly TimeSpan _heartbeatInterval;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly string? _diagnosticLogDirectory;
    private string? _activeMarkerPath;
    private ApplicationRunMarker? _activeMarker;
    private Timer? _heartbeatTimer;

    internal ApplicationRunState(IWindowsCrashEventReader eventReader) : this(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SatiLogica", "Sati", "RunState"),
        eventReader,
        DefaultHeartbeatInterval,
        diagnosticLogDirectory: null)
    {
    }

    internal ApplicationRunState(string directory) : this(
        directory,
        new WindowsCrashEventReader(),
        DefaultHeartbeatInterval,
        diagnosticLogDirectory: Path.Combine(directory, "Logs"))
    {
    }

    internal ApplicationRunState(
        string directory,
        IWindowsCrashEventReader eventReader,
        TimeSpan heartbeatInterval,
        Func<DateTime>? utcNow = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        string? diagnosticLogDirectory = null)
    {
        _directory = directory;
        _eventReader = eventReader;
        _heartbeatInterval = heartbeatInterval;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _delay = delay ?? Task.Delay;
        _diagnosticLogDirectory = diagnosticLogDirectory;
    }

    public async Task StartSessionAsync(
        User user,
        IIncidentReporter reporter,
        CancellationToken cancellationToken = default)
    {
        MarkGracefulExit();
        var scope = user.Role == UserRole.PlatformOperator
            ? IncidentScopes.Platform
            : IncidentScopes.Agency;
        var path = Path.Combine(_directory, $"agency-{user.AgencyId}-{scope.ToLowerInvariant()}.json");
        try
        {
            Directory.CreateDirectory(_directory);
            ArchiveSurvivingMarker(path, user, scope);

            var now = AsUtc(_utcNow());
            var marker = new ApplicationRunMarker(
                user.AgencyId,
                scope,
                user.Role.ToString(),
                Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown",
                now,
                Environment.ProcessId,
                SafeProcessName(Process.GetCurrentProcess().ProcessName),
                now,
                NewCrashReference());
            lock (_sync)
            {
                WriteMarker(path, marker);
                _activeMarkerPath = path;
                _activeMarker = marker;
                _heartbeatTimer = new Timer(
                    _ => RefreshHeartbeat(),
                    null,
                    _heartbeatInterval,
                    _heartbeatInterval);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Sati run marker could not be prepared. Failure type: {exception.GetType().FullName}");
            return;
        }

        try
        {
            await ReplayPendingCrashesAsync(user.AgencyId, scope, reporter, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"Sati crash readback could not finish. Failure type: {exception.GetType().FullName}");
        }
    }

    public void MarkGracefulExit()
    {
        lock (_sync)
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            _activeMarker = null;
            if (_activeMarkerPath is null)
                return;

            try
            {
                if (File.Exists(_activeMarkerPath))
                    File.Delete(_activeMarkerPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Sati graceful-exit marker could not be removed. Failure type: {exception.GetType().FullName}");
            }
            finally
            {
                _activeMarkerPath = null;
            }
        }
    }

    private void RefreshHeartbeat()
    {
        lock (_sync)
        {
            if (_activeMarkerPath is null || _activeMarker is null)
                return;
            try
            {
                _activeMarker = _activeMarker with { LastHeartbeatUtc = AsUtc(_utcNow()) };
                WriteMarker(_activeMarkerPath, _activeMarker);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Sati run heartbeat could not be written. Failure type: {exception.GetType().FullName}");
            }
        }
    }

    private void ArchiveSurvivingMarker(string activePath, User user, string scope)
    {
        if (!File.Exists(activePath))
            return;

        var previous = TryReadMarker(activePath);
        var fileTime = File.GetLastWriteTimeUtc(activePath);
        previous ??= new ApplicationRunMarker(
            user.AgencyId,
            scope,
            user.Role.ToString(),
            "unknown",
            fileTime,
            LastHeartbeatUtc: fileTime,
            SessionReference: NewCrashReference());
        previous = NormalizePreviousMarker(previous, user.AgencyId, scope, fileTime);

        var pendingDirectory = Path.Combine(_directory, "Pending");
        Directory.CreateDirectory(pendingDirectory);
        var pendingPath = Path.Combine(
            pendingDirectory,
            $"crash-{previous.AgencyId}-{previous.Scope.ToLowerInvariant()}-{previous.SessionReference}.json");
        WriteMarker(pendingPath, previous);
        File.Delete(activePath);
    }

    private async Task ReplayPendingCrashesAsync(
        int agencyId,
        string scope,
        IIncidentReporter reporter,
        CancellationToken cancellationToken)
    {
        var pendingDirectory = Path.Combine(_directory, "Pending");
        if (!Directory.Exists(pendingDirectory))
            return;

        foreach (var path in Directory.EnumerateFiles(pendingDirectory, "crash-*.json")
                     .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var marker = TryReadMarker(path);
            if (marker is null || marker.AgencyId != agencyId || marker.Scope != scope)
                continue;

            if (AsUtc(_utcNow()) - EffectiveHeartbeat(marker, File.GetLastWriteTimeUtc(path)) > PendingMarkerRetention)
            {
                File.Delete(path);
                continue;
            }

            var diagnostic = await ResolveDiagnosticAsync(marker, cancellationToken);
            var previousQuality = marker.LastReportedDiagnosticStatus is { } status
                ? CrashDiagnosticRules.Quality(status)
                : -1;
            var currentQuality = CrashDiagnosticRules.Quality(diagnostic.Status);
            var shouldReport = previousQuality < 0 || currentQuality > previousQuality;
            if (shouldReport)
            {
                var exception = new UnexpectedApplicationTerminationException();
                AppErrorLog.Record(
                    exception,
                    "application.previous-session-unclean",
                    _diagnosticLogDirectory,
                    referenceOverride: marker.SessionReference);
                AppErrorLog.RecordCrashDiagnostic(
                    marker.SessionReference,
                    diagnostic,
                    _diagnosticLogDirectory);
                try
                {
                    await reporter.ReportCrashAsync(
                        exception,
                        "application.previous-session-unclean",
                        marker.SessionReference,
                        diagnostic,
                        IncidentSeverities.Critical,
                        cancellationToken);
                }
                catch
                {
                    // Keep the marker. The same stable reference makes the eventual retry
                    // idempotent in both the local and API-backed incident aggregators.
                    continue;
                }

                marker = marker with { LastReportedDiagnosticStatus = diagnostic.Status };
            }

            if (diagnostic.Status is CrashDiagnosticStatuses.Matched or CrashDiagnosticStatuses.CorrelationUnavailable)
            {
                // Persist the terminal replay state before deletion. If deletion itself is
                // interrupted, the next launch retries cleanup without writing/reporting again.
                WriteMarker(path, marker);
                File.Delete(path);
            }
            else
                WriteMarker(path, marker);
        }
    }

    private async Task<CrashDiagnosticDto> ResolveDiagnosticAsync(
        ApplicationRunMarker marker,
        CancellationToken cancellationToken)
    {
        var heartbeat = EffectiveHeartbeat(marker, marker.StartedAtUtc);
        if (marker.ProcessId <= 0 || marker.ProcessName == "unknown")
        {
            return new CrashDiagnosticDto(
                CrashDiagnosticStatuses.CorrelationUnavailable,
                0,
                "unknown",
                heartbeat);
        }

        var lookup = await _eventReader.FindAsync(
            marker.ProcessId,
            marker.ProcessName,
            heartbeat,
            cancellationToken);
        if (lookup.State == WindowsCrashLookupState.NotFound)
        {
            await _delay(EventLogRetryDelay, cancellationToken);
            lookup = await _eventReader.FindAsync(
                marker.ProcessId,
                marker.ProcessName,
                heartbeat,
                cancellationToken);
        }

        if (lookup.State == WindowsCrashLookupState.ApplicationLogUnavailable)
        {
            return new CrashDiagnosticDto(
                CrashDiagnosticStatuses.ApplicationLogUnavailable,
                marker.ProcessId,
                marker.ProcessName,
                heartbeat);
        }

        if (lookup.Match is not { } match)
        {
            return new CrashDiagnosticDto(
                CrashDiagnosticStatuses.PendingOrUnavailable,
                marker.ProcessId,
                marker.ProcessName,
                heartbeat);
        }

        return new CrashDiagnosticDto(
            CrashDiagnosticStatuses.Matched,
            marker.ProcessId,
            marker.ProcessName,
            heartbeat,
            1000,
            match.RecordId,
            AsUtc(match.OccurredAtUtc),
            match.Provider,
            match.FaultingApplication,
            match.FaultingApplicationVersion,
            match.FaultingModule,
            match.FaultingModuleVersion,
            match.ExceptionCode,
            match.FaultOffset,
            match.DotNetRuntimeEventObserved);
    }

    private static ApplicationRunMarker NormalizePreviousMarker(
        ApplicationRunMarker marker,
        int expectedAgencyId,
        string expectedScope,
        DateTime fallbackHeartbeatUtc) => marker with
    {
        // The authenticated actor and the known marker filename own tenant scope. Marker content
        // is local input and is never trusted to choose a directory or an incident agency.
        AgencyId = expectedAgencyId,
        Scope = expectedScope,
        ProcessName = SafeProcessName(marker.ProcessName),
        LastHeartbeatUtc = EffectiveHeartbeat(marker, fallbackHeartbeatUtc),
        SessionReference = IsSafeReference(marker.SessionReference)
            ? marker.SessionReference
            : NewCrashReference()
    };

    private static DateTime EffectiveHeartbeat(ApplicationRunMarker marker, DateTime fallbackUtc) =>
        AsUtc(marker.LastHeartbeatUtc != default
            ? marker.LastHeartbeatUtc
            : marker.StartedAtUtc != default
                ? marker.StartedAtUtc
                : fallbackUtc);

    private static ApplicationRunMarker? TryReadMarker(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ApplicationRunMarker>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void WriteMarker(string path, ApplicationRunMarker marker)
    {
        var temporary = path + $".pending-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(marker, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string SafeProcessName(string? processName)
    {
        var safe = new string((processName ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            .Take(80)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }

    private static bool IsSafeReference(string? reference) =>
        reference?.Length is >= 6 and <= 40 &&
        reference.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static string NewCrashReference() =>
        $"CRASH{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}";

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
