using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;

namespace Sati.Api.Endpoints;

/// <summary>
/// Contact history for the monthly-contact billing requirement. Every endpoint that
/// makes a billing-compliance decision attaches it to the consumers it evaluates;
/// only dates, types, and statuses are read, never narratives.
/// </summary>
internal static partial class ApiEndpoints
{
    private static async Task<ILookup<int, ContactFact>> LoadContactFactsByPersonAsync(
        ApiDbContext db,
        int agencyId,
        IReadOnlyCollection<int> personIds,
        CancellationToken cancellationToken)
    {
        if (personIds.Count == 0)
            return Array.Empty<ContactFact>().ToLookup(_ => 0);

        var notes = await db.Notes.AsNoTracking()
            .Where(note => personIds.Contains(note.PersonId) &&
                           note.AgencyId == agencyId &&
                           note.EventDate != null)
            .Select(note => new { note.Id, note.PersonId, note.EventDate, note.NoteType, note.Activities, note.Status })
            .ToListAsync(cancellationToken);
        return notes
            .Select(note => new
            {
                note.PersonId,
                Fact = MonthlyContactRules.ToFact(
                    ContractMapper.NoteTypeName(note.NoteType),
                    ContractMapper.NoteStatusName(note.Status),
                    note.EventDate,
                    note.Id,
                    note.Activities)
            })
            .Where(item => item.Fact is not null)
            .ToLookup(item => item.PersonId, item => item.Fact!);
    }

    /// <summary>
    /// Attaches contact history to every instance of each consumer. No-tracking
    /// queries return one instance per row, so the same consumer can appear several times.
    /// </summary>
    private static async Task PopulateContactHistoryAsync(
        ApiDbContext db,
        int agencyId,
        IEnumerable<ServerPerson> people,
        CancellationToken cancellationToken)
    {
        var instances = people.ToArray();
        var contacts = await LoadContactFactsByPersonAsync(
            db,
            agencyId,
            instances.Select(person => person.Id).Distinct().ToArray(),
            cancellationToken);
        foreach (var person in instances)
            person.ContactFactsForCompliance = contacts[person.Id].ToArray();
    }

    private static ContactFact? ToContactFact(ServerNote note) =>
        MonthlyContactRules.ToFact(
            ContractMapper.NoteTypeName(note.NoteType),
            ContractMapper.NoteStatusName(note.Status),
            note.EventDate,
            note.Id,
            note.Activities);

    /// <summary>
    /// The consumer's contact history with the note under evaluation replacing its
    /// stored copy, so a visit being logged counts toward its own service date.
    /// </summary>
    private static IReadOnlyList<ContactFact>? ContactHistoryFor(ServerPerson person, ServerNote note) =>
        person.ContactFactsForCompliance is null
            ? null
            : MonthlyContactRules.WithCandidate(
                person.ContactFactsForCompliance,
                note.Id,
                ToContactFact(note));
}
