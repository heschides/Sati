using Sati.Contracts.V1;

namespace Sati.Data;

public interface IConsumerScheduleService
{
    Task<IReadOnlyList<ConsumerScheduleEntryDto>> GetAsync(int personId);
    Task<ConsumerScheduleEntryDto> SaveAsync(int personId, int? entryId,
        SaveConsumerScheduleEntryRequest request);
    Task DeleteAsync(int personId, int entryId, int expectedRevision);
}
