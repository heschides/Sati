using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Endpoints;
using Xunit;

namespace Sati.Api.Tests;

public sealed class BillingCandidateProjectionTests
{
    [Fact]
    public void BaseQuerySelectsBillingFactsWithoutClinicalOrEncryptedPayloads()
    {
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new ApiDbContext(options);

        var sql = ApiEndpoints.BillingCandidateBaseQuery(db, agencyId: 1)
            .ToQueryString();

        Assert.Contains("EventDate", sql, StringComparison.Ordinal);
        Assert.Contains("Minutes", sql, StringComparison.Ordinal);
        Assert.Contains("MaineCareId", sql, StringComparison.Ordinal);
        foreach (var forbiddenColumn in new[]
                 {
                     "Narrative",
                     "VisitDocumentationJson",
                     "Bio",
                     "Journal",
                     "SsnCiphertext",
                     "SsnNonce",
                     "SsnTag",
                     "SsnWrappedKey",
                     "SsnKeyId",
                     "SsnLastFour"
                 })
        {
            Assert.DoesNotContain(forbiddenColumn, sql, StringComparison.Ordinal);
        }
    }
}
