using PdfSharp.Pdf.IO;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Forms;
using Sati.Models;
using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

[Collection(PdfRenderingCollection.Name)]
public sealed class AgencyReleaseTests
{
    [Fact]
    public void Valid_release_passes_the_shared_rules()
    {
        Assert.Empty(AgencyReleaseRules.Validate(ValidRequest()));
    }

    [Fact]
    public void Staff_generation_confirmation_explicitly_has_no_compliance_effect()
    {
        Assert.Contains(
            "document preparation only",
            AgencyReleaseRules.StaffAttestation,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "does not complete a tracked release obligation",
            AgencyReleaseRules.AttestationScopeNotice,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void One_time_release_cannot_exceed_ninety_days()
    {
        var request = ValidRequest() with { ExpirationDate = new DateOnly(2026, 12, 2) };

        var errors = AgencyReleaseRules.Validate(request);

        Assert.Contains("90 days", errors[nameof(request.ExpirationDate)].Single());
    }

    [Fact]
    public void Selecting_other_requires_a_description()
    {
        var request = ValidRequest() with
        {
            InformationCategories = [AgencyReleaseInformation.Other],
            OtherInformation = "",
        };

        var errors = AgencyReleaseRules.Validate(request);

        Assert.Contains(nameof(request.OtherInformation), errors.Keys);
    }

    [Fact]
    public void Generator_produces_a_two_page_pdf()
    {
        var subject = new AgencyReleaseSubject(
            31,
            "Jordan Example",
            new DateTime(1987, 4, 12),
            "Taylor Example",
            "Example Support Services",
            "12 Main Street, Augusta, ME, 04330",
            "207-555-0100",
            "Case Manager",
            "CaseManager");

        var bytes = new AgencyReleasePdfGenerator().Generate(
            subject,
            ValidRequest() with { ConfirmedObtainedRoi = true },
            new DateTime(2026, 8, 19, 14, 30, 0, DateTimeKind.Utc));

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        using var stream = new MemoryStream(bytes);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(2, pdf.PageCount);
    }

    [Fact]
    public void Medical_generator_produces_a_distinct_two_page_pdf()
    {
        var subject = new AgencyReleaseSubject(
            31, "Jordan Example", new DateTime(1987, 4, 12), null,
            "Example Support Services", "12 Main Street", "207-555-0100",
            "Case Manager", "CaseManager");
        var shared = new AgencyReleasePdfGenerator();

        var bytes = new MedicalReleasePdfGenerator(shared).Generate(
            subject, ValidRequest(), new DateTime(2026, 9, 3, 14, 0, 0, DateTimeKind.Utc));

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        using var stream = new MemoryStream(bytes);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(2, pdf.PageCount);
    }

    [Fact]
    public void DraftRejectsSensitiveConsentButAllowsIdentityOnlyPreparation()
    {
        var draft = ValidRequest() with
        {
            IsDraft = true,
            AuthorizationGranted = null,
            ContactName = null,
            InformationCategories = [],
            IncludeDrugAlcohol = null,
            IncludeMentalHealth = null,
            IncludeHivAids = null
        };

        Assert.Empty(AgencyReleaseRules.Validate(draft));
        Assert.Contains("SensitiveConsent", AgencyReleaseRules.Validate(
            draft with { IncludeMentalHealth = true }).Keys);
    }

    [Fact]
    public async Task LocalGenerationRecordsOnlyMetadataAndSupersedesThePriorArtifact()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate ??= DateTime.Today.AddMonths(-2);
            await db.SaveChangesAsync();
        }
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var shared = new AgencyReleasePdfGenerator();
        var service = new AgencyReleaseService(
            fixture.Factory, session, shared, new MedicalReleasePdfGenerator(shared));

        await service.GenerateMedicalAsync(
            fixture.PersonOneId, ValidRequest() with { IsDraft = true });
        await service.GenerateMedicalAsync(fixture.PersonOneId, ValidRequest());

        await using var verification = fixture.Factory.CreateDbContext();
        var artifacts = await verification.DocumentArtifacts.AsNoTracking()
            .Where(artifact => artifact.PersonId == fixture.PersonOneId &&
                artifact.Kind == AnnualDocumentKind.ReleaseMedical)
            .OrderBy(artifact => artifact.Id)
            .ToListAsync();
        Assert.Equal(2, artifacts.Count);
        Assert.Equal(DocumentArtifactOrigin.Draft, artifacts[0].Origin);
        Assert.Equal(artifacts[1].Id, artifacts[0].SupersededByArtifactId);
        Assert.Equal(DocumentArtifactOrigin.GeneratedInSati, artifacts[1].Origin);
        Assert.Null(artifacts[1].SupersededByArtifactId);
        Assert.Equal(64, artifacts[1].ContentSha256?.Length);
        Assert.True(artifacts[1].ByteCount > 0);
    }

