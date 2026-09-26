using Sati.Contracts.V1;

namespace Sati.ViewModels.ClientDocuments;

/// <summary>A shaded span of the timeline, as fractions of the track.</summary>
public sealed record PlanYearTimelineZone(double StartFraction, double WidthFraction, string Caption, string Kind);

/// <summary>One date on the timeline; several items due the same day share a marker.</summary>
public sealed record PlanYearTimelineMarker(
    double StartFraction,
    string DateLabel,
    string Caption,
    string StateKey,
    string AccessibleName,
    bool LabelAbove)
{
    /// <summary>Centers the marker and its label on the date.</summary>
    public double LabelStartFraction => StartFraction;
}

public sealed record PlanYearTimelineTick(double StartFraction, string Label);

/// <summary>
/// Lays a plan year out as fractions of a horizontal track: the preparation before it
/// begins, the year itself, the next year's preparation window, today, and every dated
/// item. Pure arithmetic; the view scales fractions against the track's measured width.
/// </summary>
public sealed class PlanYearTimeline
{
    public IReadOnlyList<PlanYearTimelineZone> Zones { get; private init; } = [];
    public IReadOnlyList<PlanYearTimelineMarker> Markers { get; private init; } = [];
    public IReadOnlyList<PlanYearTimelineTick> Ticks { get; private init; } = [];
    public double? TodayFraction { get; private init; }
    public bool HasToday => TodayFraction is not null;
    public double TodayStartFraction => TodayFraction ?? 0;
    public string TodayLabel { get; private init; } = "";
    public string AccessibleSummary { get; private init; } = "";

    public static PlanYearTimeline Build(PlanYear year, PlanYear? next, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(year);
        today = today.Date;
        var dated = year.Items.Where(item => item.DueOn is not null).ToList();
        var earliest = dated.Select(item => item.DueOn!.Value.Date).DefaultIfEmpty(year.Start).Min();
        var latest = dated.Select(item => item.DueOn!.Value.Date).DefaultIfEmpty(year.EndInclusive).Max();
        var rangeStart = MonthStart(Min(year.Start.AddDays(-90), earliest));
        var rangeEnd = MonthStart(Max(year.EndInclusive.AddDays(1), latest)).AddMonths(1);
        var span = (rangeEnd - rangeStart).TotalDays;
        double At(DateTime date) => Math.Clamp((date.Date - rangeStart).TotalDays / span, 0, 1);

        var zones = new List<PlanYearTimelineZone>
        {
            new(0, At(year.Start), "Prepared before the year", "Prep"),
            new(At(year.Start), At(year.EndInclusive.AddDays(1)) - At(year.Start), "", "Year")
        };
        if (next?.WorkOpensOn is DateTime opens && opens <= year.EndInclusive)
        {
            var from = At(Max(opens, year.Start));
            zones.Add(new(from, At(year.EndInclusive.AddDays(1)) - from,
                $"Next year's preparation opens {opens:MMM d}", "NextPrep"));
        }

        var markers = new List<PlanYearTimelineMarker>();
        double? previous = null;
        var above = false;
        foreach (var group in dated.GroupBy(item => item.DueOn!.Value.Date).OrderBy(group => group.Key))
        {
            var items = group.ToList();
            var worst = items.OrderBy(item => Severity(item.State)).First().State;
            var fraction = At(group.Key);
            // Neighbors closer than a label's width alternate above and below the line.
            above = previous is double last && fraction - last < 0.09 ? !above : false;
            previous = fraction;
            markers.Add(new PlanYearTimelineMarker(
                fraction,
                group.Key.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture),
                items.Count == 1 ? ShortTitle(items[0].Title) : $"{items.Count} items",
                worst.ToString(),
                $"{group.Key:MMMM d, yyyy}: " + string.Join("; ", items.Select(item => $"{item.Title}, {item.StateLabel}")),
                above));
        }

        var ticks = new List<PlanYearTimelineTick>();
        for (var month = rangeStart; month <= rangeEnd; month = month.AddMonths(3))
            ticks.Add(new(At(month), month.Month == 1 || month == rangeStart
                ? month.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : month.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture)));

        var showToday = today >= rangeStart && today < rangeEnd;
        return new PlanYearTimeline
        {
            Zones = zones,
            Markers = markers,
            Ticks = ticks,
            TodayFraction = showToday ? At(today) : null,
            TodayLabel = showToday ? $"Today · {today:MMM d}" : "",
            AccessibleSummary = $"Plan year {year.Label}, {year.Start:MMMM d, yyyy} to {year.EndInclusive:MMMM d, yyyy}. " +
                                $"{year.NeedsYouCount} need attention, {year.ComingUpCount} coming up, {year.DoneCount} done."
        };
    }

    private static int Severity(PlanYearItemState state) => state switch
    {
        PlanYearItemState.Overdue => 0,
        PlanYearItemState.Open => 1,
        PlanYearItemState.ComingUp => 2,
        _ => 3
    };

    private static string ShortTitle(string title)
    {
        var separator = title.IndexOf(" · ", StringComparison.Ordinal);
        return separator > 0 ? title[..separator] : title;
    }

    private static DateTime MonthStart(DateTime date) => new(date.Year, date.Month, 1);
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
