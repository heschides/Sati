using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

public sealed class ReleaseComplianceRefreshTests
{
    private static readonly DateTime Today = DateTime.Today;
    private static readonly DateTime CurrentTarget =
        ComplianceScheduleRules.CurrentTargetEffectiveDate(new DateTime(2024, 3, 7), Today);

    [Fact]
    public async Task RefreshMergesAuthoritativeFactsBeforeOneComplianceNotification()
    {
        var person = PersonWithStaleCurrentDhhs();
        var priorTarget = CurrentTarget.AddYears(-1);
        var prior = CreateDhhsEntity(person.Id, priorTarget);
        prior.AttestManually(
            priorTarget,
            Today,
            AttestationActorKind.CaseManager,
            actorUserId: 1,
            DateTime.SpecifyKind(Today, DateTimeKind.Utc));
        person.ReleaseObligations.Add(prior);

        var service = new StubReleaseService();
        service.CompletedTargets.Add(CurrentTarget);
        var viewModel = new ReleaseObligationsViewModel(service);
        var notifications = 0;
        viewModel.ComplianceChangedAsync = () =>
        {
            notifications++;
            var facts = ((IEventSource)person).ReleaseComplianceFacts;
            Assert.NotNull(ReleaseAttestationRules.CompletedOn(
                service.StableKey(CurrentTarget),
                facts.Single(item => item.TargetEffectiveDate == CurrentTarget).Attestations));
            return Task.CompletedTask;
        };

        viewModel.SetPerson(person);
        await viewModel.RefreshAsync();

        Assert.Equal(1, notifications);
        Assert.Null(person.ReleaseObligations.Single(item =>
            item.TargetEffectiveDate == CurrentTarget).CompletedOn);
        Assert.Contains(person.ReleaseComplianceSnapshots,
            item => item.TargetEffectiveDate == priorTarget);
        Assert.True(person.EvaluateBillingWindowDetailed(
            Today,
            BillingComplianceRequirements.DhhsRelease).Passed);
    }

    [Fact]
    public async Task AttestationAndWithdrawalEachRefreshSharedComplianceExactlyOnce()
    {
        var person = PersonWithStaleCurrentDhhs();
        var service = new StubReleaseService();
        var viewModel = new ReleaseObligationsViewModel(service);

        viewModel.SetPerson(person);
        await viewModel.OpenForAttestationAsync(service.ObligationId(CurrentTarget), CurrentTarget);
        var notifications = 0;
        viewModel.ComplianceChangedAsync = () =>
        {
            notifications++;
            return Task.CompletedTask;
        };

        viewModel.CompletionDate = Today;
        await viewModel.CompleteAttestationCommand.ExecuteAsync(null);

        Assert.Equal(1, notifications);
        Assert.Equal(Today, ((IEventSource)person).ReleaseComplianceFacts
            .Single(item => item.TargetEffectiveDate == CurrentTarget)
            .Attestations.Single().CompletedOn);

        viewModel.BeginWithdrawalCommand.Execute(viewModel.Items.Single(item =>
            item.ObligationId == service.ObligationId(CurrentTarget)));
        viewModel.WithdrawalDate = Today;
        viewModel.WithdrawalReason = "Consumer withdrew authorization.";
        await viewModel.CompleteWithdrawalCommand.ExecuteAsync(null);

        Assert.Equal(2, notifications);
        Assert.Equal(Today, viewModel.Items.Single(item =>
            item.ObligationId == service.ObligationId(CurrentTarget)).WithdrawnOn);
        Assert.NotNull(((IEventSource)person).ReleaseComplianceFacts
            .Single(item => item.TargetEffectiveDate == CurrentTarget)
            .Attestations.SingleOrDefault());
    }

    [Fact]
    public async Task ProviderAssignmentRefreshesFactsAndSharedComplianceOnce()
    {
        var person = PersonWithStaleCurrentDhhs();
        var releases = new StubReleaseService();
        var releaseViewModel = new ReleaseObligationsViewModel(releases);
        releaseViewModel.SetPerson(person);

        var links = new StubProviderLinkService(() => releases.IncludeMedical = true);
        var providers = new ConsumerProvidersViewModel(
            links,
            new StubProviderService(),
            () => Today);
        providers.SetPerson(person);
        providers.ProviderAssignmentsChangedAsync = releaseViewModel.RefreshAsync;

        var notifications = 0;
        releaseViewModel.ComplianceChangedAsync = () =>
        {
            notifications++;
            return Task.CompletedTask;
        };
        providers.NewProviderId = 25;
        providers.NewStartDate = Today.AddDays(10);

        await providers.AddProviderCommand.ExecuteAsync(null);

        Assert.Equal(1, notifications);
        Assert.Contains(((IEventSource)person).ReleaseComplianceFacts, item =>
            item.Category == ReleaseObligationCategory.Medical &&
            item.RecipientDisplayName == "Dr. Current");
    }

    private static Person PersonWithStaleCurrentDhhs()
    {
        var person = Person.Rehydrate(40, 1);
        person.EffectiveDate = CurrentTarget.AddYears(-1);
        person.ReleaseObligations.Add(CreateDhhsEntity(person.Id, CurrentTarget));
        return person;
    }

