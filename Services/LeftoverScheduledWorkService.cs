using Sati.Data;
using Sati.Helpers;
using Sati.Models;

namespace Sati.Services;

public enum LeftoverScheduledWorkChoice
{
    MoveToNextWorkday,
    Delete,
    /// <summary>
    /// Leave the item on today, unfinished, for a case manager who is closing Sati but will be
    /// back before the day ends. An item from a past day is brought forward to today, never left
    /// on the finished day.
    /// </summary>
    KeepForToday
}

public sealed record LeftoverScheduledWorkDecision(Note Note, LeftoverScheduledWorkChoice Choice);

public sealed record LeftoverScheduledWorkResult(int Moved, int Kept, int Deleted, int Failed);

/// <summary>
/// Finds planned work whose day has passed without it being done, and carries out the case
/// manager's decision about each item at shutdown. A Scheduled note left on a finished day is
/// not a record of anything: it clutters the calendar, and it also holds that day open in the
/// productivity average (<see cref="Contracts.V1.ProductivityForecast"/> treats scheduled work
/// as a day still being written up). Moving or deleting it is the honest resolution. Today is not
/// finished yet, so keeping an item on today is also honest; it is offered again at the next close.
/// </summary>
/// <remarks>
/// Every write goes through <see cref="INoteService"/>, so the same ownership, workflow,
/// concurrency, and audit checks apply here as when the item is edited by hand, locally or
/// through the API. Only Scheduled notes are touched; anything started, logged, or approved
/// is never offered.
/// </remarks>
public interface ILeftoverScheduledWorkService
{
    /// <summary>Scheduled items dated <paramref name="today"/> or earlier, oldest first.</summary>
    Task<IReadOnlyList<Note>> LoadAsync(int userId, DateTime today);

    /// <summary>The next workday after <paramref name="today"/>, or null when none is configured.</summary>
    Task<DateTime?> NextWorkdayAsync(int userId, DateTime today);

    Task<LeftoverScheduledWorkResult> ApplyAsync(
        IReadOnlyList<LeftoverScheduledWorkDecision> decisions,
        DateTime nextWorkday,
        DateTime today);
}

public sealed class LeftoverScheduledWorkService(
    INoteService notes,
    ISettingsService settings,
    IExemptDateService exemptDates) : ILeftoverScheduledWorkService
{
    public async Task<IReadOnlyList<Note>> LoadAsync(int userId, DateTime today)
    {
        // The year query is the one the calendar already uses. Last year is included so a
        // backlog left over December is still found in January.
        var rows = new List<Note>();
        foreach (var year in new[] { today.Year - 1, today.Year })
            rows.AddRange(await notes.GetByYearAsync(userId, year));

        return rows
            .Where(note => note.Status == NoteStatus.Scheduled &&
                           note.EventDate is DateTime date && date.Date <= today.Date)
            .DistinctBy(note => note.Id)
            .OrderBy(note => note.EventDate)
            .ThenBy(note => note.StartTime ?? int.MaxValue)
            .ToList();
    }

    public async Task<DateTime?> NextWorkdayAsync(int userId, DateTime today)
    {
        var agencySettings = await settings.LoadAsync();
        var timeOff = new List<DateTime>();
        foreach (var year in new[] { today.Year, today.Year + 1 })
            timeOff.AddRange((await exemptDates.GetByYearAsync(userId, year)).Select(date => date.Date));
        return WorkdayHelper.NextWorkday(today, agencySettings, timeOff);
    }

    public async Task<LeftoverScheduledWorkResult> ApplyAsync(
        IReadOnlyList<LeftoverScheduledWorkDecision> decisions,
        DateTime nextWorkday,
        DateTime today)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        int moved = 0, kept = 0, deleted = 0, failed = 0;
        foreach (var decision in decisions)
        {
            // Keeping an item already on today writes nothing, so there is nothing to refuse.
            if (decision.Choice == LeftoverScheduledWorkChoice.KeepForToday &&
                decision.Note.EventDate?.Date == today.Date)
            {
                kept++;
                continue;
            }

            // Re-checked here rather than trusted from the list: a stale row must not carry a
            // started or submitted note into a delete.
            if (decision.Note.Status != NoteStatus.Scheduled)
            {
                failed++;
                continue;
            }

            var originalDate = decision.Note.EventDate;
            var originalStartTime = decision.Note.StartTime;
            try
            {
                if (decision.Choice == LeftoverScheduledWorkChoice.Delete)
                {
                    await notes.DeleteNoteAsync(decision.Note);
                    deleted++;
                }
                else if (decision.Choice == LeftoverScheduledWorkChoice.KeepForToday)
                {
                    // Brought forward from a past day. NoteSchedulingPolicy clears the start
                    // time only for future dates, so clear the past day's slot here as a move
                    // to a later workday would.
                    decision.Note.EventDate = today.Date;
                    decision.Note.StartTime = null;
                    await notes.UpdateNoteAsync(decision.Note);
                    kept++;
                }
                else
                {
                    decision.Note.EventDate = nextWorkday.Date;
                    await notes.UpdateNoteAsync(decision.Note);
                    moved++;
                }
            }
            catch (Exception error)
            {
                // One refused item must not stop the rest, or stop Sati closing. The item
                // stays where it was and is offered again next time.
                decision.Note.EventDate = originalDate;
                decision.Note.StartTime = originalStartTime;
                AppErrorLog.Record(error, "leftover-scheduled-work.apply");
                failed++;
            }
        }

        return new LeftoverScheduledWorkResult(moved, kept, deleted, failed);
    }
}
