using Sati.Data;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sati.Services;

public sealed record ImportedOutlookEvent(
    string SourceId,
    string Title,
    DateTime Start,
    DateTime End,
    bool IsAllDay,
    string? Location)
{
    public string TimeLabel => IsAllDay
        ? "All day"
        : $"{Start:t}–{End:t}";

    public string AccessibleLabel => string.IsNullOrWhiteSpace(Location)
        ? $"Outlook event, {Title}, {TimeLabel}"
        : $"Outlook event, {Title}, {TimeLabel}, {Location}";
}

public sealed record OutlookCalendarImportResult(
    int ImportedCount,
    int SkippedCount,
    DateTime? EarliestEventDate,
    DateTime? LatestEventDate);

public interface IOutlookCalendarService
{
    Task<IReadOnlyList<ImportedOutlookEvent>> GetByYearAsync(
        int userId,
        int year,
        CancellationToken cancellationToken = default);

    Task<OutlookCalendarImportResult> ImportAsync(
        int userId,
        string filePath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Maintains a user- and environment-scoped local mirror of an Outlook calendar export.
/// The .ics file is parsed on the workstation and never uploaded. The cached event copy is
/// protected with the current Windows user's DPAPI key because subjects and locations may
/// contain protected information. It is an integration overlay, not an authoritative Sati record.
/// </summary>
public sealed class OutlookCalendarService : IOutlookCalendarService
{
    // This is an operational processing bound, not a calendar-content limit. Imports are
    // streamed, and the parser separately bounds the data it retains from any one event.
    internal const long MaximumImportBytes = 512L * 1024 * 1024;
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("Sati.OutlookCalendarOverlay.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DataEnvironmentInfo _environment;
    private readonly string _cachePath;
    private readonly Func<byte[], byte[]> _protect;
    private readonly Func<byte[], byte[]> _unprotect;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OutlookCalendarService(DataEnvironmentInfo environment)
        : this(
            environment,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sati",
                "outlook-calendar-cache.dat"),
            bytes => ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser),
            bytes => ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser))
    {
    }

    internal OutlookCalendarService(
        DataEnvironmentInfo environment,
        string cachePath,
        Func<byte[], byte[]> protect,
        Func<byte[], byte[]> unprotect)
    {
        _environment = environment;
        _cachePath = cachePath;
        _protect = protect;
        _unprotect = unprotect;
    }

