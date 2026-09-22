using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudFormAttestationChangeReviewService(CloudApiClient api)
    : IFormAttestationChangeReviewService
{
    public async Task<IReadOnlyList<FormAttestationChangeReviewFlagDto>> GetForSupervisorAsync(
        CancellationToken cancellationToken = default) =>
        await api.GetAsync<List<FormAttestationChangeReviewFlagDto>>(
            "/api/v1/form-attestation-change-review-flags?audience=supervisor",
            cancellationToken);

    public async Task<IReadOnlyList<FormAttestationChangeReviewFlagDto>> GetForBillingAsync(
        CancellationToken cancellationToken = default) =>
        await api.GetAsync<List<FormAttestationChangeReviewFlagDto>>(
            "/api/v1/form-attestation-change-review-flags?audience=billing",
            cancellationToken);
}
