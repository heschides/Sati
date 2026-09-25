using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class BillingPrivacyTests(SatiApiFactory factory)
{
    [Fact]
    public async Task FinanceBillingQueueReturnsMinimumNecessaryServiceFactsOnly()
    {
        using var finance = await factory.CreateAuthenticatedClientAsync("finance-one");
        int noteId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            noteId = await db.Notes.MaxAsync(note => note.Id) + 1;
            db.Notes.Add(new ServerNote
            {
                Id = noteId,
                PersonId = 101,
                AgencyId = 1,
                Narrative = "Synthetic clinical narrative that must not reach Finance",
                VisitDocumentationJson = "{\"private\":\"synthetic visit details\"}",
                OverrideReason = "Synthetic clinical exception detail",
                EventDate = DateTime.Today,
                Minutes = 30,
                Status = 6
            });
            await db.SaveChangesAsync();
        }

        try
        {
            using var response = await finance.GetAsync("/api/v1/billing/candidates");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var candidates = await response.Content.ReadFromJsonAsync<List<BillingCandidateDto>>();
            var candidate = Assert.Single(candidates!, item => item.NoteId == noteId);

            Assert.Equal(101, candidate.PersonId);
            Assert.Equal(12, candidate.PersonOwnerUserId);
            Assert.DoesNotContain("narrative", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("visitDocumentation", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("overrideReason", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("firstName", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("lastName", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var note = await db.Notes.SingleOrDefaultAsync(item => item.Id == noteId);
            if (note is not null)
            {
                db.Notes.Remove(note);
                await db.SaveChangesAsync();
            }
        }
    }
}
