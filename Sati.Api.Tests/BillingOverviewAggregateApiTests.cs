using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class BillingOverviewAggregateApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task OverviewReturnsSixScopedMonthsAndAllScopedDraftValueInABoundedPayload()
    {
        int[] noteIds = [];
        int[] periodIds = [];
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var nextNoteId = await db.Notes.MaxAsync(note => note.Id) + 1;
                var nextPeriodId = await db.BillingPeriods.MaxAsync(period => period.Id) + 1;
                var facts = new[]
                {
                    new OverviewFact(1, 101, 12, 2026, 5, 2, 23m),
                    new OverviewFact(1, 101, 12, 2026, 3, 0, 37m),
                    new OverviewFact(1, 101, 12, 2026, 10, 0, 43m),
                    new OverviewFact(2, 201, 22, 2026, 9, 0, 999m)
                };

                var notes = facts.Select((fact, index) => new ServerNote
                {
                    Id = nextNoteId + index,
                    PersonId = fact.PersonId,
                    AgencyId = fact.AgencyId,
                    EventDate = new DateTime(fact.Year, fact.Month, 2),
                    Minutes = 15,
                    Status = 6,
                    Narrative = $"Bounded overview narrative {index + 1} must not be returned."
                }).ToArray();
                db.Notes.AddRange(notes);
                await db.SaveChangesAsync();

                var periods = facts.Select((fact, index) => new ServerBillingPeriod
                {
                    Id = nextPeriodId + index,
                    UserId = fact.UserId,
                    Year = fact.Year,
                    Month = fact.Month,
                    Status = fact.Status,
                    Lines =
                    [
                        new ServerClaimLine
                        {
                            NoteId = notes[index].Id,
                            DateOfService = notes[index].EventDate!.Value,
                            ChargeAmount = fact.Charge
                        }
                    ]
                }).ToArray();
                db.BillingPeriods.AddRange(periods);
                await db.SaveChangesAsync();
                noteIds = notes.Select(note => note.Id).ToArray();
                periodIds = periods.Select(period => period.Id).ToArray();
            }

            decimal expectedDraft;
            Dictionary<(int Year, int Month), decimal> expectedMonths;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var scoped = await (from line in db.ClaimLines.AsNoTracking()
                                    join period in db.BillingPeriods.AsNoTracking()
                                        on line.BillingPeriodId equals period.Id
                                    join owner in db.Users.AsNoTracking()
                                        on period.UserId equals owner.Id
                                    where owner.AgencyId == 1
                                    select new
                                    {
                                        period.Year,
                                        period.Month,
                                        period.Status,
                                        line.ChargeAmount
                                    }).ToListAsync();
                expectedDraft = scoped.Where(row => row.Status == 0).Sum(row => row.ChargeAmount);
                expectedMonths = scoped
                    .Where(row => row.Year * 100 + row.Month is >= 202604 and <= 202609)
                    .GroupBy(row => (row.Year, row.Month))
                    .ToDictionary(group => group.Key, group => group.Sum(row => row.ChargeAmount));
            }

            using var biller = await factory.CreateAuthenticatedClientAsync("billing-only-one");
            using var response = await biller.GetAsync("/api/v1/billing/overview-periods/2026/9");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var overview = await response.Content.ReadFromJsonAsync<BillingPeriodOverviewDto>();

            Assert.NotNull(overview);
            Assert.Equal(expectedDraft, overview.DraftRevenue);
            Assert.Equal(
                [202604, 202605, 202606, 202607, 202608, 202609],
                overview.Months.Select(month => month.Year * 100 + month.Month));
            Assert.Equal(
                expectedMonths.GetValueOrDefault((2026, 5)),
                Assert.Single(overview.Months, month => month.Year == 2026 && month.Month == 5).BilledAmount);
            Assert.DoesNotContain(overview.Months, month => month.BilledAmount == 999m);
            Assert.True(json.Length < 1000, $"Overview payload was unexpectedly large ({json.Length:N0} characters).");
            Assert.DoesNotContain("lines", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("claim", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("noteId", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("narrative", json, StringComparison.OrdinalIgnoreCase);

            using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await caseManager.GetAsync("/api/v1/billing/overview-periods/2026/9")).StatusCode);
        }
        finally
        {
            if (periodIds.Length > 0 || noteIds.Length > 0)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                if (periodIds.Length > 0)
                    await db.BillingPeriods.Where(period => periodIds.Contains(period.Id)).ExecuteDeleteAsync();
                if (noteIds.Length > 0)
                    await db.Notes.Where(note => noteIds.Contains(note.Id)).ExecuteDeleteAsync();
            }
        }
    }

    private sealed record OverviewFact(
        int AgencyId,
        int PersonId,
        int UserId,
        int Year,
        int Month,
        int Status,
        decimal Charge);
}
