using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class ReleaseObligationIntegrationTests
{
    private static readonly DateTime Target = new(2027, 3, 7);

    [Fact]
    public void ProviderLinksResolveToRecipientSpecificAssignmentsAndSurfaceMissingWaiverLinkage()
    {
        var resolution = ReleaseAssignmentResolution.Resolve(
            Target,
            observedOn: new DateTime(2026, 12, 10),
            requiresServiceProvider: true,
            [
                new ReleaseProviderLinkFact(17, 70, "Healthcare", "Primary care",
                    new DateTime(2025, 1, 2), null, new DateTime(2025, 1, 2), "Medical One"),
                new ReleaseProviderLinkFact(18, 71, "Healthcare", "Neurology",
                    new DateTime(2025, 5, 2), null, new DateTime(2025, 5, 2), "Medical Two")
            ]);

        Assert.Equal(2, resolution.Assignments.Count);
        Assert.All(resolution.Assignments,
            fact => Assert.Equal(ReleaseAssignmentKind.MedicalProvider, fact.Kind));
        Assert.Contains(resolution.Issues,
            issue => issue.Code == ReleaseLinkageIssueCodes.MissingServiceProvider);
    }

    [Fact]
    public async Task PersistenceKeepsIndependentRecipientAttestationsAndProspectiveWithdrawal()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
        int personId;
        await using (var setup = new SatiContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Agencies.Add(new Agency { Id = 3, Name = "Synthetic Agency" });
            var user = User.Create(
                12, "release-cm", "Release CM", "", "", UserRole.CaseManager, null, 3);
            setup.Users.Add(user);
            var person = Person.CreatePerson(
                user.Id,
                "Release",
                "Test",
                string.Empty,
                new DateTime(1990, 1, 1),
                Target.AddYears(-1),
                WaiverType.None,
                new Settings());
            person.AgencyId = 3;
            setup.People.Add(person);
            setup.Providers.AddRange(
                new Provider
                {
                    Id = 70,
                    AgencyId = 3,
                    Type = ProviderType.Healthcare,
                    MedicalKind = MedicalProviderKind.Individual,
                    Name = "Medical One"
                },
                new Provider
                {
                    Id = 71,
                    AgencyId = 3,
                    Type = ProviderType.Healthcare,
                    MedicalKind = MedicalProviderKind.Individual,
                    Name = "Medical Two"
                });
            await setup.SaveChangesAsync();
            personId = person.Id;
        }

        var medicalPlans = ReleaseObligationRules.GenerateCycle(Target,
        [
            new ReleaseAssignmentFact("person-provider:17", ReleaseAssignmentKind.MedicalProvider,
                new DateTime(2025, 1, 2), null, new DateTime(2025, 1, 2)),
            new ReleaseAssignmentFact("person-provider:18", ReleaseAssignmentKind.MedicalProvider,
                new DateTime(2025, 5, 2), null, new DateTime(2025, 5, 2))
        ]).Where(plan => plan.Category == ReleaseObligationCategory.Medical).ToArray();

        var first = ReleaseObligation.Create(3, personId, medicalPlans[0],
            new DateTime(2026, 12, 7, 12, 0, 0, DateTimeKind.Utc), 70, "Medical One");
        var second = ReleaseObligation.Create(3, personId, medicalPlans[1],
            new DateTime(2026, 12, 7, 12, 0, 0, DateTimeKind.Utc), 71, "Medical Two");
        first.AttestManually(new DateTime(2027, 3, 1), new DateTime(2027, 3, 2),
            AttestationActorKind.CaseManager, 12,
            new DateTime(2027, 3, 2, 14, 0, 0, DateTimeKind.Utc));
        first.Withdraw(new DateTime(2027, 6, 1), new DateTime(2027, 6, 1), 12,
            new DateTime(2027, 6, 1, 14, 0, 0, DateTimeKind.Utc), "Consumer withdrew.");

        await using (var db = new SatiContext(options))
        {
            db.ReleaseObligations.AddRange(first, second);
            await db.SaveChangesAsync();
        }

        await using var verify = new SatiContext(options);
        var rows = await verify.ReleaseObligations
            .Include(row => row.Attestations)
            .Include(row => row.AuthorizationEvents)
            .OrderBy(row => row.StableKey)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows.Single(row => row.CompletedOn is not null).Attestations);
        Assert.Single(rows.Single(row => row.CompletedOn is not null).AuthorizationEvents);
        Assert.Null(rows.Single(row => row.CompletedOn is null).CompletedOn);
    }

    [Fact]
    public async Task LocalServiceReconcilesEachRecipientAndKeepsWithdrawalSeparateFromCompletion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        User actor;
        int personId;
        var target = new DateTime(2026, 3, 7);
        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Agencies.Add(new Agency { Id = 30, Name = "Release Integration Agency" });
            actor = User.Create(
                120, "release-integration-cm", "Release Integration CM", "", "",
                UserRole.CaseManager, null, 30);
            setup.Users.Add(actor);
            var person = Person.CreatePerson(
                actor.Id,
                "Synthetic",
                "Release Consumer",
                string.Empty,
                new DateTime(1990, 1, 1),
                target,
                WaiverType.Section21,
                new Settings());
            person.AgencyId = 30;
            setup.People.Add(person);
            setup.Providers.AddRange(
                new Provider { Id = 701, AgencyId = 30, Type = ProviderType.Healthcare, Name = "Medical A" },
                new Provider { Id = 702, AgencyId = 30, Type = ProviderType.Healthcare, Name = "Medical B" },
                new Provider { Id = 703, AgencyId = 30, Type = ProviderType.Waiver, Name = "Service Agency" });
            await setup.SaveChangesAsync();
            personId = person.Id;
            setup.PersonProviders.AddRange(
                Link(personId, 701, target.AddYears(-1)),
                Link(personId, 702, target.AddMonths(-8)),
                Link(personId, 703, target.AddYears(-2)));
            await setup.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SetUser(actor);
        var service = new ReleaseObligationService(factory, session);
        var status = await service.ReconcileAsync(personId, target);

        Assert.Equal(4, status.Obligations.Count);
        Assert.Equal(2, status.Obligations.Count(item => item.Category == "Medical"));
        Assert.Single(status.Obligations.Where(item => item.Category == "Agency"));
        Assert.Single(status.Obligations.Where(item => item.Category == "Dhhs"));
        Assert.Empty(status.LinkageIssues);

        var firstMedical = status.Obligations.First(item => item.Category == "Medical");
        var completed = await service.AttestAsync(
            personId, firstMedical.ObligationId, target.AddDays(-1));
        var withdrawn = await service.WithdrawAsync(
            personId, firstMedical.ObligationId, target.AddMonths(2), "Consumer withdrew.");

        Assert.Equal(target.AddDays(-1), completed.CompletedOn);
        Assert.Equal(completed.CompletedOn, withdrawn.CompletedOn);
        Assert.Equal(target.AddMonths(2), withdrawn.WithdrawnOn);
        Assert.False(withdrawn.IsAuthorizationActive);
        Assert.Null((await service.GetStatusAsync(personId, target)).Obligations
            .Single(item => item.Category == "Medical" &&
                            item.ObligationId != firstMedical.ObligationId).CompletedOn);
    }

    [Fact]
    public async Task LocalStatusSurfacesLegacyCompletionWithoutCompletingTheExactDhhsRelease()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        User actor;
        int personId;
        var target = new DateTime(2026, 3, 7);
        var legacyCompletedOn = target.AddDays(-3);
        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Agencies.Add(new Agency { Id = 31, Name = "Legacy Release Review Agency" });
            actor = User.Create(
                121, "legacy-release-cm", "Legacy Release CM", "", "",
                UserRole.CaseManager, null, 31);
            setup.Users.Add(actor);
            var person = Person.CreatePerson(
                actor.Id,
                "Legacy",
                "Release Consumer",
                string.Empty,
                new DateTime(1990, 1, 1),
                target,
                WaiverType.None,
                new Settings());
            person.AgencyId = 31;
            person.Forms.Add(new Form(
                FormType.Release_DHHS,
                target,
                legacyCompletedOn,
                target));
            setup.People.Add(person);
            await setup.SaveChangesAsync();
            personId = person.Id;
        }

        var session = new SessionService();
        session.SetUser(actor);
        var service = new ReleaseObligationService(factory, session);
        var reconciled = await service.ReconcileAsync(personId, target);

        var warning = Assert.Single(reconciled.LinkageIssues,
            issue => issue.Code == ReleaseLinkageIssueCodes.LegacyCategoryCompletionNeedsReview);
        Assert.Contains("did not copy its date", warning.Message);
        var dhhs = Assert.Single(reconciled.Obligations,
            item => item.Category == nameof(ReleaseObligationCategory.Dhhs));
        Assert.Null(dhhs.CompletedOn);

        await service.AttestAsync(personId, dhhs.ObligationId, legacyCompletedOn);
        var reviewed = await service.GetStatusAsync(personId, target);
        Assert.DoesNotContain(reviewed.LinkageIssues,
            issue => issue.Code == ReleaseLinkageIssueCodes.LegacyCategoryCompletionNeedsReview);
    }

    private static PersonProvider Link(int personId, int providerId, DateTime startsOn) => new()
    {
        PersonId = personId,
        ProviderId = providerId,
        StartDate = startsOn,
        AssignmentKnownOn = startsOn,
        Role = "Synthetic assignment"
    };

    private sealed class TestContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);

        public Task<SatiContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
