using Sati.Contracts.V1;

namespace Sati.Data;

public interface ICheckRequestAutomationService
{
    Task<CheckRequestTemplateDto?> GetTemplateAsync(int personId);
    Task<CheckRequestTemplateDto> SaveTemplateAsync(
        int personId,
        SaveCheckRequestTemplateRequest request);
    Task<WeeklyCheckRequestDraftResultDto> EnsureWeeklyDraftsAsync();
    Task<IReadOnlyList<GeneratedCheckRequestDraftDto>> GetPendingDraftsAsync();
    Task<IReadOnlyList<TimeOffCheckRequestCollisionDto>> GetTimeOffCollisionsAsync(DateTime timeOffDate);
    Task<WeeklyCheckRequestDraftResultDto> EnsureTimeOffDraftsAsync(DateTime timeOffDate);
}
