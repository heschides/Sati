using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudHousingSupportFundsService(CloudApiClient client) : IHousingSupportFundsService
{
    private const string ReviewHeader = "X-Sati-Review-Items";

    public async Task<HousingSupportFundsResult> GenerateAsync(
        int personId,
        HousingSupportFundsRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = HousingSupportFundsRules.Validate(request);
        if (validation.Count > 0)
            throw new ArgumentException(string.Join(" ", validation.SelectMany(entry => entry.Value)), nameof(request));

        var (pdf, headers) = await client.PostBytesWithHeaderAsync(
            $"/api/v1/people/{personId}/housing-support-funds.pdf",
            request,
            ReviewHeader,
            cancellationToken);
        var reviewItems = headers.Count == 0
            ? []
            : headers[0].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new HousingSupportFundsResult(
            pdf,
            HousingSupportFundsService.SuggestedFileName(personId, null, null),
            reviewItems,
            HousingSupportFundsRules.SourceRevision);
    }
}
