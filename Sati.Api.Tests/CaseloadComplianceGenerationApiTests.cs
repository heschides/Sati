using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[CollectionDefinition(CollectionName)]
public sealed class CaseloadComplianceGenerationApiCollection
    : ICollectionFixture<SatiApiFactory>
{
    public const string CollectionName = "Caseload compliance generation API integration";
}

[Collection(CaseloadComplianceGenerationApiCollection.CollectionName)]
public sealed class CaseloadComplianceGenerationApiTests(SatiApiFactory factory)
{
    private static readonly string[] ExpectedFormTypes =
    [
        "Q1R", "Q2R", "Q3R", "Q4R", "PCP", "ComprehensiveAssessment",
        "Reclassification", "SafetyPlan", "PrivacyPractices"
    ];

    [Fact]
    public async Task CaseloadLoadCreatesOutstandingAnnualGraphOnceAndReturnsReleaseFacts()
    {
        var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var effectiveOn = DateTime.Today.AddMonths(-1).Date;
        var seed = await AddConsumerWithoutComplianceGraphAsync(effectiveOn);
        var expectedTargets = new[] { effectiveOn, effectiveOn.AddYears(1) };

        var simultaneousLoads = await Task.WhenAll(
            LoadConsumerAsync(client, seed.PersonId),
            LoadConsumerAsync(client, seed.PersonId));
        var first = simultaneousLoads[0];

        Assert.Equal(ExpectedFormTypes.Length * expectedTargets.Length, first.Forms.Count);
        Assert.All(first.Forms, form =>
        {
            Assert.Null(form.CompletedDate);
            Assert.Null(form.OpenedDate);
            Assert.False(form.IsCompliant);
            Assert.Contains(form.TargetEffectiveDate, expectedTargets.Cast<DateTime?>());
        });
        Assert.Equal(
            first.Forms.Count,
            first.Forms.Select(form => (form.Type, form.TargetEffectiveDate)).Distinct().Count());
        Assert.All(expectedTargets, target =>
            Assert.Equal(
                ExpectedFormTypes.Order(StringComparer.Ordinal),
                first.Forms.Where(form => form.TargetEffectiveDate == target)
                    .Select(form => form.Type)
                    .Order(StringComparer.Ordinal)));

        var currentPcp = first.Forms.Single(form =>
            form.Type == "PCP" && form.TargetEffectiveDate == effectiveOn);
        var currentAssessment = first.Forms.Single(form =>
            form.Type == "ComprehensiveAssessment" &&
            form.TargetEffectiveDate == effectiveOn);
        var currentReclassification = first.Forms.Single(form =>
            form.Type == "Reclassification" &&
            form.TargetEffectiveDate == effectiveOn);
        Assert.Equal(effectiveOn, currentPcp.DueDate);
        Assert.Equal(effectiveOn.AddDays(-90), currentAssessment.DueDate);
        Assert.Equal(effectiveOn.AddDays(-30), currentReclassification.DueDate);

        var firstReleases = Assert.IsAssignableFrom<IReadOnlyList<ReleaseComplianceFact>>(
            first.ReleaseObligations);
        Assert.Equal(3 * expectedTargets.Length, firstReleases.Count);
        Assert.All(firstReleases, release =>
        {
            Assert.Empty(release.Attestations);
            Assert.NotNull(release.ObligationId);
            Assert.Contains(release.TargetEffectiveDate, expectedTargets.Cast<DateTime?>());
        });
        Assert.All(expectedTargets, target =>
            Assert.Equal(
                Enum.GetValues<ReleaseObligationCategory>().Order(),
                firstReleases.Where(release => release.TargetEffectiveDate == target)
                    .Select(release => release.Category)
                    .Order()));
        Assert.All(firstReleases.Where(release =>
                release.Category == ReleaseObligationCategory.Medical),
            release => Assert.Equal(seed.MedicalProviderName, release.RecipientDisplayName));
        Assert.All(firstReleases.Where(release =>
                release.Category == ReleaseObligationCategory.Agency),
            release => Assert.Equal(seed.AgencyProviderName, release.RecipientDisplayName));
        Assert.All(firstReleases.Where(release =>
                release.Category == ReleaseObligationCategory.Dhhs),
            release => Assert.Null(release.RecipientDisplayName));

        var second = await LoadConsumerAsync(client, seed.PersonId);
        Assert.Equal(
            first.Forms.Select(form => (form.Type, form.TargetEffectiveDate, form.DueDate))
                .OrderBy(item => item.Type).ThenBy(item => item.TargetEffectiveDate),
            simultaneousLoads[1].Forms
                .Select(form => (form.Type, form.TargetEffectiveDate, form.DueDate))
                .OrderBy(item => item.Type).ThenBy(item => item.TargetEffectiveDate));
        Assert.Equal(
            first.Forms.Select(form => (form.Type, form.TargetEffectiveDate, form.DueDate))
                .OrderBy(item => item.Type).ThenBy(item => item.TargetEffectiveDate),
            second.Forms.Select(form => (form.Type, form.TargetEffectiveDate, form.DueDate))
                .OrderBy(item => item.Type).ThenBy(item => item.TargetEffectiveDate));
        Assert.Equal(
            firstReleases.Select(release => (release.StableKey, release.ObligationId))
                .OrderBy(item => item.StableKey),
            second.ReleaseObligations!.Select(release =>
                    (release.StableKey, release.ObligationId))
                .OrderBy(item => item.StableKey));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Equal(
            ExpectedFormTypes.Length * expectedTargets.Length,
            await db.Forms.CountAsync(form => form.PersonId == seed.PersonId));
        Assert.Equal(
            3 * expectedTargets.Length,
            await db.ReleaseObligations.CountAsync(release =>
                release.PersonId == seed.PersonId));
    }

