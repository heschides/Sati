using System.Globalization;
using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudStatisticsBreakdownService(CloudApiClient api) : IStatisticsBreakdownService
{
    public Task<StatisticsBreakdownReportDto> GetAsync(
        DateTime windowStart,
        DateTime windowEnd,
        StatisticsPeriod period,
        int? personId = null)
    {
        StatisticsBreakdownBuilder.ValidateWindow(windowStart, windowEnd, period);
        if (personId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(personId));

        var start = windowStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = windowEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var url = $"/api/v1/reports/statistics-breakdown?start={start}&end={end}&period={period}";
        if (personId is int selectedPersonId)
            url += $"&personId={selectedPersonId}";
        return api.GetAsync<StatisticsBreakdownReportDto>(url);
    }
}