    private static ReleaseObligation CreateDhhsEntity(int personId, DateTime target)
    {
        var plan = ReleaseObligationRules.GenerateCycle(target, []).Single();
        return ReleaseObligation.Create(
            agencyId: 1,
            personId,
            plan,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private sealed class StubReleaseService : IReleaseObligationService
    {
        public HashSet<DateTime> CompletedTargets { get; } = [];
        public bool IncludeMedical { get; set; }
        private bool _withdrawn;

        public Guid ObligationId(DateTime target) => Guid.Parse(
            $"00000000-0000-0000-0000-{target:yyyyMMdd}0000");

        public string StableKey(DateTime target) => ReleaseObligationRules.StableKey(
            target,
            ReleaseObligationCategory.Dhhs,
            ReleaseObligationTrigger.AnnualRenewal,
            assignmentKey: null);

        public Task<ReleaseObligationStatusDto> GetStatusAsync(
            int personId,
            DateTime targetEffectiveDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Status(personId, targetEffectiveDate));

        public Task<ReleaseObligationStatusDto> ReconcileAsync(
            int personId,
            DateTime targetEffectiveDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Status(personId, targetEffectiveDate));

        public Task<ReleaseObligationDto> AttestAsync(
            int personId,
            Guid obligationId,
            DateTime completedOn,
            string? reason = null,
            CancellationToken cancellationToken = default)
        {
            CompletedTargets.Add(CurrentTarget);
            return Task.FromResult(Status(personId, CurrentTarget).Obligations[0]);
        }

        public Task<ReleaseObligationDto> WithdrawAsync(
            int personId,
            Guid obligationId,
            DateTime withdrawnOn,
            string reason,
            CancellationToken cancellationToken = default)
        {
            _withdrawn = true;
            return Task.FromResult(Status(personId, CurrentTarget).Obligations[0]);
        }

        private ReleaseObligationStatusDto Status(int personId, DateTime targetEffectiveDate)
        {
            var target = targetEffectiveDate.Date;
            var obligations = new List<ReleaseObligationDto> { Dhhs(personId, target) };
            if (IncludeMedical && target == CurrentTarget)
                obligations.Add(Medical(personId, target));
            return new ReleaseObligationStatusDto(
                personId,
                target,
                nameof(SignerCapacity.Consumer),
                "Consumer signature",
                obligations,
                []);
        }

        private ReleaseObligationDto Dhhs(int personId, DateTime target)
        {
            var key = StableKey(target);
            var completed = CompletedTargets.Contains(target);
            var attestations = completed
                ? new[]
                {
                    new ReleaseObligationAttestationDto(
                        91,
                        Today,
                        nameof(ReleaseAttestationSource.Manual),
                        nameof(AttestationActorKind.CaseManager),
                        1,
                        null,
                        null,
                        DateTime.SpecifyKind(Today, DateTimeKind.Utc),
                        null)
                }
                : [];
            return new ReleaseObligationDto(
                target.Year,
                ObligationId(target),
                personId,
                key,
                nameof(ReleaseObligationCategory.Dhhs),
                nameof(ReleaseObligationTrigger.AnnualRenewal),
                target,
                null,
                null,
                target.AddDays(-90),
                target,
                target,
                null,
                completed ? Today : null,
                _withdrawn && target == CurrentTarget ? Today : null,
                completed && (!_withdrawn || target != CurrentTarget),
                attestations);
        }

        private static ReleaseObligationDto Medical(int personId, DateTime target)
        {
            const string assignmentKey = "person-provider:100";
            var key = ReleaseObligationRules.StableKey(
                target,
                ReleaseObligationCategory.Medical,
                ReleaseObligationTrigger.AssignmentStart,
                assignmentKey);
            return new ReleaseObligationDto(
                target.Year + 10_000,
                Guid.Parse($"10000000-0000-0000-0000-{target:yyyyMMdd}0000"),
                personId,
                key,
                nameof(ReleaseObligationCategory.Medical),
                nameof(ReleaseObligationTrigger.AssignmentStart),
                target,
                25,
                "Dr. Current",
                Today,
                Today.AddDays(9),
                Today.AddDays(10),
                null,
                null,
                null,
                false,
                []);
        }
    }

    private sealed class StubProviderLinkService(Action onSave) : IConsumerProviderService
    {
        private readonly List<PersonProvider> _links = [];

        public Task<List<PersonProvider>> GetByPersonAsync(int personId) =>
            Task.FromResult(_links.Where(item => item.PersonId == personId).ToList());

        public Task<PersonProvider> SaveAsync(PersonProvider link)
        {
            _links.Add(link);
            onSave();
            return Task.FromResult(link);
        }

        public Task EndAsync(int personId, int linkId, DateTime endDate) => Task.CompletedTask;
        public Task RemoveAsync(int personId, int linkId) => Task.CompletedTask;
    }

    private sealed class StubProviderService : IProviderService
    {
        private static Provider Provider => new()
        {
            Id = 25,
            Name = "Dr. Current",
            Type = ProviderType.Healthcare,
            MedicalKind = MedicalProviderKind.Individual
        };

        public Task<List<Provider>> GetAllAsync() => Task.FromResult(new List<Provider> { Provider });
        public Task<List<Provider>> GetPassthroughProvidersAsync() => Task.FromResult(new List<Provider>());
        public Task<Provider> AddAsync(Provider provider) => Task.FromResult(provider);
        public Task<Provider> UpdateAsync(Provider provider) => Task.FromResult(provider);
        public Task DeleteAsync(Provider provider) => Task.CompletedTask;
        public Task<List<ProviderContact>> GetContactsAsync(int providerId) => Task.FromResult(new List<ProviderContact>());
        public Task<ProviderContact> SaveContactAsync(ProviderContact contact) => Task.FromResult(contact);
        public Task RemoveContactAsync(int providerId, int contactId) => Task.CompletedTask;
        public Task<string> MergeAsync(int survivingProviderId, int mergedProviderId) => Task.FromResult(string.Empty);
    }
}
