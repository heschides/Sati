using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudScheduledNoteMoveService(CloudApiClient api) : IScheduledNoteMoveService
{
    public async Task<IReadOnlyList<ScheduledNoteMoveDto>> GetByYearAsync(int year)
    {
        if (year is < 2000 or > 2200)
            throw new ArgumentOutOfRangeException(nameof(year));
        return await api.GetAsync<List<ScheduledNoteMoveDto>>(
            $"/api/v1/notes/schedule-moves/year/{year}");
    }
}
