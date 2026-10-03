using Sati.Contracts.V1;

namespace Sati.Data;

public interface IStatisticsBreakdownService
{
    Task<StatisticsBreakdownReportDto> GetAsync(
        DateTime windowStart,
        DateTime windowEnd,
        StatisticsPeriod period,
        int? personId = null);
}
