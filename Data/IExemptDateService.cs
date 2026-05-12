using Sati.Models;

namespace Sati.Data
{
    public interface IExemptDateService
    {
        Task<List<ExemptDate>> GetByYearAsync(int userId, int year);
        Task<ExemptDate> AddAsync(int userId, DateTime date, string? reason = null);
        Task RemoveAsync(int id);
    }
}