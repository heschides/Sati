using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    /// <summary>
    /// Local implementation. A case manager may read and change only their own days: the calendar
    /// passes a user id, and it is checked against the signed-in actor rather than trusted, the
    /// same restriction the API applies.
    /// </summary>
    public class ServiceDayInclusionService(
        IDbContextFactory<SatiContext> contextFactory,
        ISessionService sessionService) : IServiceDayInclusionService
    {
        public async Task<List<ServiceDayInclusion>> GetByYearAsync(int userId, int year)
        {
            EnsureCurrentUser(userId);
            await using var context = contextFactory.CreateDbContext();
            await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
            return await context.ServiceDayInclusions
                .Where(inclusion => inclusion.UserId == userId && inclusion.Date.Year == year)
                .OrderBy(inclusion => inclusion.Date)
                .ToListAsync();
        }

        public async Task<ServiceDayInclusion> SetAsync(int userId, DateTime date, bool isIncluded)
        {
            EnsureCurrentUser(userId);
            await using var context = contextFactory.CreateDbContext();
            await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
            var day = date.Date;
            var existing = await context.ServiceDayInclusions
                .SingleOrDefaultAsync(inclusion => inclusion.UserId == userId && inclusion.Date == day);
            if (existing is not null)
            {
                existing.IsIncluded = isIncluded;
                await context.SaveChangesAsync();
                return existing;
            }

            var inclusion = new ServiceDayInclusion
            {
                UserId = userId,
                Date = day,
                IsIncluded = isIncluded
            };
            context.ServiceDayInclusions.Add(inclusion);
            try
            {
                await context.SaveChangesAsync();
                return inclusion;
            }
            catch (DbUpdateException)
            {
                // Another window may have recorded this same day first. Its row is as valid as
                // this one, so adopt it and apply the choice rather than failing the click.
                await using var retry = contextFactory.CreateDbContext();
                await LocalTenantAccess.EnsureSessionAsync(retry, sessionService);
                var winner = await retry.ServiceDayInclusions
                    .SingleOrDefaultAsync(item => item.UserId == userId && item.Date == day);
                if (winner is null)
                    throw;

                winner.IsIncluded = isIncluded;
                await retry.SaveChangesAsync();
                return winner;
            }
        }

        public async Task ClearAsync(int userId, DateTime date)
        {
            EnsureCurrentUser(userId);
            await using var context = contextFactory.CreateDbContext();
            await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
            var day = date.Date;
            var existing = await context.ServiceDayInclusions
                .SingleOrDefaultAsync(inclusion => inclusion.UserId == userId && inclusion.Date == day);
            if (existing is null)
                return;

            context.ServiceDayInclusions.Remove(existing);
            await context.SaveChangesAsync();
        }


        private void EnsureCurrentUser(int userId)
        {
            if (CurrentActor().Id != userId)
                throw new UnauthorizedAccessException(
                    "You may change counted service days only on your own calendar.");
        }

        private User CurrentActor() => sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in user is required.");
    }
}
