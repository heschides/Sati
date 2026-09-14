using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudCheckRequestAutomationService(CloudApiClient api)
    : ICheckRequestAutomationService
{
    public Task<CheckRequestTemplateDto?> GetTemplateAsync(int personId) =>
        api.GetAsync<CheckRequestTemplateDto?>($"/api/v1/people/{personId}/check-request-template");

    public Task<CheckRequestTemplateDto> SaveTemplateAsync(
        int personId,
        SaveCheckRequestTemplateRequest request) =>
        api.PutAsync<SaveCheckRequestTemplateRequest, CheckRequestTemplateDto>(
            $"/api/v1/people/{personId}/check-request-template", request);

    public Task<WeeklyCheckRequestDraftResultDto> EnsureWeeklyDraftsAsync() =>
        api.PostAsync<object, WeeklyCheckRequestDraftResultDto>(
            "/api/v1/check-requests/weekly-drafts/ensure", new { });

    public Task<IReadOnlyList<GeneratedCheckRequestDraftDto>> GetPendingDraftsAsync() =>
        GetPendingCoreAsync();

    private async Task<IReadOnlyList<GeneratedCheckRequestDraftDto>> GetPendingCoreAsync() =>
        await api.GetAsync<List<GeneratedCheckRequestDraftDto>>(
            "/api/v1/check-requests/generated-drafts/pending");

    public async Task<IReadOnlyList<TimeOffCheckRequestCollisionDto>> GetTimeOffCollisionsAsync(
        DateTime timeOffDate) =>
        await api.GetAsync<List<TimeOffCheckRequestCollisionDto>>(
            $"/api/v1/check-requests/time-off-collisions?date={timeOffDate:yyyy-MM-dd}");

    public Task<WeeklyCheckRequestDraftResultDto> EnsureTimeOffDraftsAsync(DateTime timeOffDate) =>
        api.PostAsync<EnsureTimeOffCheckRequestDraftsRequest, WeeklyCheckRequestDraftResultDto>(
            "/api/v1/check-requests/time-off-drafts/ensure",
            new EnsureTimeOffCheckRequestDraftsRequest(timeOffDate.Date));
}
