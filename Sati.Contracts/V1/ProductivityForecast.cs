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
    OpenUntilDocumented,

    /// <summary>
    /// A workday the case manager has marked as finished with nothing billable on it. It counts
    /// in the average at zero units, because the month's requirement did not shrink when the day
    /// produced nothing, and it is no longer work waiting to be written up.
    /// </summary>
    CountedWithoutBillableWork
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

    /// <summary>Any pending, logged, or approved note dated on this day, whatever month it is in.</summary>
    private static bool HasDocumentedWork(
        DateTime date,
        IEnumerable<ProductivityNoteFact> notes) =>
        notes.Any(note => note.EventDate?.Date == date.Date &&
                          AverageStatuses.Contains(note.Status, StringComparer.OrdinalIgnoreCase));

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
        {
            // A workday marked as finished with nothing billable counts at zero units. The
            // requirement did not shrink because the day produced nothing, so the rest of the
            // month has to make it up, and the average is where that shows.
            return caseManagerChoice == true && IsInAverageScope(date, today);
        }

        if (IsDocumentationWindowClosed(date, today, documentationWindowDays))
            return true;
        return caseManagerChoice ?? !HasScheduledWork(date, notesOnDate);
    }

    private static bool IsInAverageScope(DateTime date, DateTime today) =>
        date.Date.Year == today.Date.Year && date.Date.Month == today.Date.Month &&
        date.Date <= today.Date;

    /// <summary>
    /// Whether the case manager can still decide this day. A settled service day is no longer
    /// theirs to hold. An eligible workday with nothing on it can still be marked as finished
    /// with no billable work, which is how a real zero day reaches the numbers on the day it
    /// happens rather than a week later.
    /// </summary>
    public static bool CanChooseDailyAverageDay(
        DateTime date,
        IEnumerable<ProductivityNoteFact> notesOnDate,
        DateTime today,
        int documentationWindowDays,
        bool isEligibleWorkday = true)
    {
        ArgumentNullException.ThrowIfNull(notesOnDate);
        if (IsDocumentationWindowClosed(date, today, documentationWindowDays))
            return false;
        if (IsDocumentedServiceDay(date, notesOnDate, today))
            return true;
        return isEligibleWorkday && IsInAverageScope(date, today);
    }

    /// <summary>
    /// Past workdays whose units this month can still capture: eligible workdays inside their
    /// documentation window that are neither finished nor written up yet. The work happened, so
    /// writing it up still produces units without spending a future day.
    /// <para>
    /// <paramref name="pastEligibleWorkdays"/> comes from the caller, which already knows the
    /// agency calendar and the case manager's time off. Today is not among them: it is already
    /// counted as future capacity.
    /// </para>
    /// </summary>
    public static IReadOnlyList<DateTime> PastWorkdaysStillToDocument(
        IEnumerable<DateTime> pastEligibleWorkdays,
        IEnumerable<ProductivityNoteFact> notes,
        DateTime today,
        int documentationWindowDays,
        IReadOnlyDictionary<DateTime, bool>? caseManagerChoices = null)
    {
        ArgumentNullException.ThrowIfNull(pastEligibleWorkdays);
        ArgumentNullException.ThrowIfNull(notes);
        var all = notes.ToList();
        return pastEligibleWorkdays
            .Select(date => date.Date)
            .Distinct()
            .Where(date => date < today.Date &&
                           !IsDocumentationWindowClosed(date, today, documentationWindowDays) &&
                           !CountsInDailyAverage(
                               date, all, today, documentationWindowDays,
                               Choice(caseManagerChoices, date)))
            .OrderBy(date => date)
            .ToList();
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
        // Days the case manager marked carry no notes of their own, so the dates have to come
        // from the decisions as well: a day marked as having produced nothing is still a day.
        return all
            .Where(note => note.EventDate is not null)
            .Select(note => note.EventDate!.Value.Date)
            .Concat(caseManagerChoices?.Keys.Select(date => date.Date) ?? [])
            .Distinct()
            .Where(date => CountsInDailyAverage(
                date, all, today, documentationWindowDays, Choice(caseManagerChoices, date)))
            .ToHashSet();
    }

    /// <summary>
    /// Days the case manager marked as having produced nothing billable, and whose documentation
    /// window has since closed. Only these reach a supervisor: while the window is open the mark
    /// is a working annotation the case manager can still change by writing the day up, and a
    /// supervisor asking about a day that is still being documented would be asking too early.
    /// </summary>
    public static IReadOnlyList<DateTime> SettledDaysWithoutBillableWork(
        IEnumerable<ProductivityNoteFact> notes,
        DateTime today,
        int documentationWindowDays,
        IReadOnlyDictionary<DateTime, bool>? caseManagerChoices)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (caseManagerChoices is null)
            return [];

        var all = notes.ToList();
        return caseManagerChoices
            .Where(choice => choice.Value)
            .Select(choice => choice.Key.Date)
            .Where(date => IsDocumentationWindowClosed(date, today, documentationWindowDays) &&
                           !HasDocumentedWork(date, all))
            .OrderBy(date => date)
            .ToList();
    }

    /// <summary>
    /// Undocumented past workdays whose documentation window closes within
    /// <paramref name="noticeDays"/> days. After that the day can no longer be billed, its units
    /// are gone, and the required pace steps up. The warning has to reach the case manager while
    /// they can still act on it, so it is a rule rather than a screen.
    /// </summary>
    public static IReadOnlyList<DateTime> DaysNearingTheirDocumentationDeadline(
        IEnumerable<DateTime> pastEligibleWorkdays,
        IEnumerable<ProductivityNoteFact> notes,
        DateTime today,
        int documentationWindowDays,
        IReadOnlyDictionary<DateTime, bool>? caseManagerChoices = null,
        int noticeDays = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(noticeDays);
        var window = NormalizeDocumentationWindowDays(documentationWindowDays);
        var limit = today.Date.AddDays(noticeDays);
        return PastWorkdaysStillToDocument(
                pastEligibleWorkdays, notes, today, documentationWindowDays, caseManagerChoices)
            .Where(date => date.AddDays(window) <= limit)
            .ToList();
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
        {
            return CountsInDailyAverage(date, onDate, today, documentationWindowDays, caseManagerChoice)
                ? ProductivityDayKind.CountedWithoutBillableWork
                : ProductivityDayKind.NotCounted;
        }

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

    /// <param name="pastDaysStillToDocument">
    /// Past workdays whose work can still be written up and billed this month, from
    /// <see cref="PastWorkdaysStillToDocument"/>. They are capacity in the same sense a future day
    /// is: units can still land on them. Leaving them out told a case manager who documents in
    /// batches that every remaining unit had to come from the days ahead, which read far too high.
    /// </param>
    /// <param name="pastDaysStillToDocumentAfterToday">
    /// The same days whose window is still open tomorrow, for the pace that assumes today's
    /// deadline work expires.
    /// </param>
    public static ProductivityForecastResult Calculate(
        decimal monthlyTarget,
        int futureEligibleDays,
        int eligibleDaysAfterToday,
        DateTime today,
        int documentationWindowDays,
        IEnumerable<ProductivityNoteFact> notes,
        int pastDaysStillToDocument = 0,
        int pastDaysStillToDocumentAfterToday = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(monthlyTarget);
        ArgumentOutOfRangeException.ThrowIfNegative(futureEligibleDays);
        ArgumentOutOfRangeException.ThrowIfNegative(eligibleDaysAfterToday);
        ArgumentOutOfRangeException.ThrowIfNegative(pastDaysStillToDocument);
        ArgumentOutOfRangeException.ThrowIfNegative(pastDaysStillToDocumentAfterToday);
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

        // Capacity is every day units can still land on: the days ahead, and the past days whose
        // work has not been written up while their window is open.
        var capacity = futureEligibleDays + pastDaysStillToDocument;
        var capacityAfterToday = eligibleDaysAfterToday + pastDaysStillToDocumentAfterToday;

        return new ProductivityForecastResult(
            secured,
            recoverable,
            dueToday,
            expired,
            pendingWithoutUnits,
            futureEligibleDays,
            eligibleDaysAfterToday,
            Pace(monthlyTarget - secured, capacity),
            Pace(monthlyTarget - secured - recoverable, capacity),
            Pace(monthlyTarget - secured - (recoverable - dueToday), capacityAfterToday));
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
