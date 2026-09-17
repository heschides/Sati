namespace Sati.Contracts.V1;

/// <summary>
/// Narrative-free facts needed to explain a case manager's monthly productivity forecast.
/// Minutes, rather than caller-calculated units, are accepted so the shared rule owns rounding.
/// </summary>
public sealed record ProductivityNoteFact(DateTime? EventDate, string? Status, int? Minutes);

/// <summary>
/// Separates units already secured by a completed note from unfinished units that can still be
/// recovered, and separates both from future work capacity. This is a forecast, not a billing
/// decision: the persisted note workflow remains authoritative for whether a note is billable.
/// </summary>
public sealed record ProductivityForecastResult(
    decimal SecuredUnits,
    decimal RecoverableUnits,
    decimal DueTodayUnits,
    decimal ExpiredPendingUnits,
    int PendingItemsWithoutUnits,
    int FutureEligibleDays,
    int EligibleDaysAfterToday,
    decimal? SecuredPace,
    decimal? ProjectedPace,
    decimal? PaceIfDueTodayExpires);

/// <summary>How one calendar day stands in the current month's documented daily average.</summary>
public enum ProductivityDayKind
{
    /// <summary>Outside the current month, after today, or without a pending, logged, or approved note.</summary>
    NotCounted,

    /// <summary>In the average, with at least one logged or approved note.</summary>
    CountedWithSecuredUnits,

    /// <summary>In the average only through pending notes; nothing on it is secured yet.</summary>
    CountedWithoutSecuredUnits
}

public static class ProductivityForecast
{
    public const int DefaultDocumentationWindowDays = 7;

    private const string Pending = "Pending";
    private static readonly string[] SecuredStatuses = ["Logged", "Approved"];
    private static readonly string[] AverageStatuses = ["Pending", "Logged", "Approved"];

    /// <summary>
    /// The service days the documented daily average divides by: days of <paramref name="today"/>'s
    /// month, up to and including today, that carry a pending, logged, or approved note. A future
    /// day is never in the average, even when a pending note is already scheduled on it.
    /// </summary>
    public static IReadOnlySet<DateTime> DailyAverageDays(
        IEnumerable<ProductivityNoteFact> notes,
        DateTime today)
    {
        ArgumentNullException.ThrowIfNull(notes);
        today = today.Date;
        return notes
            .Where(note => note.EventDate is DateTime date &&
                           date.Year == today.Year && date.Month == today.Month &&
                           date.Date <= today &&
                           AverageStatuses.Contains(note.Status, StringComparer.OrdinalIgnoreCase))
            .Select(note => note.EventDate!.Value.Date)
            .ToHashSet();
    }

    /// <summary>
    /// Secured plus recoverable units per day in <see cref="DailyAverageDays"/>, to one decimal.
    /// Zero when no day qualifies yet.
    /// </summary>
    public static decimal DailyAverageUnits(
        ProductivityForecastResult forecast,
        IEnumerable<ProductivityNoteFact> notes,
        DateTime today)
    {
        ArgumentNullException.ThrowIfNull(forecast);
        var days = DailyAverageDays(notes, today).Count;
        return days == 0
            ? 0
            : Math.Round((forecast.SecuredUnits + forecast.RecoverableUnits) / days, 1);
    }

    /// <summary>Where <paramref name="date"/> stands in the average, given the notes dated on it.</summary>
    public static ProductivityDayKind ClassifyDay(
        DateTime date,
        IEnumerable<ProductivityNoteFact> notesOnDate,
        DateTime today)
    {
        ArgumentNullException.ThrowIfNull(notesOnDate);
        var onDate = notesOnDate
            .Where(note => note.EventDate?.Date == date.Date)
            .ToList();
        if (!DailyAverageDays(onDate, today).Contains(date.Date))
            return ProductivityDayKind.NotCounted;

        return onDate.Any(note => SecuredStatuses.Contains(note.Status, StringComparer.OrdinalIgnoreCase))
            ? ProductivityDayKind.CountedWithSecuredUnits
            : ProductivityDayKind.CountedWithoutSecuredUnits;
    }

    /// <summary>
    /// Treats a missing or non-positive legacy setting as the seven-calendar-day policy.
    /// New settings writes should still reject those values; this is a read-side compatibility
    /// guard for older servers and existing rows.
    /// </summary>
    public static int NormalizeDocumentationWindowDays(int? configuredDays) =>
        configuredDays is > 0 ? configuredDays.Value : DefaultDocumentationWindowDays;

    public static ProductivityForecastResult Calculate(
        decimal monthlyTarget,
        int futureEligibleDays,
        int eligibleDaysAfterToday,
        DateTime today,
        int documentationWindowDays,
        IEnumerable<ProductivityNoteFact> notes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(monthlyTarget);
        ArgumentOutOfRangeException.ThrowIfNegative(futureEligibleDays);
        ArgumentOutOfRangeException.ThrowIfNegative(eligibleDaysAfterToday);
        ArgumentNullException.ThrowIfNull(notes);

        documentationWindowDays = NormalizeDocumentationWindowDays(documentationWindowDays);

        today = today.Date;
        decimal secured = 0;
        decimal recoverable = 0;
        decimal dueToday = 0;
        decimal expired = 0;
        var pendingWithoutUnits = 0;

        foreach (var note in notes)
        {
            var units = CalculateUnits(note.Minutes);
            if (SecuredStatuses.Contains(note.Status, StringComparer.OrdinalIgnoreCase))
            {
                secured += units;
                continue;
            }

            if (!string.Equals(note.Status, Pending, StringComparison.OrdinalIgnoreCase) ||
                note.EventDate is not DateTime eventDate || eventDate.Date > today)
                continue;

            if (note.Minutes is null)
            {
                pendingWithoutUnits++;
                continue;
            }

            var lastDocumentableDay = eventDate.Date.AddDays(documentationWindowDays);
            if (lastDocumentableDay < today)
            {
                expired += units;
                continue;
            }

            recoverable += units;
            if (lastDocumentableDay == today)
                dueToday += units;
        }

        return new ProductivityForecastResult(
            secured,
            recoverable,
            dueToday,
            expired,
            pendingWithoutUnits,
            futureEligibleDays,
            eligibleDaysAfterToday,
            Pace(monthlyTarget - secured, futureEligibleDays),
            Pace(monthlyTarget - secured - recoverable, futureEligibleDays),
            Pace(monthlyTarget - secured - (recoverable - dueToday), eligibleDaysAfterToday));
    }

    private static decimal CalculateUnits(int? minutes) => minutes.HasValue
        ? Math.Max(1, (int)Math.Ceiling(minutes.Value / 15m))
        : 0;

    private static decimal? Pace(decimal unitsNeeded, int futureEligibleDays)
    {
        if (unitsNeeded <= 0)
            return 0;
        if (futureEligibleDays == 0)
            return null;
        return Math.Round(unitsNeeded / futureEligibleDays, 1);
    }
}
