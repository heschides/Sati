using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

public sealed class ScheduledNoteMoveService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : IScheduledNoteMoveService
{
    public async Task<IReadOnlyList<ScheduledNoteMoveDto>> GetByYearAsync(int year)
    {
        if (year is < 2000 or > 2200)
            throw new ArgumentOutOfRangeException(nameof(year));
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in user is required.");
        await using var context = contextFactory.CreateDbContext();
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        if (!await LocalTenantAccess.CanAccessUserAsync(context, actor, actor.Id))
            throw new UnauthorizedAccessException("Current case-management permission is required.");

        var first = new DateTime(year, 1, 1);
        var end = first.AddYears(1);
        var rows = await (from move in context.ScheduledNoteMoves.AsNoTracking()
                          join note in context.Notes.AsNoTracking() on move.NoteId equals note.Id
                          join person in context.People.AsNoTracking() on move.PersonId equals person.Id
                          join currentPerson in context.People.AsNoTracking()
                              on note.PersonId equals currentPerson.Id
                          where move.UserId == actor.Id &&
                                move.AgencyId == actor.AgencyId &&
                                note.AgencyId == actor.AgencyId &&
                                person.UserId == actor.Id &&
                                person.AgencyId == actor.AgencyId &&
                                person.Status != PersonStatus.Ghost &&
                                currentPerson.UserId == actor.Id &&
                                currentPerson.AgencyId == actor.AgencyId &&
                                currentPerson.Status != PersonStatus.Ghost &&
                                move.FromDate >= first && move.FromDate < end
                          orderby move.FromDate, move.Id
                          select new
                          {
                              Move = move,
                              CurrentNoteDate = note.EventDate,
                              person.FirstName,
                              person.LastName
                          }).ToListAsync();
        return rows
            .Where(row => row.CurrentNoteDate?.Date != row.Move.FromDate.Date)
            .GroupBy(row => (row.Move.NoteId, row.Move.FromDate.Date))
            .Select(group => group.OrderByDescending(row => row.Move.NoteRevision).First())
            .OrderBy(row => row.Move.FromDate)
            .ThenBy(row => row.Move.Id)
            .Select(row => new ScheduledNoteMoveDto(
                row.Move.Id,
                row.Move.NoteId,
                row.Move.PersonId,
                ($"{row.LastName}, {row.FirstName}").Trim(' ', ','),
                row.Move.FromDate,
                row.Move.ToDate,
                row.Move.ScheduledMinutes,
                row.Move.ScheduledUnits,
                row.Move.MovedAtUtc))
            .ToList();
    }
}
