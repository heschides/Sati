namespace Sati.Contracts.V1;

/// <summary>The calendar interval used to group a bounded statistics report.</summary>
public enum StatisticsPeriod
{
    Week,
    Month,
    Quarter,
    Year
}

/// <summary>
/// These measures have different denominators. Documented note units use the
/// productivity rounding rule; claim measures use their frozen decimal units.
/// Submitted means a non-synthetic 837P transmission, while Locked means the
/// billing period was submitted internally and may still await transmission.
/// Form and Visit activity may occur on the same note and therefore overlap.
/// Billable-marked means a documented note was not deliberately flagged
/// unbilled; it does not establish claim eligibility. A date with both kinds
/// contributes to both day counts, so those counts are not additive.
/// </summary>
public sealed record StatisticsMetrics
{
    public int DocumentedUnits { get; init; }
    public int BillableMarkedUnits { get; init; }
    public int NonBillableUnits { get; init; }
    public int PendingUnits { get; init; }
    public int ComplianceBlockedUnits { get; init; }
    public int FormDocumentedUnits { get; init; }
    public int VisitDocumentedUnits { get; init; }
    public decimal SubmittedClaimUnits { get; init; }
    public decimal SubmittedFormUnits { get; init; }
    public decimal SubmittedVisitUnits { get; init; }
    public decimal LockedClaimUnits { get; init; }
    public int DocumentedServiceDays { get; init; }
    public int BillableMarkedServiceDays { get; init; }
    public int NonBillableServiceDays { get; init; }
    public int ExpiredPendingServiceDays { get; init; }
    public int AbandonedServiceDays { get; init; }
    public int DocumentedNoteCount { get; init; }
    public int VisitNoteCount { get; init; }
    public int FormNoteCount { get; init; }
    public int SubmittedClaimCount { get; init; }
    public int SubmittedClaimsWithoutUnits { get; init; }
    public int LockedClaimCount { get; init; }
    public int LockedClaimsWithoutUnits { get; init; }
}

public sealed record StatisticsPeriodRow(DateTime Start, DateTime End, StatisticsMetrics Metrics);
public sealed record StatisticsClientRow(int PersonId, string ConsumerName, StatisticsMetrics Metrics);
public sealed record StatisticsBreakdownReportDto(
    StatisticsMetrics Totals,
    IReadOnlyList<StatisticsPeriodRow> Periods,
    IReadOnlyList<StatisticsClientRow> Clients);

// The database readers project only these bounded facts. Neither narrative nor
// visit documentation, financial identifiers, or form answers belong in this report.
public sealed record StatisticsClientFact(int PersonId, string ConsumerName);
public sealed record StatisticsNoteFact(
    int PersonId, DateTime EventDate, string? Status, int? Minutes,
    int? Activities, string? LegacyNoteType, bool IsUnbilled = false);
/// <param name="WasSubmitted">An exact non-synthetic 837P Transmitted event exists for the period.</param>
/// <param name="WasLocked">The source billing period has been submitted internally.</param>
public sealed record StatisticsClaimFact(
    int PersonId, DateTime ServiceDate, decimal? Units,
    int? Activities, string? LegacyNoteType,
    bool WasSubmitted, bool WasLocked = false);

