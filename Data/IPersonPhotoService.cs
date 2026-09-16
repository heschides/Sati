using Sati.Contracts.V1;

namespace Sati.Data;

public interface IPersonPhotoService
{
    Task<PersonPhotoDto?> GetAsync(int personId);
    Task<PersonPhotoDto> SaveAsync(
        int personId,
        string contentType,
        byte[] content,
        long? expectedRevision);
    Task DeleteAsync(int personId, long expectedRevision);
}
