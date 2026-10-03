namespace Sati.Models;

/// <summary>
/// Prior-day plan recorded with the note's successful date edit. The row is
/// immutable while its note exists and is deleted with that deletable note.
/// Dates, estimated minutes, units, and original scope are frozen at that edit.
/// </summary>
public sealed class ScheduledNoteMove
{
    public long Id { get; set; }
    public int NoteId { get; set; }
    public int PersonId { get; set; }
    public int AgencyId { get; set; }
    public int UserId { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int? ScheduledMinutes { get; set; }
    public int? ScheduledUnits { get; set; }
    public int NoteRevision { get; set; }
    public DateTime MovedAtUtc { get; set; }
}
