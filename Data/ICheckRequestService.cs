using Sati.Models;
using Sati.Contracts.V1;

namespace Sati.Data;

public interface ICheckRequestService
{
    Task<List<CheckRequestListItem>> GetAllForPersonAsync(int personId);
    Task<CheckRequest?> GetByIdAsync(int id);
    Task<CheckRequest> CreateDraftAsync(int personId);
    Task<CheckRequest> UpdateAsync(CheckRequest request);
    Task<CheckRequest> PublishAsync(CheckRequest request);
}

public sealed record CheckRequestListItem(
    int Id,
    int Revision,
    DateTime? RequestDate,
    string? PayableTo,
    decimal Amount,
    DateTime? NeededByDate,
    DateTime? PublishedAtUtc,
    CheckRequestWorkflowStatus? StoredWorkflowStatus = null,
    int? TemplateId = null,
    DateTime? ScheduledForDate = null)
{
    public bool IsPublished => PublishedAtUtc is not null;
    public CheckRequestWorkflowStatus WorkflowStatus => StoredWorkflowStatus ??
        (IsPublished ? CheckRequestWorkflowStatus.Prepared : CheckRequestWorkflowStatus.Draft);
    public string Status => CheckRequestWorkflowRules.Describe(WorkflowStatus);
    public bool IsAutomaticallyGenerated => TemplateId is not null && ScheduledForDate is not null;
}