    private async Task<GenerationSeed> AddConsumerWithoutComplianceGraphAsync(
        DateTime effectiveOn)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var medicalName = $"Synthetic medical {suffix}";
        var agencyName = $"Synthetic waiver {suffix}";
        var person = new ServerPerson
        {
            UserId = 12,
            AgencyId = 1,
            FirstName = "Generation",
            LastName = suffix,
            BirthDate = new DateTime(1990, 1, 1),
            EffectiveDate = effectiveOn,
            Waiver = 1,
            CreatedAtUtc = DateTime.UtcNow,
            Status = 0
        };
        var medical = new ServerProvider
        {
            AgencyId = 1,
            Type = "Healthcare",
            MedicalKind = "Individual",
            Name = medicalName
        };
        var agency = new ServerProvider
        {
            AgencyId = 1,
            Type = "Waiver",
            Name = agencyName
        };
        if (!await db.Settings.AnyAsync(settings => settings.AgencyId == 1))
            db.Settings.Add(new ServerSettings { AgencyId = 1 });
        db.People.Add(person);
        db.Providers.AddRange(medical, agency);
        await db.SaveChangesAsync();

        var assignmentStart = effectiveOn.AddMonths(-1);
        db.PersonProviders.AddRange(
            new ServerPersonProvider
            {
                PersonId = person.Id,
                ProviderId = medical.Id,
                Role = "Primary care",
                StartDate = assignmentStart,
                AssignmentKnownOn = assignmentStart
            },
            new ServerPersonProvider
            {
                PersonId = person.Id,
                ProviderId = agency.Id,
                Role = "Waiver service",
                StartDate = assignmentStart,
                AssignmentKnownOn = assignmentStart
            });
        await db.SaveChangesAsync();
        return new GenerationSeed(person.Id, medicalName, agencyName);
    }

    private static async Task<PersonDto> LoadConsumerAsync(
        HttpClient client,
        int personId)
    {
        using var response = await client.GetAsync("/api/v1/caseload?userId=12");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Caseload returned {(int)response.StatusCode}: {body}");
        var people = await response.Content.ReadFromJsonAsync<List<PersonDto>>()
                     ?? throw new InvalidOperationException(
                         "The caseload endpoint returned no response body.");
        return Assert.Single(people, person => person.Id == personId);
    }

    private sealed record GenerationSeed(
        int PersonId,
        string MedicalProviderName,
        string AgencyProviderName);
}
