using Sati.Contracts.V1;

namespace Sati.Data;

public interface IRepresentativePayeeService
{
    Task<IReadOnlyList<CheckRequestWorkflowQueueItemDto>> GetSupervisorQueueAsync();
    Task<IReadOnlyList<CheckRequestWorkflowQueueItemDto>> GetFinanceQueueAsync();
    Task<IReadOnlyList<RepresentativePayeeConsumerDto>> GetConsumersAsync();
    Task<RepresentativePayeeWorkspaceDto> GetWorkspaceAsync(int personId);
    Task<CheckRequestWorkflowQueueItemDto> ApplyActionAsync(
        int checkRequestId,
        CheckRequestWorkflowAction action,
        string? note = null);
    Task<RepresentativePayeeLedgerEntryDto> AddLedgerEntryAsync(
        int personId,
        DateTime? entryDate,
        decimal amount,
        string? description);
}