    public async Task<IReadOnlyList<ImportedOutlookEvent>> GetByYearAsync(
        int userId,
        int year,
        CancellationToken cancellationToken = default)
    {
        ValidateUser(userId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadAsync(cancellationToken);
            return document.Profiles.TryGetValue(ProfileKey(userId), out var profile)
                ? profile.Events
                    .Where(entry => entry.Start.Year == year || entry.End.Year == year)
                    .OrderBy(entry => entry.Start)
                    .ThenBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
                : [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OutlookCalendarImportResult> ImportAsync(
        int userId,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ValidateUser(userId);
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Choose an Outlook calendar file.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("The selected Outlook calendar file no longer exists.", filePath);

        OutlookIcsReadResult parsed;
        await using (var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (stream.Length > MaximumImportBytes)
            {
                throw new InvalidDataException(
                    "The Outlook calendar file is larger than the 512 MiB import safety limit.");
            }

            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 64 * 1024,
                leaveOpen: true);
            parsed = await OutlookIcsReader.ReadAsync(
                reader,
                DateTime.Today.Year - 1,
                DateTime.Today.Year + 5,
                cancellationToken);
        }
        if (parsed.Events.Count == 0)
            throw new InvalidDataException("No supported calendar events were found in the selected file.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadAsync(cancellationToken);
            document.Profiles[ProfileKey(userId)] = new CalendarProfile(parsed.Events);
            await WriteAsync(document, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return new OutlookCalendarImportResult(
            parsed.Events.Count,
            parsed.SkippedCount,
            parsed.Events.Min(entry => entry.Start.Date),
            parsed.Events.Max(entry => entry.Start.Date));
    }

    private async Task<CalendarDocument> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_cachePath))
            return new CalendarDocument();

        var protectedBytes = await File.ReadAllBytesAsync(_cachePath, cancellationToken);
        var json = _unprotect(protectedBytes);
        var document = JsonSerializer.Deserialize<CalendarDocument>(json, JsonOptions)
            ?? new CalendarDocument();
        document.Profiles ??= new(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in document.Profiles.Values)
            profile.Events ??= [];
        return document;
    }

    private async Task WriteAsync(CalendarDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_cachePath)
            ?? throw new IOException("The Outlook calendar cache folder is unavailable.");
        Directory.CreateDirectory(directory);
        var pending = _cachePath + $".pending-{Guid.NewGuid():N}";
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            await File.WriteAllBytesAsync(pending, _protect(json), cancellationToken);
            File.Move(pending, _cachePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(pending))
                File.Delete(pending);
        }
    }

    private string ProfileKey(int userId) => $"{_environment.Environment}:{userId}";
    private static void ValidateUser(int userId)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));
    }

    private sealed class CalendarDocument
    {
        public Dictionary<string, CalendarProfile> Profiles { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CalendarProfile
    {
        public CalendarProfile(List<ImportedOutlookEvent> events) => Events = events;
        public List<ImportedOutlookEvent> Events { get; set; }
    }
}

internal sealed record OutlookIcsReadResult(
    List<ImportedOutlookEvent> Events,
    int SkippedCount);

internal static class OutlookIcsReader
{
    private const int MaximumEvents = 50_000;
    internal const int MaximumRetainedLogicalLineCharacters = 256 * 1024;
    private const int MaximumRetainedPropertiesPerEvent = 4_096;
    private const int MaximumRetainedPropertyCharactersPerEvent = 1024 * 1024;
    private const long MaximumRetainedEventTextCharacters = 64L * 1024 * 1024;
    private const long MaximumInputCharacters = OutlookCalendarService.MaximumImportBytes;
    private static readonly HashSet<string> RetainedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BEGIN",
        "END",
        "DTSTART",
        "DTEND",
        "UID",
        "SUMMARY",
        "LOCATION",
        "STATUS",
        "EXDATE",
        "RRULE"
    };

    public static OutlookIcsReadResult Read(string text, int firstYear, int lastYear)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var reader = new StringReader(text);
        return ReadAsync(reader, firstYear, lastYear, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    internal static async Task<OutlookIcsReadResult> ReadAsync(
        TextReader reader,
        int firstYear,
        int lastYear,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (firstYear > lastYear)
            throw new ArgumentOutOfRangeException(nameof(firstYear));

        var lineReader = new BoundedPhysicalLineReader(reader, MaximumInputCharacters);
        var events = new List<ImportedOutlookEvent>();
        var skipped = 0;
        var sawCalendar = false;
        var retainedEventTextCharacters = 0L;
        List<IcsProperty>? properties = null;
        var retainedPropertyCharacters = 0;
        PendingLogicalLine? pending = null;

        void FinishEvent()
        {
            if (properties is null)
                return;

            if (!TryCreateEvents(properties, firstYear, lastYear, out var imported))
            {
                skipped++;
                properties = null;
                retainedPropertyCharacters = 0;
                return;
            }

            if (imported.Count > MaximumEvents - events.Count)
            {
                throw new InvalidDataException(
                    "The calendar contains more than 50,000 events in the import window.");
            }

            foreach (var entry in imported)
            {
                retainedEventTextCharacters += entry.SourceId.Length + entry.Title.Length + (entry.Location?.Length ?? 0);
                if (retainedEventTextCharacters > MaximumRetainedEventTextCharacters)
                {
                    throw new InvalidDataException(
                        "The calendar contains too much event text to import safely.");
                }
            }

            events.AddRange(imported);
            properties = null;
            retainedPropertyCharacters = 0;
        }

        void ProcessLogicalLine(string line)
        {
            if (line.Equals("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            {
                sawCalendar = true;
                return;
            }

            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                properties = [];
                retainedPropertyCharacters = 0;
                return;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                FinishEvent();
                return;
            }

            if (properties is null)
                return;

            var separator = line.IndexOf(':');
            if (separator <= 0)
                return;

            var declaration = line[..separator];
            var semicolon = declaration.IndexOf(';');
            var name = (semicolon < 0 ? declaration : declaration[..semicolon]).ToUpperInvariant();
            if (!RetainedPropertyNames.Contains(name))
                return;

            retainedPropertyCharacters = checked(retainedPropertyCharacters + line.Length);
            if (properties.Count >= MaximumRetainedPropertiesPerEvent ||
                retainedPropertyCharacters > MaximumRetainedPropertyCharactersPerEvent)
            {
                throw new InvalidDataException(
                    "A calendar event contains too much supported property data to import safely.");
            }

            properties.Add(new IcsProperty(name, declaration, line[(separator + 1)..]));
        }

        void FinishPendingLine()
        {
            if (pending?.Value is not null)
                ProcessLogicalLine(pending.Value.ToString());
            pending = null;
        }

        while (await lineReader.ReadLineAsync(cancellationToken) is { } physicalLine)
        {
            var isContinuation = physicalLine.Value.Length > 0 &&
                physicalLine.Value[0] is ' ' or '\t';
            if (isContinuation)
            {
                if (pending?.Value is null)
                    continue;

                if (physicalLine.WasTruncated)
                    throw RetainedLineTooLong();

                var continuationLength = physicalLine.Value.Length - 1;
                if (pending.Value.Length + continuationLength > MaximumRetainedLogicalLineCharacters)
                    throw RetainedLineTooLong();

                pending.Value.Append(physicalLine.Value, 1, continuationLength);
                continue;
            }

            FinishPendingLine();
            if (!ShouldRetainLine(physicalLine.Value))
            {
                pending = new PendingLogicalLine(null);
                continue;
            }

            if (physicalLine.WasTruncated)
                throw RetainedLineTooLong();

            pending = new PendingLogicalLine(new StringBuilder(physicalLine.Value));
        }

        FinishPendingLine();
        if (!sawCalendar)
            throw new InvalidDataException("The selected file is not an iCalendar file.");

        var distinct = events
            .GroupBy(entry => $"{entry.SourceId}|{entry.Start:O}", StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderBy(entry => entry.Start)
            .ToList();
        skipped += events.Count - distinct.Count;
        return new OutlookIcsReadResult(distinct, skipped);
    }

    private static bool ShouldRetainLine(string line)
    {
        var separator = line.IndexOf(':');
        if (separator <= 0)
            return false;

        var declaration = line.AsSpan(0, separator);
        var semicolon = declaration.IndexOf(';');
        var name = semicolon < 0 ? declaration : declaration[..semicolon];
        foreach (var retainedName in RetainedPropertyNames)
        {
            if (name.Equals(retainedName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static InvalidDataException RetainedLineTooLong() => new(
        "A supported calendar property is longer than the 256 KiB import safety limit.");

    private static bool TryCreateEvents(
        List<IcsProperty> properties,
        int firstYear,
        int lastYear,
        out List<ImportedOutlookEvent> events)
    {
        events = [];
        string? Value(string name) => properties.LastOrDefault(item => item.Name == name)?.Value;
        if (string.Equals(Value("STATUS"), "CANCELLED", StringComparison.OrdinalIgnoreCase))
            return false;

        var startProperty = properties.LastOrDefault(item => item.Name == "DTSTART");
        if (startProperty is null || !TryReadDate(startProperty, out var start, out var allDay))
            return false;

        var endProperty = properties.LastOrDefault(item => item.Name == "DTEND");
        var end = endProperty is not null && TryReadDate(endProperty, out var parsedEnd, out _)
            ? parsedEnd
            : start.Add(allDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1));
        if (end <= start)
            end = start.Add(allDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1));

        var uid = Value("UID")?.Trim();
        if (string.IsNullOrWhiteSpace(uid))
            uid = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{start:O}|{Value("SUMMARY")}")))[..20];
        var title = DecodeText(Value("SUMMARY"));
        if (string.IsNullOrWhiteSpace(title))
            title = "Busy";
        var location = DecodeText(Value("LOCATION"));
        var duration = end - start;
        var exclusions = properties
            .Where(item => item.Name == "EXDATE")
            .SelectMany(item => item.Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => TryReadDate(item with { Value = value }, out var date, out _) ? date : (DateTime?)null))
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .ToHashSet();

        var rule = Value("RRULE");
        var starts = string.IsNullOrWhiteSpace(rule)
            ? [start]
            : ExpandRecurrence(start, rule, firstYear, lastYear);
        foreach (var occurrence in starts.Where(date => !exclusions.Contains(date)))
        {
            events.Add(new ImportedOutlookEvent(
                uid,
                title,
                occurrence,
                occurrence + duration,
                allDay,
                string.IsNullOrWhiteSpace(location) ? null : location));
        }

        return events.Count > 0;
    }

    private static List<DateTime> ExpandRecurrence(DateTime start, string rule, int firstYear, int lastYear)
    {
        var parts = rule.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .ToDictionary(part => part[0].ToUpperInvariant(), part => part[1], StringComparer.OrdinalIgnoreCase);
        if (!parts.TryGetValue("FREQ", out var frequency))
            return [start];

        var interval = parts.TryGetValue("INTERVAL", out var intervalText) && int.TryParse(intervalText, out var parsedInterval)
            ? Math.Clamp(parsedInterval, 1, 365)
            : 1;
        var count = parts.TryGetValue("COUNT", out var countText) && int.TryParse(countText, out var parsedCount)
            ? Math.Clamp(parsedCount, 1, MaximumEvents)
            : MaximumEvents;
        var until = parts.TryGetValue("UNTIL", out var untilText) &&
                    TryReadDate(new IcsProperty("UNTIL", "UNTIL", untilText), out var parsedUntil, out _)
            ? parsedUntil
            : new DateTime(lastYear, 12, 31, 23, 59, 59);
        var windowStart = new DateTime(firstYear, 1, 1);
        var windowEnd = new DateTime(lastYear, 12, 31, 23, 59, 59);
        var result = new List<DateTime>();

        void AddIfVisible(DateTime candidate, int ordinal)
        {
            if (ordinal <= count && candidate >= windowStart && candidate <= windowEnd && candidate <= until)
                result.Add(candidate);
        }

        if (frequency.Equals("WEEKLY", StringComparison.OrdinalIgnoreCase) && parts.TryGetValue("BYDAY", out var byDay))
        {
            var days = byDay.Split(',').Select(ParseDay).Where(day => day.HasValue).Select(day => day!.Value).Distinct().OrderBy(day => day == DayOfWeek.Sunday ? 7 : (int)day).ToList();
            if (days.Count == 0) days.Add(start.DayOfWeek);
            var week = start.Date.AddDays(-(((int)start.DayOfWeek + 6) % 7));
            var ordinal = 0;
            for (var cycle = 0; cycle < MaximumEvents && ordinal < count; cycle++, week = week.AddDays(7 * interval))
            {
                foreach (var day in days)
                {
                    var candidate = week.AddDays(((int)day + 6) % 7).Add(start.TimeOfDay);
                    if (candidate < start) continue;
                    ordinal++;
                    if (candidate > until || candidate > windowEnd) return result;
                    AddIfVisible(candidate, ordinal);
                }
            }
            return result;
        }

        var current = start;
        for (var ordinal = 1; ordinal <= count && current <= until && current <= windowEnd; ordinal++)
        {
            AddIfVisible(current, ordinal);
            current = frequency.ToUpperInvariant() switch
            {
                "DAILY" => current.AddDays(interval),
                "WEEKLY" => current.AddDays(7 * interval),
                "MONTHLY" => current.AddMonths(interval),
                "YEARLY" => current.AddYears(interval),
                _ => windowEnd.AddTicks(1)
            };
        }
        return result;
    }

    private static DayOfWeek? ParseDay(string value)
    {
        var suffix = value.Length >= 2 ? value[^2..].ToUpperInvariant() : value.ToUpperInvariant();
        return suffix switch { "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday, "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday, _ => null };
    }

    private static bool TryReadDate(IcsProperty property, out DateTime result, out bool allDay)
    {
        allDay = property.Declaration.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) || property.Value.Length == 8;
        string[] formats = allDay
            ? ["yyyyMMdd"]
            : ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm"];
        var value = property.Value.Trim();
        var utc = value.EndsWith('Z');
        if (utc) value = value[..^1];
        if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            result = default;
            return false;
        }

        if (allDay)
        {
            result = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Unspecified);
            return true;
        }
        if (utc)
        {
            result = DateTime.SpecifyKind(parsed, DateTimeKind.Utc).ToLocalTime();
            return true;
        }

        var timezoneId = ReadParameter(property.Declaration, "TZID");
        if (!string.IsNullOrWhiteSpace(timezoneId))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId.Trim('"'));
                result = TimeZoneInfo.ConvertTimeFromUtc(
                    TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), zone),
                    TimeZoneInfo.Local);
                return true;
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        result = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
        return true;
    }

    private static string? ReadParameter(string declaration, string name) =>
        declaration.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(part => part.Split('=', 2))
            .FirstOrDefault(part => part.Length == 2 && part[0].Equals(name, StringComparison.OrdinalIgnoreCase))?
            .ElementAtOrDefault(1);

    private static string DecodeText(string? value) => (value ?? string.Empty)
        .Replace("\\n", " ", StringComparison.OrdinalIgnoreCase)
        .Replace("\\,", ",", StringComparison.Ordinal)
        .Replace("\\;", ";", StringComparison.Ordinal)
        .Replace("\\\\", "\\", StringComparison.Ordinal)
        .Trim();

    private sealed record IcsProperty(string Name, string Declaration, string Value);

    private sealed record PendingLogicalLine(StringBuilder? Value);

    private readonly record struct PhysicalLine(string Value, bool WasTruncated);

    /// <summary>
    /// Reads physical lines without ever retaining more than one bounded prefix. Unsupported
    /// Outlook properties such as DESCRIPTION and ATTACH can therefore be arbitrarily verbose
    /// (within the total input ceiling) without becoming live managed strings.
    /// </summary>
    private sealed class BoundedPhysicalLineReader(TextReader reader, long maximumInputCharacters)
    {
        private readonly char[] _buffer = new char[16 * 1024];
        private int _bufferOffset;
        private int _bufferLength;
        private long _charactersConsumed;
        private bool _skipLineFeedAfterCarriageReturn;

        public async ValueTask<PhysicalLine?> ReadLineAsync(CancellationToken cancellationToken)
        {
            StringBuilder? retained = null;
            var sawCharacter = false;
            var wasTruncated = false;

            while (true)
            {
                var value = await ReadCharacterAsync(cancellationToken);
                if (value < 0)
                {
                    return sawCharacter
                        ? new PhysicalLine(retained?.ToString() ?? string.Empty, wasTruncated)
                        : null;
                }

                var character = (char)value;
                if (_skipLineFeedAfterCarriageReturn)
                {
                    _skipLineFeedAfterCarriageReturn = false;
                    if (character == '\n')
                        continue;
                }

                if (character == '\r')
                {
                    _skipLineFeedAfterCarriageReturn = true;
                    return new PhysicalLine(retained?.ToString() ?? string.Empty, wasTruncated);
                }

                if (character == '\n')
                    return new PhysicalLine(retained?.ToString() ?? string.Empty, wasTruncated);

                sawCharacter = true;
                if (!wasTruncated)
                {
                    retained ??= new StringBuilder(Math.Min(256, MaximumRetainedLogicalLineCharacters));
                    if (retained.Length < MaximumRetainedLogicalLineCharacters)
                        retained.Append(character);
                    else
                        wasTruncated = true;
                }
            }
        }

        private async ValueTask<int> ReadCharacterAsync(CancellationToken cancellationToken)
        {
            if (_bufferOffset >= _bufferLength)
            {
                _bufferLength = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken);
                _bufferOffset = 0;
                if (_bufferLength == 0)
                    return -1;
            }

            _charactersConsumed++;
            if (_charactersConsumed > maximumInputCharacters)
            {
                throw new InvalidDataException(
                    "The Outlook calendar contains more than 512 Mi characters and cannot be imported safely.");
            }

            return _buffer[_bufferOffset++];
        }
    }
}
