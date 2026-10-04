using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

internal static class ConsumerProviderOrderSnapshot
{
    internal static ConsumerProviderDto Fact(PersonProvider link) => new(
        link.Id, link.PersonId, link.ProviderId, link.Role, link.IsPrimaryCare,
        link.StartDate, link.EndDate, link.HasActiveRelease, link.SortOrder, link.AssignmentKnownOn);
}
