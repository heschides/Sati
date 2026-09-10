using System.IO;
using System.Text.Json;
using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

internal sealed record PendingIncidentEnvelope(string Path, IncidentReportRequest Report);

internal sealed class IncidentOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly string _pendingDirectory;
    private readonly string _rejectedDirectory;

    public IncidentOutbox() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SatiLogica", "Sati", "IncidentOutbox"))
    {
    }

    internal IncidentOutbox(string rootDirectory)
    {
        _pendingDirectory = Path.Combine(rootDirectory, "Pending");
        _rejectedDirectory = Path.Combine(rootDirectory, "Rejected");
    }

    public void Enqueue(IncidentReportRequest report)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(_pendingDirectory);
            var safeReference = new string(report.Reference
                .Where(char.IsLetterOrDigit)
                .Take(40)
                .ToArray());
            if (string.IsNullOrWhiteSpace(safeReference))
                safeReference = Guid.NewGuid().ToString("N");

            var existing = Directory.EnumerateFiles(_pendingDirectory, $"*-{safeReference}.json")
                .FirstOrDefault();
            if (existing is not null)
            {
                var queued = TryRead(existing);
                if (queued is null || !ShouldReplace(queued, report))
                    return;
                WriteAtomically(existing, report, overwrite: true);
                return;
            }

            var name = $"{report.OccurredAtUtc.Ticks:D19}-{safeReference}.json";
            WriteAtomically(Path.Combine(_pendingDirectory, name), report, overwrite: false);
        }
    }

    public IReadOnlyList<PendingIncidentEnvelope> ReadPending()
    {
        lock (_sync)
        {
            if (!Directory.Exists(_pendingDirectory))
                return [];

            var pending = new List<PendingIncidentEnvelope>();
            foreach (var path in Directory.EnumerateFiles(_pendingDirectory, "*.json")
                         .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase))
            {
                var report = TryRead(path);
                if (report is not null)
                    pending.Add(new PendingIncidentEnvelope(path, report));
                else
                    Quarantine(path);
            }

            return pending;
        }
    }

    public void Complete(PendingIncidentEnvelope envelope)
    {
        lock (_sync)
        {
            if (!File.Exists(envelope.Path))
                return;

            // If a pending/unavailable crash report was upgraded while its older envelope was in
            // flight, leave the replacement for the next flush instead of deleting unsent detail.
            if (TryRead(envelope.Path) == envelope.Report)
                File.Delete(envelope.Path);
        }
    }

    public void Reject(PendingIncidentEnvelope envelope)
    {
        lock (_sync)
        {
            if (File.Exists(envelope.Path) && TryRead(envelope.Path) == envelope.Report)
                Quarantine(envelope.Path);
        }
    }

    private static bool ShouldReplace(IncidentReportRequest queued, IncidentReportRequest reported)
    {
        if (queued.Reference != reported.Reference || reported.CrashDiagnostic is null)
            return false;
        if (queued.CrashDiagnostic is null)
            return true;
        return CrashDiagnosticRules.Quality(reported.CrashDiagnostic.Status) >
               CrashDiagnosticRules.Quality(queued.CrashDiagnostic.Status);
    }

    private static IncidentReportRequest? TryRead(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<IncidentReportRequest>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void WriteAtomically(string destination, IncidentReportRequest report, bool overwrite)
    {
        var temporary = destination + $".pending-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(report, JsonOptions));
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private void Quarantine(string path)
    {
        try
        {
            Directory.CreateDirectory(_rejectedDirectory);
            var destination = Path.Combine(
                _rejectedDirectory,
                $"{Path.GetFileNameWithoutExtension(path)}-invalid-{Guid.NewGuid():N}.json");
            File.Move(path, destination);
        }
        catch
        {
            // Keep an unreadable envelope in place if it cannot be quarantined.
        }
    }
}
