using Sati.Contracts.V1;

namespace Sati.Data;

public interface IBenefitsApplicationService
{
    Task<BenefitsApplicationResult> GenerateAsync(int personId,
        BenefitsApplicationRequest request, CancellationToken cancellationToken = default);
}
