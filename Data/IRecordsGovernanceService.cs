using Sati.Contracts.V1;
namespace Sati.Data;
public interface IRecordsGovernanceService
{
    Task<IReadOnlyList<GovernanceHoldDto>> GetHoldsAsync(CancellationToken ct = default);
    Task<GovernanceHoldDto> ChangeHoldAsync(GovernanceHoldRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<RetentionPolicyDto>> GetPoliciesAsync(CancellationToken ct = default);
    Task<RetentionPolicyDto> SavePolicyAsync(RetentionPolicyRequest request, CancellationToken ct = default);
    Task<RetentionPreviewDto> PreviewAsync(long policyId, CancellationToken ct = default);
}
