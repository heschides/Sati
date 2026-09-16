using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

/// <summary>
/// Supplies the read-side billing rule with facts that do not live on the person row:
/// provider assignments and recorded contact history. Persisted release rows remain
/// authoritative evidence; provider facts only ensure that a missing derived row
/// cannot make billing pass open. Contact history is read as dates and types only,
/// never narratives.
/// </summary>
internal static class BillingComplianceProjectionLoader
{
    public static async Task PopulateAsync(
        SatiContext context,
        IEnumerable<Person> people,
        int agencyId,
        CancellationToken cancellationToken = default)
    {
        var personList = people.DistinctBy(person => person.Id).ToArray();
        var personIds = personList.Select(person => person.Id).ToArray();
        if (personIds.Length == 0)
            return;

        var rows = await (from link in context.PersonProviders.AsNoTracking()
                          join provider in context.Providers.AsNoTracking()
                              on link.ProviderId equals provider.Id
                          where personIds.Contains(link.PersonId) &&
                                provider.AgencyId == agencyId
                          select new
                          {
                              link.PersonId,
                              Fact = new ReleaseProviderLinkFact(
                                  link.Id,
                                  link.ProviderId,
                                  provider.Type.ToString(),
                                  link.Role,
                                  link.StartDate,
                                  link.EndDate,
                                  link.AssignmentKnownOn,
                                  provider.Name)
                          })
            .ToListAsync(cancellationToken);
        var byPerson = rows.ToLookup(row => row.PersonId, row => row.Fact);

        var contacts = await LoadContactFactsAsync(context, personIds, agencyId, cancellationToken);

        // Several detached instances of one consumer can be in play (one per note row);
        // every instance gets the same facts.
        foreach (var person in people)
        {
            person.ReleaseProviderLinksForCompliance = byPerson[person.Id].ToList();
            person.ContactFactsForCompliance = contacts[person.Id].ToList();
        }
    }

    public static async Task<ILookup<int, ContactFact>> LoadContactFactsAsync(
        SatiContext context,
        IReadOnlyCollection<int> personIds,
        int agencyId,
        CancellationToken cancellationToken = default)
    {
        if (personIds.Count == 0)
            return Array.Empty<ContactFact>().ToLookup(_ => 0);

        var notes = await context.Notes.AsNoTracking()
            .Where(note => personIds.Contains(note.PersonId) &&
                           note.AgencyId == agencyId &&
                           note.EventDate != null)
            .Select(note => new { note.Id, note.PersonId, note.EventDate, note.NoteType, note.Status })
            .ToListAsync(cancellationToken);

        return notes
            .Select(note => new
            {
                note.PersonId,
                Fact = MonthlyContactRules.ToFact(
                    note.NoteType?.ToString(),
                    note.Status?.ToString(),
                    note.EventDate,
                    note.Id)
            })
            .Where(item => item.Fact is not null)
            .ToLookup(item => item.PersonId, item => item.Fact!);
    }
}
