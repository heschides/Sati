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
    CountedWithoutSecuredUnits,

    /// <summary>
    /// Documented in part, but still open: work remains on its schedule, or the case manager
    /// has set it aside. Counting it now would divide a finished day's units by an unfinished
    /// day. Its own documentation window settles it either way.
    /// </summary>
    OpenUntilDocumented
}

public static class ProductivityForecast
{
    public const int DefaultDocumentationWindowDays = 7;

    private const string Pending = "Pending";
    private const string Scheduled = "Scheduled";
    private static readonly string[] SecuredStatuses = ["Logged", "Approved"];
    private static readonly string[] AverageStatuses = ["Pending", "Logged", "Approved"];

    /// <summary>
    /// Whether a day can be in the average at all: a day of <paramref name="today"/>'s month, up
    /// to and including today, carrying a pending, logged, or approved note. A future day is never
    /// in the average, even when a pending note is already scheduled on it.
    /// </summary>
    private static bool IsDocumentedServiceDay(
        DateTime date,
        IEnumerable<ProductivityNoteFact> notesOnDate,
        DateTime today) =>
        date.Date.Year == today.Date.Year && date.Date.Month == today.Date.Month &&
        date.Date <= today.Date &&
        notesOnDate.Any(note => note.EventDate?.Date == date.Date &&
                                AverageStatuses.Contains(note.Status, StringComparer.OrdinalIgnoreCase));

    /// <summary>A day is settled once its documentation window has closed; nothing can be added late.</summary>
    public static bool IsDocumentationWindowClosed(DateTime date, DateTime today, int documentationWindowDays) =>
        date.Date.AddDays(NormalizeDocumentationWindowDays(documentationWindowDays)) < today.Date;

    /// <summary>Work planned on this day that has not been documented yet.</summary>
    private static bool HasScheduledWork(
        DateTime date,
        IEnumerable<ProductivityNoteFact> notesOnDate) =>
        notesOnDate.Any(note => note.EventDate?.Date == date.Date &&
                                string.Equals(note.Status, Scheduled, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the average divides by <paramref name="date"/>.
    /// <para>
    /// A day still inside its documentation window is only counted once it looks finished — it has
    /// documented work and nothing left on its schedule — or the case manager says so with
    /// <paramref name="caseManagerChoice"/>. That keeps a day documented one note at a time from
    /// dragging the average down while the rest of it is still being written up.
    /// </para>
    /// <para>
    /// Once the window closes the day counts on its own, so a choice that was never made, or was
    /// made and forgotten, cannot hold a real service day out of the average forever.
    /// </para>
    /// </summary>
    public static bool CountsInDailyAverage(
        DateTime date,
        IEnumerable<ProductivityNoteFact> notesOnDate,
        DateTime today,
        int documentationWindowDays,
        bool? caseManagerChoice)
    {
        ArgumentNullException.ThrowIfNull(notesOnDate);
        if (!IsDocumentedServiceDay(date, notesOnDate, today))
            return false;
        if (IsDocumentationWindowClosed(date, today, documentationWindowDays))
            return true;
        return caseManagerChoice ?? !HasScheduledWork(date, notesOnDate);
    }

    /// <summary>Whether the case manager can still decide this day; a settled day is no longer theirs to hold.</summary>
    public static bool CanChooseDailyAverageDay(
        DateTime date,
        IEnumerable<ProductivityNoteFact> notesOnDate,
        DateTime today,
        int documentationWindowDays)
    {
        ArgumentNullException.ThrowIfNull(notesOnDate);
        return IsDocumentedServiceDay(date, notesOnDate, today) &&
               !IsDocumentationWindowClosed(date, today, documentationWindowDays);
    }

    /// <summary>The service days the average divides by.</summary>
    public static IReadOnlySet<DateTime> DailyAverageDays(
        IEnumerable<ProductivityNoteFact> notes,
        DateTime today,
        int documentationWindowDays,
        IReadOnlyDictionary<DateTime, bool>? caseManagerChoices = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var all = notes.ToList();
        return all
            .Where(note => note.EventDate is not null)
            .Select(note => note.EventDate!.Value.Date)
            .Distinct()
            .Where(date => CountsInDailyAverage(
                date, all, today, documentationWindowDays, Choice(caseManagerChoices, date)))
            .ToHashSet();
    }

    /// <summary>
    /// Secured plus recoverable units on the counted days, per counted day, to one decimal. Units
    /// on a day the average does not divide by are left out of both halves, so an open day neither
    /// raises nor lowers it. Zero when no day qualifies yet.
    /// </summary>
    public static decimal DailyAverageUnits(
        IEnumerable<ProductivityNoteFact> notes,
        DateTime today,
        int documentationWindowDays,
        IReadOnlyDictionary<DateTime, bool>? caseManagerChoices = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var all = notes.ToList();
        var days = DailyAverageDays(all, today, documentationWindowDays, caseManagerChoices);
        if (days.Count == 0)
            return 0;

        var window = NormalizeDocumentationWindowDays(documentationWindowDays);
        var units = all
            .Where(note => note.EventDate is DateTime date && days.Contains(date.Date))
            .Sum(note => CountableUnits(note, today, window));
        return Math.Round(units / days.Count, 1);
    }

    /// <summary>Secured units, or recoverable pending units; anything else contributes nothing.</summary>
    private static decimal CountableUnits(ProductivityNoteFact note, DateTime today, int window)
    {
        if (SecuredStatuses.Contains(note.Status, StringComparer.OrdinalIgnoreCase))
            return CalculateUnits(note.Minutes);
        if (!string.Equals(note.Status, Pending, StringComparison.OrdinalIgnoreCase) ||
            note.EventDate is not DateTime date || date.Date > today.Date)
            return 0;
        return date.Date.AddDays(window) < today.Date ? 0 : CalculateUnits(note.Minutes);
    }

    private static bool? Choice(IReadOnlyDictionary<DateTime, bool>? choices, DateTime date) =>
        choices is not null && choices.TryGetValue(date.Date, out var choice) ? choice : null;

    /// <summary>Where <paramref name="date"/> stands in the average, given the notes dated on it.</summary>
    public static ProductivityDayKind ClassifyDay(
        DateTime date,
        IEnumerable<ProductivityNoteFact> notesOnDate,
        DateTime today,
        int documentationWindowDays,
        bool? caseManagerChoice)
    {
        ArgumentNullException.ThrowIfNull(notesOnDate);
        var onDate = notesOnDate.Where(note => note.EventDate?.Date == date.Date).ToList();
        if (!IsDocumentedServiceDay(date, onDate, today))
            return ProductivityDayKind.NotCounted;
        if (!CountsInDailyAverage(date, onDate, today, documentationWindowDays, caseManagerChoice))
            return ProductivityDayKind.OpenUntilDocumented;

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
