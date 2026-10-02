using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudConsumerScheduleService(CloudApiClient api) : IConsumerScheduleService
{
    public async Task<IReadOnlyList<ConsumerScheduleEntryDto>> GetAsync(int personId) =>
        await api.GetAsync<List<ConsumerScheduleEntryDto>>($"/api/v1/people/{personId}/schedule");

    public Task<ConsumerScheduleEntryDto> SaveAsync(int personId, int? entryId,
        SaveConsumerScheduleEntryRequest request) => entryId is int id
        ? api.PutAsync<SaveConsumerScheduleEntryRequest, ConsumerScheduleEntryDto>(
            $"/api/v1/people/{personId}/schedule/{id}", request)
        : api.PostAsync<SaveConsumerScheduleEntryRequest, ConsumerScheduleEntryDto>(
            $"/api/v1/people/{personId}/schedule", request);

    public Task DeleteAsync(int personId, int entryId, int expectedRevision) =>
        api.DeleteAsync($"/api/v1/people/{personId}/schedule/{entryId}?expectedRevision={expectedRevision}");
}
