using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudRepresentativePayeeService(CloudApiClient api) : IRepresentativePayeeService
{
    public async Task<IReadOnlyList<CheckRequestWorkflowQueueItemDto>> GetSupervisorQueueAsync() =>
        await api.GetAsync<List<CheckRequestWorkflowQueueItemDto>>(
            "/api/v1/check-requests/supervisor-queue");

    public async Task<IReadOnlyList<CheckRequestWorkflowQueueItemDto>> GetFinanceQueueAsync() =>
        await api.GetAsync<List<CheckRequestWorkflowQueueItemDto>>(
            "/api/v1/representative-payee/check-requests");

    public async Task<IReadOnlyList<RepresentativePayeeConsumerDto>> GetConsumersAsync() =>
        await api.GetAsync<List<RepresentativePayeeConsumerDto>>(
            "/api/v1/representative-payee/consumers");

    public async Task<RepresentativePayeeWorkspaceDto> GetWorkspaceAsync(int personId) =>
        await api.GetAsync<RepresentativePayeeWorkspaceDto>(
            $"/api/v1/representative-payee/consumers/{personId}/ledger");

    public async Task<CheckRequestWorkflowQueueItemDto> ApplyActionAsync(
        int checkRequestId,
        CheckRequestWorkflowAction action,
        string? note = null) =>
        await api.PostAsync<ApplyCheckRequestWorkflowActionRequest, CheckRequestWorkflowQueueItemDto>(
            $"/api/v1/check-requests/{checkRequestId}/workflow", new(action, note));

    public async Task<RepresentativePayeeLedgerEntryDto> AddLedgerEntryAsync(
        int personId,
        DateTime? entryDate,
        decimal amount,
        string? description) =>
        await api.PostAsync<AddRepresentativePayeeLedgerEntryRequest, RepresentativePayeeLedgerEntryDto>(
            "/api/v1/representative-payee/ledger-entries",
            new(personId, entryDate, amount, description));
}
