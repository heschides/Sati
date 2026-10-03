using Sati.Contracts.V1;

namespace Sati.Data;

/// <summary>Read the signed-in case manager's still-relevant prior scheduled dates.</summary>
public interface IScheduledNoteMoveService
{
    Task<IReadOnlyList<ScheduledNoteMoveDto>> GetByYearAsync(int year);
}
