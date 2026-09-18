using Sati.Models;

namespace Sati.Data
{
    /// <summary>
    /// The case manager's own decisions about which of their days the documented daily average
    /// divides by while those days are still open. A day with no decision follows the shared rule
    /// in <c>ProductivityForecast</c>, and every day counts once its documentation window closes.
    /// </summary>
    public interface IServiceDayInclusionService
    {
        /// <summary>
        /// One case manager's decisions. A reviewer who can reach that caseload may read them, so
        /// a settled day marked as having produced nothing billable can be asked about; only the
        /// case manager whose calendar it is may write.
        /// </summary>
        Task<List<ServiceDayInclusion>> GetByYearAsync(int userId, int year);

        /// <summary>Records a decision for one day, replacing any earlier one.</summary>
        Task<ServiceDayInclusion> SetAsync(int userId, DateTime date, bool isIncluded);

        /// <summary>Drops the decision, returning that day to the shared rule.</summary>
        Task ClearAsync(int userId, DateTime date);
    }
}
