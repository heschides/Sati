using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Models.Billing;

namespace Sati.Services.Billing
{
    public class BillingService : IBillingService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public BillingService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<BillingPeriod> GetOrCreateBillingPeriodAsync(int userId, int month, int year)
        {
            await using var context = _contextFactory.CreateDbContext();
            var period = await context.BillingPeriods
                .FirstOrDefaultAsync(b => b.UserId == userId
                    && b.Month == month
                    && b.Year == year);

            if (period is not null)
                return period;

            period = new BillingPeriod
            {
                UserId = userId,
                Month = month,
                Year = year,
                Status = BillingStatus.Draft
            };

            context.BillingPeriods.Add(period);
            await context.SaveChangesAsync();
            return period;
        }

        public async Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.BillingPeriods
                .Where(b => b.UserId == userId)
                .Include(b => b.Lines)
                .OrderByDescending(b => b.Year)
                .ThenByDescending(b => b.Month)
                .ToListAsync();
        }

        public async Task<ClaimLine> CreateClaimLineAsync(int noteId)
        {
            await using var context = _contextFactory.CreateDbContext();

            var note = await context.Notes
                .Include(n => n.Person)
                    .ThenInclude(p => p.Agency)
                .FirstOrDefaultAsync(n => n.Id == noteId)
                ?? throw new InvalidOperationException($"Note {noteId} not found.");

            if (note.Person is null)
                throw new InvalidOperationException($"Note {noteId} has no associated person.");

            if (note.EventDate is null)
                throw new InvalidOperationException($"Note {noteId} has no event date.");

            var period = await GetOrCreateBillingPeriodAsync(
                note.Person.UserId,
                note.EventDate.Value.Month,
                note.EventDate.Value.Year);

            var procedureCode = note.Person.Waiver switch
            {
                WaiverType.Section21 => "T2041",
                WaiverType.Section29 => "T2042",
                _ => throw new InvalidOperationException($"Unknown waiver type for person {note.PersonId}.")
            };

            var claimLine = new ClaimLine
            {
                NoteId = noteId,
                BillingPeriodId = period.Id,
                DateOfService = note.EventDate.Value,
                ProcedureCode = procedureCode,
                Units = note.Units,
                ClientMaineCareId = note.Person.MaineCareId ?? string.Empty,
                RenderingProviderNpi = note.Person.Agency?.Npi ?? string.Empty,
                DiagnosisCode = note.Person.DiagnosisCode ?? string.Empty,
                PlaceOfService = (int?)note.Person.PlaceOfService ?? (int)PlaceOfService.Other

            };

            context.ClaimLines.Add(claimLine);
            await context.SaveChangesAsync();
            return claimLine;
        }

        public async Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.ClaimLines
                .Include(c => c.BillingPeriod)
                .Where(c => c.BillingPeriod.UserId == userId
                    && c.BillingPeriod.Status == BillingStatus.Draft)
                .OrderBy(c => c.DateOfService)
                .ToListAsync();
        }

        public async Task SubmitBillingPeriodAsync(int billingPeriodId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var period = await context.BillingPeriods.FindAsync(billingPeriodId)
                ?? throw new InvalidOperationException($"Billing period {billingPeriodId} not found.");

            if (period.Status != BillingStatus.Draft)
                throw new InvalidOperationException("Only draft billing periods can be submitted.");

            period.Status = BillingStatus.Submitted;
            period.SubmittedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
    }
}