/// <summary>Shared report accounting for the API and transitional local service.</summary>
public static class StatisticsBreakdownBuilder
{
    public static StatisticsBreakdownReportDto Build(
        DateTime windowStart,
        DateTime windowEnd,
        StatisticsPeriod period,
        DateTime today,
        int documentationWindowDays,
        IReadOnlyList<StatisticsClientFact> clients,
        IReadOnlyList<StatisticsNoteFact> notes,
        IReadOnlyList<StatisticsClaimFact> submittedClaims)
    {
        ValidateWindow(windowStart, windowEnd, period);
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(submittedClaims);

        var start = windowStart.Date;
        var end = windowEnd.Date;
        var clientList = clients.DistinctBy(client => client.PersonId).ToList();
        var clientIds = clientList.Select(client => client.PersonId).ToHashSet();
        var scopedNotes = notes.Where(note => clientIds.Contains(note.PersonId) &&
            note.EventDate.Date >= start && note.EventDate.Date <= end).ToList();
        var scopedClaims = submittedClaims.Where(claim =>
            clientIds.Contains(claim.PersonId) &&
            claim.ServiceDate.Date >= start && claim.ServiceDate.Date <= end).ToList();

        var notesByPeriod = scopedNotes
            .GroupBy(note => GetPeriodStart(note.EventDate.Date, period))
            .ToDictionary(group => group.Key, group => group.ToList());
        var claimsByPeriod = scopedClaims
            .GroupBy(claim => GetPeriodStart(claim.ServiceDate.Date, period))
            .ToDictionary(group => group.Key, group => group.ToList());
        var periodRows = new List<StatisticsPeriodRow>();
        for (var periodStart = GetPeriodStart(start, period);
             periodStart <= end;
             periodStart = NextPeriodStart(periodStart, period))
        {
            var visibleStart = periodStart < start ? start : periodStart;
            var periodEnd = NextPeriodStart(periodStart, period).AddDays(-1);
            var visibleEnd = periodEnd > end ? end : periodEnd;
            periodRows.Add(new StatisticsPeriodRow(
                visibleStart,
                visibleEnd,
                Aggregate(notesByPeriod.GetValueOrDefault(periodStart) ?? [],
                    claimsByPeriod.GetValueOrDefault(periodStart) ?? [],
                    today, documentationWindowDays)));
        }

        var notesByClient = scopedNotes.GroupBy(note => note.PersonId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var claimsByClient = scopedClaims.GroupBy(claim => claim.PersonId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var clientRows = clientList
            .OrderBy(client => client.ConsumerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(client => client.PersonId)
            .Select(client => new StatisticsClientRow(
                client.PersonId,
                client.ConsumerName,
                Aggregate(notesByClient.GetValueOrDefault(client.PersonId) ?? [],
                    claimsByClient.GetValueOrDefault(client.PersonId) ?? [],
                    today, documentationWindowDays)))
            .ToList();

        return new StatisticsBreakdownReportDto(
            Aggregate(scopedNotes, scopedClaims, today, documentationWindowDays),
            periodRows, clientRows);
    }

    public static void ValidateWindow(DateTime windowStart, DateTime windowEnd, StatisticsPeriod period)
    {
        var start = windowStart.Date;
        var end = windowEnd.Date;
        if (end < start || start.Year < 2000 || end.Year > 2200 ||
            (end - start).TotalDays > 3_660)
            throw new ArgumentException(
                "The statistics window must be within 2000-2200 and no longer than 10 years.");
        if (!Enum.IsDefined(period))
            throw new ArgumentOutOfRangeException(nameof(period), "Choose a supported statistics period.");
    }

    private static StatisticsMetrics Aggregate(
        IReadOnlyList<StatisticsNoteFact> notes,
        IReadOnlyList<StatisticsClaimFact> claims,
        DateTime today,
        int documentationWindowDays)
    {
        var documented = notes.Where(note => IsStatus(note.Status, "Logged") ||
                                             IsStatus(note.Status, "Approved")).ToList();
        var billableMarked = documented.Where(note => !note.IsUnbilled).ToList();
        var nonBillable = documented.Where(note => note.IsUnbilled).ToList();
        var pending = notes.Where(note => IsStatus(note.Status, "Pending")).ToList();
        var blocked = notes.Where(note => IsStatus(note.Status, "ComplianceBlocked")).ToList();
        var abandoned = notes.Where(note => IsStatus(note.Status, "Abandoned")).ToList();
        var formNotes = documented.Where(note => HasActivity(note, NoteActivity.Form)).ToList();
        var visitNotes = documented.Where(note => HasActivity(note, NoteActivity.Visit)).ToList();
        var transmittedClaims = claims.Where(claim => claim.WasSubmitted).ToList();
        var lockedClaims = claims.Where(claim => claim.WasLocked || claim.WasSubmitted).ToList();
        var formClaims = transmittedClaims.Where(claim => HasActivity(claim, NoteActivity.Form)).ToList();
        var visitClaims = transmittedClaims.Where(claim => HasActivity(claim, NoteActivity.Visit)).ToList();

        return new StatisticsMetrics
        {
            DocumentedUnits = documented.Sum(NoteUnits),
            BillableMarkedUnits = billableMarked.Sum(NoteUnits),
            NonBillableUnits = nonBillable.Sum(NoteUnits),
            PendingUnits = pending.Sum(NoteUnits),
            ComplianceBlockedUnits = blocked.Sum(NoteUnits),
            FormDocumentedUnits = formNotes.Sum(NoteUnits),
            VisitDocumentedUnits = visitNotes.Sum(NoteUnits),
            SubmittedClaimUnits = transmittedClaims.Sum(claim => claim.Units ?? 0m),
            SubmittedFormUnits = formClaims.Sum(claim => claim.Units ?? 0m),
            SubmittedVisitUnits = visitClaims.Sum(claim => claim.Units ?? 0m),
            LockedClaimUnits = lockedClaims.Sum(claim => claim.Units ?? 0m),
            DocumentedServiceDays = documented.Select(note => note.EventDate.Date).Distinct().Count(),
            BillableMarkedServiceDays = billableMarked
                .Select(note => note.EventDate.Date).Distinct().Count(),
            NonBillableServiceDays = nonBillable
                .Select(note => note.EventDate.Date).Distinct().Count(),
            ExpiredPendingServiceDays = pending
                .Where(note => ProductivityForecast.IsDocumentationWindowClosed(
                    note.EventDate, today, documentationWindowDays))
                .Select(note => note.EventDate.Date).Distinct().Count(),
            AbandonedServiceDays = abandoned.Select(note => note.EventDate.Date).Distinct().Count(),
            DocumentedNoteCount = documented.Count,
            VisitNoteCount = visitNotes.Count,
            FormNoteCount = formNotes.Count,
            SubmittedClaimCount = transmittedClaims.Count,
            SubmittedClaimsWithoutUnits = transmittedClaims.Count(claim => claim.Units is null),
            LockedClaimCount = lockedClaims.Count,
            LockedClaimsWithoutUnits = lockedClaims.Count(claim => claim.Units is null)
        };
    }

    private static int NoteUnits(StatisticsNoteFact note) =>
        (int)ProductivityForecast.CalculateUnits(note.Minutes);

    private static bool IsStatus(string? status, string expected) =>
        string.Equals(status, expected, StringComparison.OrdinalIgnoreCase);

    private static bool HasActivity(StatisticsNoteFact note, NoteActivity activity) =>
        NoteActivityRules.Has(note.Activities, note.LegacyNoteType, activity);

    private static bool HasActivity(StatisticsClaimFact claim, NoteActivity activity) =>
        NoteActivityRules.Has(claim.Activities, claim.LegacyNoteType, activity);

    private static DateTime GetPeriodStart(DateTime date, StatisticsPeriod period) => period switch
    {
        StatisticsPeriod.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        StatisticsPeriod.Month => new DateTime(date.Year, date.Month, 1),
        StatisticsPeriod.Quarter => new DateTime(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1),
        StatisticsPeriod.Year => new DateTime(date.Year, 1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(period))
    };

    private static DateTime NextPeriodStart(DateTime start, StatisticsPeriod period) => period switch
    {
        StatisticsPeriod.Week => start.AddDays(7),
        StatisticsPeriod.Month => start.AddMonths(1),
        StatisticsPeriod.Quarter => start.AddMonths(3),
        StatisticsPeriod.Year => start.AddYears(1),
        _ => throw new ArgumentOutOfRangeException(nameof(period))
    };
}
