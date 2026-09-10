using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace Sati.Services;

internal enum WindowsCrashLookupState
{
    Matched,
    NotFound,
    ApplicationLogUnavailable
}

internal sealed record WindowsCrashEventMatch(
    long RecordId,
    DateTime OccurredAtUtc,
    string Provider,
    string FaultingApplication,
    string? FaultingApplicationVersion,
    string FaultingModule,
    string? FaultingModuleVersion,
    string? ExceptionCode,
    string? FaultOffset,
    bool DotNetRuntimeEventObserved);

internal sealed record WindowsCrashLookupResult(
    WindowsCrashLookupState State,
    WindowsCrashEventMatch? Match = null);

internal interface IWindowsCrashEventReader
{
    Task<WindowsCrashLookupResult> FindAsync(
        int processId,
        string processName,
        DateTime lastHeartbeatUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads fatal-fault signatures after the failed process is already dead. This class never calls
/// EventRecord.FormatDescription and never returns Event 1026 EventData: that free text can contain
/// exception messages and therefore PHI. A 1026 record contributes only a boolean signal when it is
/// temporally paired with an Event 1000 already matched by Sati's persisted process name and PID.
/// </summary>
internal sealed class WindowsCrashEventReader : IWindowsCrashEventReader
{
    private static readonly TimeSpan BeforeHeartbeat = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan AfterHeartbeat = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RuntimePairWindow = TimeSpan.FromSeconds(30);

    public Task<WindowsCrashLookupResult> FindAsync(
        int processId,
        string processName,
        DateTime lastHeartbeatUtc,
        CancellationToken cancellationToken = default)
    {
        if (processId <= 0 || string.IsNullOrWhiteSpace(processName))
            return Task.FromResult(new WindowsCrashLookupResult(WindowsCrashLookupState.NotFound));

        try
        {
            var startUtc = AsUtc(lastHeartbeatUtc).Subtract(BeforeHeartbeat);
            var endUtc = AsUtc(lastHeartbeatUtc).Add(AfterHeartbeat);
            var query = new EventLogQuery(
                "Application",
                PathType.LogName,
                BuildXPath(startUtc, endUtc))
            {
                ReverseDirection = true
            };

            var applicationErrors = new List<WindowsApplicationErrorCandidate>();
            var runtimeEvents = new List<WindowsRuntimeEventCandidate>();
            using var reader = new EventLogReader(query);
            while (reader.ReadEvent() is { } record)
            {
                using (record)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (record.Id == 1000 &&
                        string.Equals(record.ProviderName, "Application Error", StringComparison.OrdinalIgnoreCase))
                    {
                        var candidate = ParseApplicationErrorXml(record.ToXml());
                        if (candidate is not null)
                        {
                            applicationErrors.Add(candidate with
                            {
                                RecordId = record.RecordId ?? 0,
                                OccurredAtUtc = AsUtc(record.TimeCreated ?? candidate.OccurredAtUtc)
                            });
                        }
                    }
                    else if (record.Id == 1026 &&
                             string.Equals(record.ProviderName, ".NET Runtime", StringComparison.OrdinalIgnoreCase) &&
                             record.TimeCreated is { } runtimeTime &&
                             record.ProcessId is { } runtimeProcessId)
                    {
                        // Do not read ToXml/Properties/FormatDescription for Event 1026. Only the
                        // provider-controlled System timestamp is retained in memory.
                        runtimeEvents.Add(new WindowsRuntimeEventCandidate(
                            runtimeProcessId,
                            AsUtc(runtimeTime)));
                    }
                }
            }

            var match = SelectMatch(
                applicationErrors,
                runtimeEvents,
                processId,
                processName,
                lastHeartbeatUtc);
            if (match is null)
                return Task.FromResult(new WindowsCrashLookupResult(WindowsCrashLookupState.NotFound));

            return Task.FromResult(new WindowsCrashLookupResult(WindowsCrashLookupState.Matched, match));
        }
        catch (Exception exception) when (exception is EventLogException or UnauthorizedAccessException or
                                           PlatformNotSupportedException or InvalidOperationException or XmlException)
        {
            return Task.FromResult(new WindowsCrashLookupResult(WindowsCrashLookupState.ApplicationLogUnavailable));
        }
    }

    internal static WindowsCrashEventMatch? SelectMatch(
        IEnumerable<WindowsApplicationErrorCandidate> applicationErrors,
        IEnumerable<WindowsRuntimeEventCandidate> runtimeEvents,
        int processId,
        string processName,
        DateTime lastHeartbeatUtc)
    {
        var match = applicationErrors
                .Where(candidate => candidate.ProcessId == processId &&
                    SameProcessName(candidate.FaultingApplication, processName))
                .OrderBy(candidate => Math.Abs((candidate.OccurredAtUtc - AsUtc(lastHeartbeatUtc)).TotalSeconds))
                .FirstOrDefault();
        return match is null
            ? null
            : new WindowsCrashEventMatch(
                match.RecordId,
                match.OccurredAtUtc,
                "Application Error",
                match.FaultingApplication,
                match.FaultingApplicationVersion,
                match.FaultingModule,
                match.FaultingModuleVersion,
                match.ExceptionCode,
                match.FaultOffset,
                runtimeEvents.Any(runtime => runtime.ProcessId == processId &&
                    (AsUtc(runtime.OccurredAtUtc) - match.OccurredAtUtc).Duration() <= RuntimePairWindow));
    }

    internal static string BuildXPath(DateTime startUtc, DateTime endUtc)
    {
        var start = XmlConvert.ToString(AsUtc(startUtc), XmlDateTimeSerializationMode.Utc);
        var end = XmlConvert.ToString(AsUtc(endUtc), XmlDateTimeSerializationMode.Utc);
        return $"*[System[(((Provider[@Name='Application Error']) and (EventID=1000)) or " +
               $"((Provider[@Name='.NET Runtime']) and (EventID=1026))) and " +
               $"TimeCreated[@SystemTime>='{start}' and @SystemTime<='{end}']]]";
    }

    internal static WindowsApplicationErrorCandidate? ParseApplicationErrorXml(string xml)
    {
        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var document = XDocument.Load(xmlReader, LoadOptions.None);
        var root = document.Root;
        if (root is null)
            return null;
        var ns = root.Name.Namespace;
        var eventId = root.Descendants(ns + "EventID").FirstOrDefault()?.Value;
        var provider = root.Descendants(ns + "Provider").FirstOrDefault()?.Attribute("Name")?.Value;
        if (eventId != "1000" || !string.Equals(provider, "Application Error", StringComparison.OrdinalIgnoreCase))
            return null;

        var data = root.Descendants(ns + "EventData")
            .Elements(ns + "Data")
            .Where(element => element.Attribute("Name") is not null)
            .GroupBy(element => element.Attribute("Name")!.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
        if (!TryGetProcessId(data, out var processId) ||
            !TrySafeFileName(data.GetValueOrDefault("AppName"), out var application) ||
            !TrySafeFileName(data.GetValueOrDefault("ModuleName"), out var module))
            return null;

        var occurredAt = DateTime.TryParse(
            root.Descendants(ns + "TimeCreated").FirstOrDefault()?.Attribute("SystemTime")?.Value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsedTime)
            ? parsedTime
            : DateTime.UnixEpoch;

        return new WindowsApplicationErrorCandidate(
            0,
            occurredAt,
            processId,
            application,
            SafeVersion(data.GetValueOrDefault("AppVersion")),
            module,
            SafeVersion(data.GetValueOrDefault("ModuleVersion")),
            SafeHex(data.GetValueOrDefault("ExceptionCode")),
            SafeHex(data.GetValueOrDefault("FaultingOffset")));
    }

    private static bool TryGetProcessId(IReadOnlyDictionary<string, string> data, out int processId)
    {
        var text = data.GetValueOrDefault("ProcessId")?.Trim();
        if (text?.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true)
            return int.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out processId) && processId > 0;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out processId) && processId > 0;
    }

