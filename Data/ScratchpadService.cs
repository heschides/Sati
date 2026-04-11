using Microsoft.EntityFrameworkCore;
using Sati.Models;
using System.Diagnostics;

namespace Sati.Data
{
    public class ScratchpadService : IScratchpadService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public ScratchpadService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<Scratchpad> LoadTodayAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var today = DateTime.Today;
            var scratchpad = await context.Scratchpad
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Date == today);

            if (scratchpad is null)
            {
                scratchpad = new Scratchpad { UserId = userId, Date = today };
                Debug.WriteLine($"SERVICE SaveAsync called with: '{scratchpad.Content}'");
                context.Scratchpad.Add(scratchpad);
                await context.SaveChangesAsync();
            }

            return scratchpad;
        }

        public async Task<List<Scratchpad>> GetHistoryAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var today = DateTime.Today;
            return await context.Scratchpad
                .Where(s => s.UserId == userId && s.Date < today)
                .OrderByDescending(s => s.Date)
                .ToListAsync();
        }

        public async Task SaveAsync(Scratchpad scratchpad)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.Scratchpad.Update(scratchpad);
            await context.SaveChangesAsync();
        }
    }
}