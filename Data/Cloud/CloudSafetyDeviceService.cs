using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudSafetyDeviceService(CloudApiClient client) : ISafetyDeviceService
{
    private const string ReviewHeader = "X-Sati-Review-Items";

    public async Task<SafetyDeviceResult> GenerateAsync(int personId, SafetyDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = SafetyDeviceRules.Validate(request, DateOnly.FromDateTime(DateTime.Today));
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors.SelectMany(entry => entry.Value)), nameof(request));
        var (pdf, headers) = await client.PostBytesWithHeaderAsync(
            $"/api/v1/people/{personId}/safety-device-request.pdf", request,
            ReviewHeader, cancellationToken);
        var reviewItems = headers.Count == 0 ? [] :
            headers[0].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new SafetyDeviceResult(pdf,
            SafetyDeviceService.SuggestedFileName(personId, null, null), reviewItems,
            SafetyDeviceRules.SourceRevision);
    }
}