    [Fact]
    public async Task LocalGenerationLinksTheArtifactToTheExactSelectedRecipientObligation()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var target = DateTime.Today;
        Guid obligationId;
        long obligationRecordId;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var person = await db.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = target.AddYears(-1);
            var provider = new Provider
            {
                Id = 99101,
                AgencyId = fixture.CaseManagerOne.AgencyId,
                Type = ProviderType.Healthcare,
                MedicalKind = MedicalProviderKind.Individual,
                Name = "Exact Medical Recipient"
            };
            db.Providers.Add(provider);
            var plan = ReleaseObligationRules.GenerateCycle(target,
            [
                new ReleaseAssignmentFact(
                    "person-provider:99102",
                    ReleaseAssignmentKind.MedicalProvider,
                    target.AddYears(-1),
                    null,
                    target.AddYears(-1))
            ]).Single(item => item.Category == ReleaseObligationCategory.Medical);
            var obligation = ReleaseObligation.Create(
                fixture.CaseManagerOne.AgencyId,
                person.Id,
                plan,
                DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                provider.Id,
                provider.Name);
            db.ReleaseObligations.Add(obligation);
            await db.SaveChangesAsync();
            obligationId = obligation.ObligationId;
            obligationRecordId = obligation.Id;
        }

        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var shared = new AgencyReleasePdfGenerator();
        var service = new AgencyReleaseService(
            fixture.Factory, session, shared, new MedicalReleasePdfGenerator(shared));

        await service.GenerateMedicalForObligationAsync(
            fixture.PersonOneId, ValidRequest(), obligationId);

        await using var verification = fixture.Factory.CreateDbContext();
        var artifact = await verification.DocumentArtifacts.AsNoTracking()
            .SingleAsync(item => item.PersonId == fixture.PersonOneId &&
                                 item.Kind == AnnualDocumentKind.ReleaseMedical &&
                                 item.SupersededByArtifactId == null);
        Assert.Equal(obligationRecordId, artifact.ReleaseObligationId);
        Assert.Equal(target, artifact.CycleStart);
    }

    [Fact]
    public async Task Staff_generation_confirmation_requires_confirmation_before_generation()
    {
        var service = new RecordingAgencyReleaseService();
        var viewModel = ReadyViewModel(service);
        viewModel.DidObtainRoi = true;
        viewModel.AttestationRequested += _ => false;

        await viewModel.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(0, service.GenerationCount);
        Assert.Contains("not recorded", viewModel.StatusMessage);
    }

    [Fact]
    public void Changing_consumer_clears_every_release_choice()
    {
        var viewModel = ReadyViewModel(new RecordingAgencyReleaseService());
        viewModel.ContactName = "Prior recipient";
        viewModel.InformationCategories[0].IsSelected = true;
        viewModel.DidObtainRoi = true;

        viewModel.SetPerson(PersonFor(42, "Second"));

        Assert.Equal(string.Empty, viewModel.ContactName);
        Assert.All(viewModel.InformationCategories, option => Assert.False(option.IsSelected));
        Assert.False(viewModel.DidObtainRoi);
        Assert.Null(viewModel.AuthorizationChoice);
    }

    [Fact]
    public async Task ReleaseEditorSendsTheExactSelectedObligationAndFiltersByDocumentKind()
    {
        var service = new RecordingAgencyReleaseService();
        var viewModel = ReadyViewModel(service);
        var agencyId = Guid.NewGuid();
        var medicalId = Guid.NewGuid();
        viewModel.SetReleaseObligations(
        [
            Obligation(31, agencyId, ReleaseObligationCategory.Agency, "Service Provider"),
            Obligation(31, medicalId, ReleaseObligationCategory.Medical, "Medical Provider")
        ]);

        var agencyChoice = Assert.Single(viewModel.ReleaseObligationChoices);
        Assert.Equal(agencyId, agencyChoice.ObligationId);
        viewModel.SelectedReleaseObligation = agencyChoice;
        await viewModel.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(agencyId, service.LastObligationId);
        Assert.Equal(AnnualDocumentKind.ReleaseAgency, service.LastKind);

        viewModel.SelectedReleaseKind = viewModel.ReleaseKindChoices.Single(
            item => item.Kind == AnnualDocumentKind.ReleaseMedical);
        var medicalChoice = Assert.Single(viewModel.ReleaseObligationChoices);
        Assert.Equal(medicalId, medicalChoice.ObligationId);
        Assert.Null(viewModel.SelectedReleaseObligation);
        viewModel.SelectedReleaseObligation = medicalChoice;
        await viewModel.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(medicalId, service.LastObligationId);
        Assert.Equal(AnnualDocumentKind.ReleaseMedical, service.LastKind);
    }

    [Fact]
    public void ReleaseEditorDisambiguatesDuplicateRecipientNamesWithDirectoryIdentity()
    {
        var viewModel = ReadyViewModel(new RecordingAgencyReleaseService());
        viewModel.SetReleaseObligations(
        [
            Obligation(31, Guid.NewGuid(), ReleaseObligationCategory.Agency,
                "Same Provider", recipientProviderId: 77),
            Obligation(31, Guid.NewGuid(), ReleaseObligationCategory.Agency,
                "Same Provider", recipientProviderId: 78)
        ]);

        Assert.Equal(2, viewModel.ReleaseObligationChoices.Count);
        Assert.Contains("directory #77", viewModel.ReleaseObligationChoices[0].DisplayName);
        Assert.Contains("directory #78", viewModel.ReleaseObligationChoices[1].DisplayName);
        Assert.NotEqual(
            viewModel.ReleaseObligationChoices[0].DisplayName,
            viewModel.ReleaseObligationChoices[1].DisplayName);
    }

    [Fact]
    public void ReleaseTrackerNamesTheDirectoryIdentityForDuplicateSafeAccessibility()
    {
        var item = new ReleaseObligationItemViewModel(
            Obligation(31, Guid.NewGuid(), ReleaseObligationCategory.Medical,
                "Same Provider", recipientProviderId: 78),
            DateTime.Today);

        Assert.Contains("Same Provider (directory #78)", item.Name);
        Assert.Contains("Same Provider (directory #78)", item.AutomationName);
    }

    private static AgencyReleaseViewModel ReadyViewModel(IAgencyReleaseService service)
    {
        var viewModel = new AgencyReleaseViewModel(service);
        viewModel.SetPerson(PersonFor(31, "First"));
        viewModel.AuthorizationChoice = viewModel.YesNoChoices.Single(choice => choice.Value);
        viewModel.ContactType = viewModel.ContactTypeChoices[0];
        viewModel.ContactName = "Community Provider";
        viewModel.ContactAddress = "1 Center Street";
        viewModel.ContactPhone = "207-555-0100";
        viewModel.InformationCategories[0].IsSelected = true;
        viewModel.StartDate = new DateTime(2026, 8, 19);
        viewModel.SelectedScope = viewModel.ScopeChoices.Single(choice => choice.Value == AgencyReleaseScope.OneTime);
        viewModel.DrugAlcoholChoice = viewModel.YesNoChoices.Single(choice => !choice.Value);
        viewModel.MentalHealthChoice = viewModel.YesNoChoices.Single(choice => !choice.Value);
        viewModel.HivAidsChoice = viewModel.YesNoChoices.Single(choice => !choice.Value);
        viewModel.ReleaseWithoutReviewChoice = viewModel.YesNoChoices.Single(choice => !choice.Value);
        return viewModel;
    }

    private static AgencyReleaseRequest ValidRequest() => new(
        true,
        "Community support",
        "Community Provider",
        "Service provider",
        "1 Center Street",
        "Augusta",
        "ME",
        "207-555-0101",
        "207-555-0100",
        "records@example.test",
        [AgencyReleaseInformation.IntakeAssessment, AgencyReleaseInformation.TreatmentPlan],
        null,
        new DateOnly(2026, 8, 19),
        new DateOnly(2026, 11, 17),
        nameof(AgencyReleaseScope.OneTime),
        false,
        false,
        false,
        false);

    private static ReleaseObligationDto Obligation(
        int personId,
        Guid obligationId,
        ReleaseObligationCategory category,
        string recipient,
        int recipientProviderId = 77) => new(
        42,
        obligationId,
        personId,
        $"synthetic:{obligationId:D}",
        category.ToString(),
        ReleaseObligationTrigger.AnnualRenewal.ToString(),
        DateTime.Today,
        recipientProviderId,
        recipient,
        DateTime.Today.AddDays(-1),
        DateTime.Today,
        DateTime.Today,
        null,
        null,
        null,
        false,
        []);

    private static Person PersonFor(int id, string lastName)
    {
        var person = Person.CreatePerson(
            1,
            "Test",
            lastName,
            string.Empty,
            new DateTime(1980, 1, 1),
            DateTime.Today.AddYears(-1),
            WaiverType.Section21,
            new Settings());
        typeof(Person).GetProperty(nameof(Person.Id))!.SetValue(person, id);
        person.AgencyId = 1;
        return person;
    }

    private sealed class RecordingAgencyReleaseService : IAgencyReleaseService
    {
        public int GenerationCount { get; private set; }
        public Guid? LastObligationId { get; private set; }
        public AnnualDocumentKind? LastKind { get; private set; }

        public Task<AgencyReleaseResult> GenerateAsync(
            int personId,
            AgencyReleaseRequest request,
            CancellationToken cancellationToken = default)
        {
            GenerationCount++;
            return Task.FromResult(new AgencyReleaseResult([1, 2, 3], "agency-release.pdf"));
        }

        public Task<AgencyReleaseResult> GenerateForObligationAsync(
            int personId,
            AgencyReleaseRequest request,
            Guid releaseObligationId,
            CancellationToken cancellationToken = default)
        {
            GenerationCount++;
            LastObligationId = releaseObligationId;
            LastKind = AnnualDocumentKind.ReleaseAgency;
            return Task.FromResult(new AgencyReleaseResult([1, 2, 3], "agency-release.pdf"));
        }

        public Task<AgencyReleaseResult> GenerateMedicalForObligationAsync(
            int personId,
            AgencyReleaseRequest request,
            Guid releaseObligationId,
            CancellationToken cancellationToken = default)
        {
            GenerationCount++;
            LastObligationId = releaseObligationId;
            LastKind = AnnualDocumentKind.ReleaseMedical;
            return Task.FromResult(new AgencyReleaseResult([1, 2, 3], "medical-release.pdf"));
        }
    }
}
