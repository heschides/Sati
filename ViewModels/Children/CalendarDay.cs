using Sati.Contracts.V1;
using Sati.Services;

namespace Sati.ViewModels.Children;

public sealed class CalendarDay
{
    public DateTime Date { get; init; }
    public bool IsExempt { get; set; }
    public int? ExemptDateId { get; set; }
    public bool IsWeekend { get; init; }
    public bool IsToday => Date.Date == DateTime.Today;
    public List<CalendarNoteItem> Notes { get; init; } = [];
    public List<ImportedOutlookEvent> OutlookEvents { get; init; } = [];
    public int NoteCount => Notes.Count;
    public bool HasNotes => NoteCount > 0;
    public int OutlookEventCount => OutlookEvents.Count;
    public bool HasOutlookEvents => OutlookEventCount > 0;

    /// <summary>Where this day stands in the current month's documented daily average.</summary>
    public ProductivityDayKind ProductivityKind { get; init; }
    public bool CountsWithSecuredUnits => ProductivityKind == ProductivityDayKind.CountedWithSecuredUnits;
    public bool CountsWithoutSecuredUnits => ProductivityKind == ProductivityDayKind.CountedWithoutSecuredUnits;
    public bool CountsTowardAverage => ProductivityKind != ProductivityDayKind.NotCounted;

    public int TotalUnits => Notes.Sum(note => note.Units ?? 0);
    public bool HasUnits => TotalUnits > 0;
    public string UnitsLabel => TotalUnits == 1 ? "1 unit" : $"{TotalUnits} units";

    /// <summary>Units per note status, in status order: "Pending 2 · Logged 4".</summary>
    public string UnitsByStatusLabel => string.Join(" · ", Notes
        .Where(note => note.Units is > 0)
        .GroupBy(note => (note.StatusOrder, note.StatusLabel))
        .OrderBy(group => group.Key.StatusOrder)
        .Select(group => $"{group.Key.StatusLabel} {group.Sum(note => note.Units ?? 0)}"));

    /// <summary>The words that carry what the fill colour shows.</summary>
    public string ProductivityLabel => ProductivityKind switch
    {
        ProductivityDayKind.CountedWithSecuredUnits => "In average",
        ProductivityDayKind.CountedWithoutSecuredUnits => "In average · none logged",
        _ => string.Empty
    };

    public string AccessibleLabel
    {
        get
        {
            var noteText = NoteCount == 1 ? "1 note" : $"{NoteCount} notes";
            var unitText = HasUnits ? $", {UnitsLabel} ({UnitsByStatusLabel})" : string.Empty;
            var outlookText = OutlookEventCount == 1
                ? ", 1 Outlook event"
                : OutlookEventCount > 1
                    ? $", {OutlookEventCount} Outlook events"
                    : string.Empty;
            var exemptText = IsExempt ? ", exempt day" : string.Empty;
            var productivityText = ProductivityKind switch
            {
                ProductivityDayKind.CountedWithSecuredUnits =>
                    ", counts toward this month's daily average",
                ProductivityDayKind.CountedWithoutSecuredUnits =>
                    ", counts toward this month's daily average, nothing logged or approved yet",
                _ => string.Empty
            };
            return $"{Date:dddd, MMMM d, yyyy}, {noteText}{unitText}{outlookText}{exemptText}{productivityText}";
        }
    }
}
