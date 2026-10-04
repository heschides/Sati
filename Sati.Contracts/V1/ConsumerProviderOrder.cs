using System.Security.Cryptography;
using System.Text.Json;

namespace Sati.Contracts.V1;

public sealed record ReorderConsumerProvidersRequest(string ExpectedVersion, int[] OrderedLinkIds);

/// <summary>Collection concurrency without a second persisted ordering model.</summary>
public static class ConsumerProviderOrder
{
    public const string ConflictCode = "consumer_provider_order_changed";
    public const string ConflictMessage = "The provider assignments changed. Reload before saving your intended order.";

    // All retained relationship fields participate, including ended links. Directory
    // names/affiliations are intentionally excluded: ordering never writes those facts.
    public static string Version(IEnumerable<ConsumerProviderDto> links) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            links.OrderBy(link => link.Id).ToArray())));

    public static string? Validate(ReorderConsumerProvidersRequest request,
        IReadOnlyList<ConsumerProviderDto> links)
    {
        if (request.OrderedLinkIds is null || request.OrderedLinkIds.Length > ConsumerProviderRules.MaxSortOrder + 1)
            return "Supply the complete current provider order.";
        var current = links.Where(link => ConsumerProviderRules.IsCurrent(link.EndDate)).ToArray();
        if (request.OrderedLinkIds.Length != current.Length ||
            request.OrderedLinkIds.Distinct().Count() != current.Length ||
            !request.OrderedLinkIds.Order().SequenceEqual(current.Select(link => link.Id).Order()))
            return "The order must contain every current assignment exactly once and no other assignment.";
        var primary = current.SingleOrDefault(link => link.IsPrimaryCare);
        if (primary is not null && request.OrderedLinkIds[0] != primary.Id)
            return "Primary care stays first. Move the other current providers below it.";
        return null;
    }
}
