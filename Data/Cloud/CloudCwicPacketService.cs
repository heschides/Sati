using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudCwicPacketService(CloudApiClient client) : ICwicPacketService
{
    private const string UnfilledHeader = "X-Sati-Unfilled-Fields";

    public async Task<CwicPacketResult> GenerateAsync(
        int personId,
        CwicPacketRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = CwicPacketRules.Validate(request);
        if (validation.Count > 0)
            throw new ArgumentException(string.Join(" ", validation.SelectMany(entry => entry.Value)), nameof(request));

        var (pdf, headers) = await client.PostBytesWithHeaderAsync(
            $"/api/v1/people/{personId}/cwic-referral.pdf",
            request,
            UnfilledHeader,
            cancellationToken);
        var blankFields = headers.Count == 0
            ? []
            : headers[0].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new CwicPacketResult(
            pdf,
            CwicPacketService.SuggestedFileName(personId, null, null),
            blankFields,
            CwicPacketRules.SourceRevision);
    }
}
