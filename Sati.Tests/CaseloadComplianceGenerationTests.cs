using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class CaseloadComplianceGenerationTests
{
    [Fact]
    public async Task LocalCaseloadProjectsNoteFactsWithoutClinicalBlobs()
    {
        await using var connection = new SqliteConnection(
            "Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var commands = new RecordingCommandInterceptor();
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlite(connection)
            .AddInterceptors(commands)
            .Options;
        var factory = new TestContextFactory(options);
        var actor = User.Create(
            902,
            "summary-case-manager",
            "Summary Case Manager",
            "hash",
            "salt",
            UserRole.CaseManager,
            null,
            90);
        int scheduledNoteId;
        int contactNoteId;

        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Agencies.Add(new Agency { Id = 90, Name = "Summary Test Agency" });
            setup.Users.Add(actor);
            var person = Person.Rehydrate(0, actor.Id, DateTime.UtcNow);
            person.AgencyId = 90;
            person.FirstName = "Blob";
            person.LastName = "Free";
            person.BirthDate = new DateTime(1990, 1, 1);
            setup.People.Add(person);
            await setup.SaveChangesAsync();

            var note = Note.Create(
                new string('N', 8_000),
                DateTime.Today.AddDays(10),
                NoteStatus.Scheduled,
                60,
                person.Id,
                noteType: NoteType.Visit);
            note.AgencyId = 90;
            note.VisitDocumentationJson = new string('J', 8_000);
            setup.Notes.Add(note);
            var contact = Note.Create(
                "Historical contact fact",
                DateTime.Today.AddDays(-10),
                NoteStatus.Logged,
                60,
                person.Id,
                noteType: NoteType.Visit);
            contact.AgencyId = 90;
            setup.Notes.Add(contact);
            foreach (var excluded in new[]
                     {
                         Note.Create("Past scheduled", DateTime.Today.AddDays(-1),
                             NoteStatus.Scheduled, 15, person.Id, noteType: NoteType.Reminder),
                         Note.Create("Far scheduled", DateTime.Today.AddDays(31),
                             NoteStatus.Scheduled, 15, person.Id, noteType: NoteType.Reminder),
                         Note.Create("In-window but pending", DateTime.Today.AddDays(5),
                             NoteStatus.Pending, 15, person.Id, noteType: NoteType.Reminder)
                     })
            {
                excluded.AgencyId = 90;
                setup.Notes.Add(excluded);
            }
            await setup.SaveChangesAsync();
            scheduledNoteId = note.Id;
            contactNoteId = contact.Id;
        }

        commands.Commands.Clear();
        var session = new SessionService();
        session.SetUser(actor);
        var service = new PersonService(factory, new FixedSettingsService(), session);

        var loaded = Assert.Single(await service.GetAllPeopleAsync(actor.Id));

        var summary = Assert.Single(loaded.Notes);
        Assert.Equal(scheduledNoteId, summary.Id);
        Assert.Equal(NoteStatus.Scheduled, summary.Status);
        Assert.Equal(DateTime.Today.AddDays(10), summary.EventDate);
        Assert.Equal(string.Empty, summary.Narrative);
        Assert.Contains(loaded.ContactFactsForCompliance!, fact =>
            fact.EvidenceId == $"note:{contactNoteId}");

        var sidebar = Assert.Single(await service.GetPeopleForSummaryAsync(actor.Id));
        var sidebarNote = Assert.Single(sidebar.NoteSummaries);
        Assert.Equal(scheduledNoteId, sidebarNote.Id);
        Assert.Equal(NoteStatus.Scheduled, sidebarNote.Status);
        Assert.Equal(DateTime.Today.AddDays(10), sidebarNote.EventDate);
        var noteSelects = commands.Commands
            .Where(command => command.Contains("FROM \"Notes\"", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(noteSelects);
        Assert.All(noteSelects, command =>
        {
            Assert.DoesNotContain("Narrative", command, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("VisitDocumentationJson", command, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task LocalCaseloadLoadCreatesCurrentAndNextFormsAndReleasesOnlyOnce()
    {
        await using var connection = new SqliteConnection(
            "Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlite(connection)
            .Options;
        var factory = new TestContextFactory(options);
        var effectiveOn = DateTime.Today.AddMonths(-1).Date;
        var expectedTargets = new[] { effectiveOn, effectiveOn.AddYears(1) };
        var actor = User.Create(
            812,
            "generation-case-manager",
            "Generation Case Manager",
            "hash",
            "salt",
            UserRole.CaseManager,
            null,
            81);
        int personId;
        int existingNoteId;

        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Agencies.Add(new Agency { Id = 81, Name = "Generation Test Agency" });
            setup.Users.Add(actor);
            var person = Person.Rehydrate(0, actor.Id, DateTime.UtcNow);
            person.AgencyId = 81;
            person.FirstName = "Annual";
            person.LastName = "Generation";
            person.BirthDate = new DateTime(1990, 1, 1);
            person.EffectiveDate = effectiveOn;
            person.Waiver = WaiverType.Section21;
            var medical = new Provider
            {
                AgencyId = 81,
                Type = ProviderType.Healthcare,
                MedicalKind = MedicalProviderKind.Individual,
                Name = "Generation Medical"
            };
            var agency = new Provider
            {
                AgencyId = 81,
                Type = ProviderType.Waiver,
                Name = "Generation Waiver"
            };
            setup.People.Add(person);
            setup.Providers.AddRange(medical, agency);
            await setup.SaveChangesAsync();
            personId = person.Id;

            var assignmentStart = effectiveOn.AddMonths(-1);
            setup.PersonProviders.AddRange(
                new PersonProvider
                {
                    PersonId = personId,
                    ProviderId = medical.Id,
                    Role = "Primary care",
                    StartDate = assignmentStart,
                    AssignmentKnownOn = assignmentStart
                },
                new PersonProvider
                {
                    PersonId = personId,
                    ProviderId = agency.Id,
                    Role = "Waiver service",
                    StartDate = assignmentStart,
                    AssignmentKnownOn = assignmentStart
                });
            var existingNote = Note.Create(
                "Existing clinical narrative must not be reinserted.",
                DateTime.Today.AddDays(1),
                NoteStatus.Scheduled,
                60,
                personId,
                noteType: NoteType.Visit);
            existingNote.AgencyId = 81;
            setup.Notes.Add(existingNote);
            await setup.SaveChangesAsync();
            existingNoteId = existingNote.Id;
        }

        var session = new SessionService();
        session.SetUser(actor);
        var service = new PersonService(factory, new FixedSettingsService(), session);

        var first = Assert.Single(await service.GetAllPeopleAsync(actor.Id));
        Assert.Equal(existingNoteId, Assert.Single(first.Notes).Id);
        Assert.Equal(PersonSaveRules.FormTypes.Count * expectedTargets.Length, first.Forms.Count);
        Assert.All(first.Forms, form =>
        {
            Assert.Null(form.CompletedDate);
            Assert.Null(form.OpenedDate);
            Assert.Contains(form.TargetEffectiveDate.Date, expectedTargets);
        });
        Assert.Equal(
            first.Forms.Count,
            first.Forms.Select(form => (form.Type, form.TargetEffectiveDate.Date))
                .Distinct().Count());
        Assert.Equal(3 * expectedTargets.Length, first.ReleaseObligations.Count);
        Assert.All(first.ReleaseObligations, release =>
        {
            Assert.Empty(release.Attestations);
            Assert.Contains(release.TargetEffectiveDate, expectedTargets);
        });

        var firstFormIdentities = first.Forms
            .Select(form => (form.Type, Target: form.TargetEffectiveDate.Date, form.DueDate))
            .OrderBy(item => item.Type).ThenBy(item => item.Target)
            .ToArray();
        var firstReleaseIdentities = first.ReleaseObligations
            .Select(release => (release.StableKey, release.ObligationId))
            .OrderBy(item => item.StableKey)
            .ToArray();

        var second = Assert.Single(await service.GetAllPeopleAsync(actor.Id));
        Assert.Equal(firstFormIdentities, second.Forms
            .Select(form => (form.Type, Target: form.TargetEffectiveDate.Date, form.DueDate))
            .OrderBy(item => item.Type).ThenBy(item => item.Target));
        Assert.Equal(firstReleaseIdentities, second.ReleaseObligations
            .Select(release => (release.StableKey, release.ObligationId))
            .OrderBy(item => item.StableKey));

        await using var verify = factory.CreateDbContext();
        Assert.Equal(PersonSaveRules.FormTypes.Count * expectedTargets.Length,
            await verify.Forms.CountAsync(form => form.PersonId == personId));
        Assert.Equal(3 * expectedTargets.Length,
            await verify.ReleaseObligations.CountAsync(release =>
                release.PersonId == personId));
        Assert.Equal(1, await verify.Notes.CountAsync(note => note.PersonId == personId));
    }

    [Fact]
    public void CloudMappingCarriesReleaseFactsToFullAndSidebarModels()
    {
        var target = new DateTime(2030, 3, 7);
        var obligationId = Guid.NewGuid();
        var release = new ReleaseComplianceFact(
            ReleaseObligationRules.StableKey(
                target,
                ReleaseObligationCategory.Dhhs,
                ReleaseObligationTrigger.AnnualRenewal,
                assignmentKey: null),
            ReleaseObligationCategory.Dhhs,
            target,
            target,
            RetiredOn: null,
            Attestations: [],
            obligationId,
            target,
            target.AddDays(-90));
        var dto = PersonDtoWithRelease(target, release);

        var person = CloudContractMapper.ToPerson(dto);
        var summary = CloudContractMapper.ToPersonSummary(dto);

        Assert.Equal(release, Assert.Single(person.ReleaseComplianceSnapshots));
        Assert.Equal(release, Assert.Single(summary.ReleaseComplianceSnapshots));
        var billing = person.EvaluateBillingWindowDetailed(
            target.AddDays(1),
            BillingComplianceRequirements.DhhsRelease);
        Assert.False(billing.Passed);
        Assert.Equal(
            $"release:{obligationId:D}",
            Assert.Single(billing.Blockers!).ObligationId);

        var sidebarEvents = new UpcomingEventService().GenerateEvents(
            [summary],
            new Settings(),
            target);
        var releaseEvent = Assert.Single(sidebarEvents,
            item => item.FormType == FormType.Release_DHHS);
        Assert.Equal(target, releaseEvent.Date);
        Assert.Contains("DHHS release", releaseEvent.Title, StringComparison.Ordinal);
    }

    private static PersonDto PersonDtoWithRelease(
        DateTime effectiveOn,
        ReleaseComplianceFact release) =>
        new(
            Id: 77,
            UserId: 12,
            FirstName: "Mapped",
            LastName: "Consumer",
            BirthDate: new DateTime(1990, 1, 1),
            Gender: "Unknown",
            EffectiveDate: effectiveOn,
            Bio: null,
            Waiver: "None",
            AgencyId: 1,
            MaineCareId: null,
            DiagnosisCode: null,
            PlaceOfService: null,
            EvergreenId: null,
            OpenWithVR: false,
            HasGuardian: false,
            GuardianName: null,
            PhoneNumber: null,
            Address: null,
            BillingStreet: null,
            BillingCity: null,
            BillingState: null,
            BillingZip: null,
            PrimaryCareProvider: null,
            HealthcareSystemName: null,
            HasHomeSupport: false,
            HasSelfDirectedHomeSupport: false,
            HasSharedLiving: false,
            HasCommunitySupport1To1: false,
            HasCommunitySupportSelfDirected: false,
            HasCommunitySupportDayProgram: false,
            DayProgramCount: 0,
            HasEmploymentSpecialist: false,
            HasWorkSupports: false,
            IsEmployed: false,
            Revision: 1,
            Forms: [],
            Notes: [],
            ReleaseObligations: [release]);

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixedSettingsService : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(new Settings());
        public Task SaveAsync(Settings settings) => Task.CompletedTask;
    }

    private sealed class TestContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
