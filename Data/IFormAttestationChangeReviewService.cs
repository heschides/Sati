using Sati.Contracts.V1;

namespace Sati.Data;

public interface IFormAttestationChangeReviewService
{
    Task<IReadOnlyList<FormAttestationChangeReviewFlagDto>> GetForSupervisorAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FormAttestationChangeReviewFlagDto>> GetForBillingAsync(
        CancellationToken cancellationToken = default);
}
