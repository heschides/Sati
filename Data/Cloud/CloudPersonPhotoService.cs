using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudPersonPhotoService(CloudApiClient api) : IPersonPhotoService
{
    public async Task<PersonPhotoDto?> GetAsync(int personId) =>
        (await api.GetAsync<PersonPhotoStateDto>($"/api/v1/people/{personId}/photo")).Photo;

    public Task<PersonPhotoDto> SaveAsync(
        int personId,
        string contentType,
        byte[] content,
        long? expectedRevision) =>
        api.PutAsync<SavePersonPhotoRequest, PersonPhotoDto>(
            $"/api/v1/people/{personId}/photo",
            new SavePersonPhotoRequest(contentType, content, expectedRevision));

    public Task DeleteAsync(int personId, long expectedRevision) =>
        api.DeleteAsync($"/api/v1/people/{personId}/photo?expectedRevision={expectedRevision}");
}