    private static bool SameProcessName(string application, string processName) =>
        string.Equals(
            NormalizeExecutableName(application),
            NormalizeExecutableName(processName),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeExecutableName(string value)
    {
        var name = Path.GetFileName(value);
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }

    private static bool TrySafeFileName(string? value, out string safe)
    {
        safe = Path.GetFileName(value?.Trim() ?? string.Empty) ?? string.Empty;
        return safe.Length is >= 1 and <= 120 && safe.All(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or '+' or '(' or ')' or ' ');
    }

    private static string? SafeVersion(string? value)
    {
        value = value?.Trim();
        return value?.Length is >= 1 and <= 40 && value.All(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            ? value
            : null;
    }

    private static string? SafeHex(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = value[2..];
        return value.Length is >= 1 and <= 16 && value.All(Uri.IsHexDigit)
            ? $"0x{value.ToUpperInvariant()}"
            : null;
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

internal sealed record WindowsApplicationErrorCandidate(
    long RecordId,
    DateTime OccurredAtUtc,
    int ProcessId,
    string FaultingApplication,
    string? FaultingApplicationVersion,
    string FaultingModule,
    string? FaultingModuleVersion,
    string? ExceptionCode,
    string? FaultOffset);

internal sealed record WindowsRuntimeEventCandidate(int ProcessId, DateTime OccurredAtUtc);
