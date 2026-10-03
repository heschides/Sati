namespace Sati.Contracts.V1;

/// <summary>
/// A prior planned service date survives a Scheduled note's move, even when the
/// note becomes a draft or documented work on its new date. A calendar reminder
/// is not service work and contributes no rescheduled units.
/// </summary>
public static class NoteScheduleMoveRules
{
    public static bool ShouldRecord(
        string? previousStatus,
        DateTime? previousDate,
        DateTime? newDate,
        string? previousNoteType) =>
        string.Equals(previousStatus, NoteSchedulingPolicy.ScheduledStatus,
            StringComparison.OrdinalIgnoreCase) &&
        previousDate.HasValue && newDate.HasValue &&
        previousDate.Value.Date != newDate.Value.Date &&
        !string.Equals(previousNoteType, NoteSchedulingPolicy.ReminderType,
            StringComparison.OrdinalIgnoreCase);

    public static int? FrozenUnits(int? previousMinutes) =>
        previousMinutes.HasValue
            ? (int)ProductivityForecast.CalculateUnits(previousMinutes)
            : null;
}

/// <summary>Minimum necessary calendar projection of one persisted date move.</summary>
public sealed record ScheduledNoteMoveDto(
    long Id,
    int NoteId,
    int PersonId,
    string ConsumerName,
    DateTime FromDate,
    DateTime ToDate,
    int? ScheduledMinutes,
    int? ScheduledUnits,
    DateTime MovedAtUtc);
