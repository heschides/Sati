using System.Text.Json;

namespace Sati.Contracts.V1;

public static class IncidentSeverities
{
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Critical = "Critical";

    public static bool IsValid(string? value) =>
        value is Warning or Error or Critical;
}

public static class IncidentScopes
{
    public const string Agency = "Agency";
    public const string Platform = "Platform";
}

/// <summary>
/// Closed states for Windows crash readback. The wording deliberately distinguishes an observed
/// unclean exit from the availability of a matching Windows record.
/// </summary>
public static class CrashDiagnosticStatuses
{
    public const string Matched = "Matched";
    public const string PendingOrUnavailable = "PendingOrUnavailable";
    public const string ApplicationLogUnavailable = "ApplicationLogUnavailable";
    public const string CorrelationUnavailable = "CorrelationUnavailable";

    public static bool IsValid(string? value) => value is
        Matched or PendingOrUnavailable or ApplicationLogUnavailable or CorrelationUnavailable;
}

/// <summary>
/// PHI-minimized Windows Application-log metadata captured after Sati restarts. Event 1026's
/// free-text description is intentionally absent from this contract.
/// </summary>
public sealed record CrashDiagnosticDto(
    string Status,
    int ProcessId,
    string ProcessName,
    DateTime LastHeartbeatUtc,
    int? WindowsEventId = null,
    long? WindowsEventRecordId = null,
    DateTime? WindowsEventTimeUtc = null,
    string? WindowsEventProvider = null,
    string? FaultingApplication = null,
    string? FaultingApplicationVersion = null,
    string? FaultingModule = null,
    string? FaultingModuleVersion = null,
    string? ExceptionCode = null,
    string? FaultOffset = null,
    bool DotNetRuntimeEventObserved = false);

/// <summary>
/// One validation and serialization owner for the curated crash envelope used by desktop, API,
/// and Admin views. It accepts only bounded structured metadata; paths and event prose have no
/// field through which they can enter the incident table.
/// </summary>
public static class CrashDiagnosticRules
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const int MaximumSerializedLength = 2_000;

    public static bool IsValid(CrashDiagnosticDto? value)
    {
        if (value is null || !CrashDiagnosticStatuses.IsValid(value.Status) ||
            value.ProcessId < 0 || !SafeToken(value.ProcessName, 1, 80) ||
            value.LastHeartbeatUtc == default)
            return false;

        if (value.Status == CrashDiagnosticStatuses.CorrelationUnavailable)
        {
            if (value.ProcessId != 0)
                return false;
        }
        else if (value.ProcessId == 0)
        {
            return false;
        }

        if (value.Status == CrashDiagnosticStatuses.Matched)
        {
            if (value.WindowsEventId != 1000 || value.WindowsEventRecordId is null or < 0 ||
                value.WindowsEventTimeUtc is null || value.WindowsEventProvider != "Application Error" ||
                !SafeFileName(value.FaultingApplication, 1, 120) ||
                !SafeFileName(value.FaultingModule, 1, 120) ||
                !SafeVersion(value.FaultingApplicationVersion) ||
                !SafeVersion(value.FaultingModuleVersion) ||
                !SafeHex(value.ExceptionCode) || !SafeHex(value.FaultOffset))
                return false;
        }
        else if (value.WindowsEventId is not null || value.WindowsEventRecordId is not null ||
                 value.WindowsEventTimeUtc is not null || value.WindowsEventProvider is not null ||
                 value.FaultingApplication is not null || value.FaultingApplicationVersion is not null ||
                 value.FaultingModule is not null || value.FaultingModuleVersion is not null ||
                 value.ExceptionCode is not null || value.FaultOffset is not null ||
                 value.DotNetRuntimeEventObserved)
        {
            return false;
        }

        return SerializeUnchecked(value).Length <= MaximumSerializedLength;
    }

    public static string Serialize(CrashDiagnosticDto value)
    {
        if (!IsValid(value))
            throw new ArgumentException("The crash diagnostic contains unsupported metadata.", nameof(value));
        return SerializeUnchecked(value);
    }

    public static CrashDiagnosticDto? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumSerializedLength)
            return null;
        try
        {
            var value = JsonSerializer.Deserialize<CrashDiagnosticDto>(json, JsonOptions);
            return IsValid(value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static int Quality(string status) => status switch
    {
        CrashDiagnosticStatuses.Matched => 3,
        CrashDiagnosticStatuses.PendingOrUnavailable => 2,
        CrashDiagnosticStatuses.ApplicationLogUnavailable => 1,
        _ => 0
    };

    private static string SerializeUnchecked(CrashDiagnosticDto value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static bool SafeToken(string? value, int minimum, int maximum) =>
        value?.Length >= minimum && value.Length <= maximum &&
        value.Any(char.IsLetterOrDigit) &&
        value.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');

    private static bool SafeFileName(string? value, int minimum, int maximum) =>
        value?.Length >= minimum && value.Length <= maximum &&
        value.Any(char.IsLetterOrDigit) &&
        value.All(character => char.IsLetterOrDigit(character) ||
            character is '.' or '-' or '_' or '+' or '(' or ')' or ' ');

    private static bool SafeVersion(string? value) => value is null || SafeToken(value, 1, 40);

    private static bool SafeHex(string? value) => value is null ||
        value.Length is >= 3 and <= 18 && value.StartsWith("0x", StringComparison.Ordinal) &&
        value[2..].All(Uri.IsHexDigit);
}

public sealed record IncidentReportRequest(
    string Reference,
    string Source,
    string Severity,
    string Operation,
    string Release,
    string ExceptionFingerprint,
    DateTime OccurredAtUtc,
    CrashDiagnosticDto? CrashDiagnostic = null);

public sealed record UpdateIncidentStatusRequest(string Status);

public sealed record IncidentGroupDto(
    long Id,
    int AgencyId,
    string Scope,
    string Source,
    string Severity,
    string Operation,
    string FirstRelease,
    string LastRelease,
    string ExceptionFingerprint,
    string Status,
    int OccurrenceCount,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    string LastReference,
    string LastActorRole,
    CrashDiagnosticDto? LastCrashDiagnostic = null);

public sealed record IncidentHealthScoreDto(
    int? Score,
    string Grade,
    int WindowDays,
    int TotalOccurrences,
    int UnresolvedGroups,
    int CriticalOccurrences,
    int ErrorOccurrences,
    int WarningOccurrences,
    int SeverityPenalty,
    int RecurrencePenalty,
    int UnresolvedAgePenalty,
    string FormulaVersion,
    string Explanation,
    string AlertLevel,
    string AlertReason,
    bool HasTelemetry);

public sealed record AdminIncidentDashboardDto(
    DateTime ObservedAtUtc,
    IncidentHealthScoreDto Health,
    IReadOnlyList<IncidentGroupDto> Incidents);

public sealed record PlatformAgencyHealthDto(
    int AgencyId,
    string AgencyName,
    IncidentHealthScoreDto Health);

public sealed record PlatformIncidentDashboardDto(
    DateTime ObservedAtUtc,
    IncidentHealthScoreDto OverallHealth,
    IReadOnlyList<PlatformAgencyHealthDto> Agencies,
    IReadOnlyList<IncidentGroupDto> Incidents);
