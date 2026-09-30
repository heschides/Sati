using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudBenefitsApplicationService(CloudApiClient client) : IBenefitsApplicationService
{
    public async Task<BenefitsApplicationResult> GenerateAsync(int personId,
        BenefitsApplicationRequest request, CancellationToken cancellationToken = default)
    {
        if (BenefitsApplicationRules.Validate(request).Count > 0)
            throw new ArgumentException("The application contains an invalid answer.", nameof(request));
        var pdf = await client.PostBytesAsync(
            $"/api/v1/people/{personId}/benefits-application.pdf", request, cancellationToken);
        return new BenefitsApplicationResult(pdf,
            BenefitsApplicationService.SuggestedFileName(personId),
            BenefitsApplicationRules.ReviewItems(request));
    }
}
