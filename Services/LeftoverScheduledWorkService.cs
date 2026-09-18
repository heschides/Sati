using Sati.Data;
using Sati.Helpers;
using Sati.Models;

namespace Sati.Services;

public enum LeftoverScheduledWorkChoice
{
    MoveToNextWorkday,
    Delete
}

public sealed record LeftoverScheduledWorkDecision(Note Note, LeftoverScheduledWorkChoice Choice);

public sealed record LeftoverScheduledWorkResult(int Moved, int Deleted, int Failed);

/// <summary>
/// Finds planned work whose day has passed without it being done, and carries out the case
/// manager's decision about each item at shutdown. A Scheduled note left on a finished day is
/// not a record of anything: it clutters the calendar, and it also holds that day open in the
/// productivity average (<see cref="Contracts.V1.ProductivityForecast"/> treats scheduled work
/// as a day still being written up). Moving or deleting it is the honest resolution.
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
        DateTime nextWorkday);
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
        DateTime nextWorkday)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        int moved = 0, deleted = 0, failed = 0;
        foreach (var decision in decisions)
        {
            // Re-checked here rather than trusted from the list: a stale row must not carry a
            // started or submitted note into a delete.
            if (decision.Note.Status != NoteStatus.Scheduled)
            {
                failed++;
                continue;
            }

            var originalDate = decision.Note.EventDate;
            try
            {
                if (decision.Choice == LeftoverScheduledWorkChoice.Delete)
                {
                    await notes.DeleteNoteAsync(decision.Note);
                    deleted++;
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
                AppErrorLog.Record(error, "leftover-scheduled-work.apply");
                failed++;
            }
        }

        return new LeftoverScheduledWorkResult(moved, deleted, failed);
    }
}
