using System.IO;
using System.Text.Json;
using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

// Origin is local routing metadata. The API always derives agency from its actor.
internal sealed record StoredIncidentEnvelope(
    int SchemaVersion,
    int OriginUserId,
    int OriginAgencyId,
    DateTime EnqueuedAtUtc,
    IncidentReportRequest Report);

internal sealed record PendingIncidentEnvelope(string Path, StoredIncidentEnvelope Stored)
{
    public IncidentReportRequest Report => Stored.Report;
    public int OriginUserId => Stored.OriginUserId;
    public int OriginAgencyId => Stored.OriginAgencyId;
}

internal sealed record IncidentOutboxStatus(
    int PendingForCurrentAccount,
    TimeSpan? OldestPendingAge,
    int HeldForOtherAccounts,
    int Quarantined);

internal sealed class IncidentOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal static readonly TimeSpan MaximumPendingAge = TimeSpan.FromDays(30);
    private const int CurrentSchemaVersion = 1;
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

    public void Enqueue(IncidentReportRequest report, int originUserId, int originAgencyId,
        DateTime? enqueuedAtUtc = null)
    {
        // The platform operator is a valid authenticated identity with agency 0.
        if (originUserId <= 0 || originAgencyId < 0)
            throw new ArgumentOutOfRangeException(nameof(originUserId),
                "An authenticated user and agency are required to queue an incident.");
        ArgumentNullException.ThrowIfNull(report);

        var queuedAt = (enqueuedAtUtc ?? DateTime.UtcNow).ToUniversalTime();
        var stored = new StoredIncidentEnvelope(CurrentSchemaVersion, originUserId,
            originAgencyId, queuedAt, report);
        lock (_sync)
        {
            Directory.CreateDirectory(_pendingDirectory);
            var safeReference = new string(report.Reference
                .Where(char.IsLetterOrDigit)
                .Take(40)
                .ToArray());
            if (string.IsNullOrWhiteSpace(safeReference))
                safeReference = Guid.NewGuid().ToString("N");

            // The same reference from a different account must not displace this one.
            var existing = Directory.EnumerateFiles(_pendingDirectory,
                    $"*-u{originUserId}-a{originAgencyId}-{safeReference}.json")
                .FirstOrDefault();
            if (existing is not null)
            {
                var queued = TryRead(existing);
                if (queued is null || !ShouldReplace(queued.Report, report))
                    return;
                WriteAtomically(existing, stored with { EnqueuedAtUtc = queued.EnqueuedAtUtc },
                    overwrite: true);
                return;
            }

            var name = $"{queuedAt.Ticks:D19}-u{originUserId}-a{originAgencyId}-{safeReference}.json";
            WriteAtomically(Path.Combine(_pendingDirectory, name), stored, overwrite: false);
        }
    }

    // Legacy flat requests have no origin. Quarantine rather than attributing them to
    // whichever account happens to sign in next.
    public IReadOnlyList<PendingIncidentEnvelope> ReadPending(DateTime? utcNow = null)
    {
        lock (_sync)
            return ReadPendingUnderLock(utcNow ?? DateTime.UtcNow);
    }

    public IReadOnlyList<PendingIncidentEnvelope> ReadPendingFor(
        int userId, int agencyId, DateTime? utcNow = null)
    {
        if (userId <= 0 || agencyId < 0)
            return [];
        lock (_sync)
            return ReadPendingUnderLock(utcNow ?? DateTime.UtcNow)
                .Where(envelope => envelope.OriginUserId == userId &&
                                   envelope.OriginAgencyId == agencyId)
                .ToList();
    }

    public IncidentOutboxStatus StatusFor(int userId, int agencyId, DateTime? utcNow = null)
    {
        var now = (utcNow ?? DateTime.UtcNow).ToUniversalTime();
        lock (_sync)
        {
            var pending = ReadPendingUnderLock(now);
            var own = pending.Where(envelope => envelope.OriginUserId == userId &&
                                                envelope.OriginAgencyId == agencyId).ToList();
            var oldest = own.Count == 0
                ? (TimeSpan?)null
                : TimeSpan.FromTicks(Math.Max(0, now.Ticks - own.Min(item => item.Stored.EnqueuedAtUtc.Ticks)));
            var quarantined = Directory.Exists(_rejectedDirectory)
                ? Directory.EnumerateFiles(_rejectedDirectory, "*.json").Count()
                : 0;
            return new IncidentOutboxStatus(own.Count, oldest, pending.Count - own.Count,
                quarantined);
        }
    }

    public void Complete(PendingIncidentEnvelope envelope)
    {
        lock (_sync)
        {
            if (File.Exists(envelope.Path) && TryRead(envelope.Path) == envelope.Stored)
                File.Delete(envelope.Path);
        }
    }

    public void Reject(PendingIncidentEnvelope envelope)
    {
        lock (_sync)
        {
            if (File.Exists(envelope.Path) && TryRead(envelope.Path) == envelope.Stored)
                Quarantine(envelope.Path, "rejected");
        }
    }

    private List<PendingIncidentEnvelope> ReadPendingUnderLock(DateTime utcNow)
    {
        if (!Directory.Exists(_pendingDirectory))
            return [];

        var now = utcNow.ToUniversalTime();
        var pending = new List<PendingIncidentEnvelope>();
        foreach (var path in Directory.EnumerateFiles(_pendingDirectory, "*.json")
                     .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase))
        {
            var stored = TryRead(path);
            if (stored is null)
            {
                Quarantine(path, "unscoped-or-invalid");
                continue;
            }
            if (stored.EnqueuedAtUtc > now.AddMinutes(5) ||
                now - stored.EnqueuedAtUtc > MaximumPendingAge)
            {
                Quarantine(path, "expired");
                continue;
            }
            pending.Add(new PendingIncidentEnvelope(path, stored));
        }
        return pending;
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

    private static StoredIncidentEnvelope? TryRead(string path)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<StoredIncidentEnvelope>(File.ReadAllText(path),
                JsonOptions);
            return stored is { SchemaVersion: CurrentSchemaVersion,
                OriginUserId: > 0, OriginAgencyId: >= 0, Report: not null } &&
                   stored.EnqueuedAtUtc != default &&
                   stored.EnqueuedAtUtc.Kind == DateTimeKind.Utc
                ? stored
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void WriteAtomically(string destination, StoredIncidentEnvelope stored,
        bool overwrite)
    {
        var temporary = destination + $".pending-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(stored, JsonOptions));
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private void Quarantine(string path, string reason)
    {
        try
        {
            Directory.CreateDirectory(_rejectedDirectory);
            var destination = Path.Combine(
                _rejectedDirectory,
                $"{Path.GetFileNameWithoutExtension(path)}-{reason}-{Guid.NewGuid():N}.json");
            File.Move(path, destination);
        }
        catch
        {
            // Keep the envelope for support review if moving it is unavailable.
        }
    }
}
