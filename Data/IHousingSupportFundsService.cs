using Sati.Contracts.V1;

namespace Sati.Data;

public interface IHousingSupportFundsService
{
    Task<HousingSupportFundsResult> GenerateAsync(
        int personId,
        HousingSupportFundsRequest request,
        CancellationToken cancellationToken = default);
}
