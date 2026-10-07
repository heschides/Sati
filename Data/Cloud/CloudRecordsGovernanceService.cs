using Sati.Contracts.V1;
namespace Sati.Data.Cloud;
public sealed class CloudRecordsGovernanceService(CloudApiClient api) : IRecordsGovernanceService
{
    public async Task<IReadOnlyList<GovernanceHoldDto>> GetHoldsAsync(CancellationToken ct = default) =>
        await api.GetAsync<List<GovernanceHoldDto>>("/api/v1/admin/records-governance/holds", ct);
    public Task<GovernanceHoldDto> ChangeHoldAsync(GovernanceHoldRequest request, CancellationToken ct = default) =>
        api.PostAsync<GovernanceHoldRequest, GovernanceHoldDto>("/api/v1/admin/records-governance/holds", request, ct);
    public async Task<IReadOnlyList<RetentionPolicyDto>> GetPoliciesAsync(CancellationToken ct = default) =>
        await api.GetAsync<List<RetentionPolicyDto>>("/api/v1/admin/records-governance/policies", ct);
    public Task<RetentionPolicyDto> SavePolicyAsync(RetentionPolicyRequest request, CancellationToken ct = default) =>
        api.PostAsync<RetentionPolicyRequest, RetentionPolicyDto>("/api/v1/admin/records-governance/policies", request, ct);
    public Task<RetentionPreviewDto> PreviewAsync(long policyId, CancellationToken ct = default) =>
        api.PostAsync<object, RetentionPreviewDto>($"/api/v1/admin/records-governance/policies/{policyId}/preview", new {}, ct);
}
