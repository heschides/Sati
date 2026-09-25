using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Api.Endpoints;
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
    public async Task CaseloadNoteSummaryIsBlobFreeAndUsesTheBusinessDateWindow()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlite(connection)
            .Options;
        using var db = new ApiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var businessDate = new DateTime(2032, 6, 15);
        db.Agencies.Add(new ServerAgency { Id = 7, Name = "Projection agency" });
        db.Users.Add(new ServerUser
        {
            Id = 71, AgencyId = 7, Username = "projection", DisplayName = "Projection",
            PasswordHash = "hash", Salt = "salt", Role = "CaseManager"
        });
        db.People.Add(new ServerPerson
        {
            Id = 701, UserId = 71, AgencyId = 7, FirstName = "Projection",
            LastName = "Consumer", BirthDate = new DateTime(1990, 1, 1),
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.Notes.AddRange(
            Note(701, 7, 0, businessDate, "Today"),
            Note(701, 7, 0, businessDate.AddDays(30), "Last included"),
            Note(701, 7, 0, businessDate.AddDays(-1), "Yesterday"),
            Note(701, 7, 0, businessDate.AddDays(31), "Too far"),
            Note(701, 7, 1, businessDate.AddDays(1), "Not scheduled"));
        await db.SaveChangesAsync();

        var query = ApiEndpoints.CaseloadNoteSummaries(db, [701], 7, businessDate);
        var sql = query
            .ToQueryString();
        var rows = (await query.ToListAsync())
            .OrderBy(row => row.EventDate)
            .ToList();

        Assert.Equal([businessDate, businessDate.AddDays(30)],
            rows.Select(row => row.EventDate!.Value));
        Assert.Contains("\"Id\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"ReleaseObligationId\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Narrative", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VisitDocumentationJson", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CaseManagerJustification", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static ServerNote Note(
        int personId,
        int agencyId,
        int status,
        DateTime eventDate,
        string narrative) => new()
        {
            PersonId = personId,
            AgencyId = agencyId,
            Status = status,
            EventDate = eventDate,
            NoteType = 4,
            Narrative = narrative,
            VisitDocumentationJson = new string('J', 2_000)
        };

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

        var scheduled = Assert.Single(first.Notes);
        Assert.Equal(seed.ScheduledNoteId, scheduled.Id);
        Assert.Equal("Scheduled", scheduled.Status);
        Assert.Equal(DateTime.Today.AddDays(10), scheduled.EventDate);
        Assert.Contains(first.ContactFacts!, fact =>
            fact.EvidenceId == $"note:{seed.ContactNoteId}");

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
        var scheduled = new ServerNote
        {
            PersonId = person.Id,
            AgencyId = 1,
            Status = 0,
            EventDate = DateTime.Today.AddDays(10),
            NoteType = 4,
            Narrative = new string('N', 8_000),
            VisitDocumentationJson = new string('J', 8_000)
        };
        var contact = new ServerNote
        {
            PersonId = person.Id,
            AgencyId = 1,
            Status = 2,
            EventDate = DateTime.Today.AddDays(-10),
            NoteType = 0,
            Narrative = "Historical contact fact"
        };
        db.Notes.AddRange(
            scheduled,
            contact,
            new ServerNote
            {
                PersonId = person.Id, AgencyId = 1, Status = 0,
                EventDate = DateTime.Today.AddDays(-1), NoteType = 4,
                Narrative = "Past scheduled"
            },
            new ServerNote
            {
                PersonId = person.Id, AgencyId = 1, Status = 0,
                EventDate = DateTime.Today.AddDays(31), NoteType = 4,
                Narrative = "Far scheduled"
            },
            new ServerNote
            {
                PersonId = person.Id, AgencyId = 1, Status = 1,
                EventDate = DateTime.Today.AddDays(5), NoteType = 4,
                Narrative = "In-window pending"
            });
        await db.SaveChangesAsync();
        return new GenerationSeed(
            person.Id,
            medicalName,
            agencyName,
            scheduled.Id,
            contact.Id);
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
        string AgencyProviderName,
        int ScheduledNoteId,
        int ContactNoteId);
}
