using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class ExemptDateService : IExemptDateService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public ExemptDateService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<List<ExemptDate>> GetByYearAsync(int userId, int year)
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.ExemptDates
                .Where(e => e.UserId == userId && e.Date.Year == year)
                .OrderBy(e => e.Date)
                .ToListAsync();
        }

        public async Task<ExemptDate> AddAsync(int userId, DateTime date, string? reason = null)
        {
            await using var context = _contextFactory.CreateDbContext();
            var exemptDate = new ExemptDate
            {
                UserId = userId,
                Date = date.Date, // strip time component
                Reason = reason
            };
            context.ExemptDates.Add(exemptDate);
            await context.SaveChangesAsync();
            return exemptDate;
        }

        public async Task RemoveAsync(int id)
        {
            await using var context = _contextFactory.CreateDbContext();
            var exemptDate = await context.ExemptDates.FindAsync(id);
            if (exemptDate is null) return;
            context.ExemptDates.Remove(exemptDate);
            await context.SaveChangesAsync();
        }
    }
}