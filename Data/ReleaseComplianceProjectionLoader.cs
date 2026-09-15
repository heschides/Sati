using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

/// <summary>
/// Supplies the read-side billing rule with provider assignments. Persisted
/// release rows remain authoritative evidence; these facts only ensure that a
/// missing derived row cannot make billing pass open.
/// </summary>
internal static class ReleaseComplianceProjectionLoader
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
        foreach (var person in personList)
            person.ReleaseProviderLinksForCompliance = byPerson[person.Id].ToList();
    }
}
