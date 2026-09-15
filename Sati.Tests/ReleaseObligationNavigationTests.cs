using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

public sealed class ReleaseObligationNavigationTests
{
    [Fact]
    public async Task ExactHistoricalObligationIsLoadedAndSelectedWithoutRedirecting()
    {
        var person = PersonForNavigation();
        var currentTarget = ComplianceScheduleRules.CurrentTargetEffectiveDate(
            person.EffectiveDate!.Value, DateTime.Today);
        var historicalTarget = currentTarget.AddYears(-1);
        var obligationId = Guid.NewGuid();
        person.ReleaseComplianceSnapshots =
        [
            Fact(obligationId, historicalTarget)
        ];
        var service = new StubReleaseObligationService(
            Obligation(person.Id, obligationId, historicalTarget));
        var viewModel = new ReleaseObligationsViewModel(service);
        viewModel.SetPerson(person);

        var opened = await viewModel.OpenForAttestationAsync(
            obligationId,
            historicalTarget);

        Assert.True(opened);
        Assert.Equal(obligationId, viewModel.SelectedForAttestation?.ObligationId);
        Assert.Contains(historicalTarget, service.StatusTargets);
    }

    [Fact]
    public async Task MismatchedAnnualTargetFailsClosedEvenWhenTheObligationIdExists()
    {
        var person = PersonForNavigation();
        var currentTarget = ComplianceScheduleRules.CurrentTargetEffectiveDate(
            person.EffectiveDate!.Value, DateTime.Today);
        var historicalTarget = currentTarget.AddYears(-1);
        var obligationId = Guid.NewGuid();
        person.ReleaseComplianceSnapshots =
        [
            Fact(obligationId, historicalTarget)
        ];
        var service = new StubReleaseObligationService(
            Obligation(person.Id, obligationId, historicalTarget));
        var viewModel = new ReleaseObligationsViewModel(service);
        viewModel.SetPerson(person);

        var opened = await viewModel.OpenForAttestationAsync(
            obligationId,
            currentTarget);

        Assert.False(opened);
        Assert.Null(viewModel.SelectedForAttestation);
        Assert.Contains("exact release obligation", viewModel.StatusMessage,
            StringComparison.OrdinalIgnoreCase);
    }

    private static Person PersonForNavigation()
    {
        var person = Person.CreatePerson(
            17,
            "Exact",
            "Release",
            string.Empty,
            new DateTime(1990, 1, 1),
            DateTime.Today.AddYears(-2),
            WaiverType.None,
            new Settings());
        typeof(Person).GetProperty(nameof(Person.Id))!.SetValue(person, 77);
        return person;
    }

    private static ReleaseComplianceFact Fact(Guid obligationId, DateTime target) =>
        new(
            $"release:v1:{target:yyyy-MM-dd}:dhhs:annual",
            ReleaseObligationCategory.Dhhs,
            target,
            target,
            RetiredOn: null,
            Attestations: [],
            obligationId,
            target,
            target.AddDays(-90));

    private static ReleaseObligationDto Obligation(
        int personId,
        Guid obligationId,
        DateTime target) =>
        new(
            1,
            obligationId,
            personId,
            $"release:v1:{target:yyyy-MM-dd}:dhhs:annual",
            nameof(ReleaseObligationCategory.Dhhs),
            nameof(ReleaseObligationTrigger.AnnualRenewal),
            target,
            null,
            null,
            target.AddDays(-90),
            target,
            target,
            null,
            null,
            null,
            false,
            []);

    private sealed class StubReleaseObligationService(
        ReleaseObligationDto historical) : IReleaseObligationService
    {
        private readonly object sync = new();
        public List<DateTime> StatusTargets { get; } = [];

        public Task<ReleaseObligationStatusDto> GetStatusAsync(
            int requestedPersonId,
            DateTime targetEffectiveDate,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
                StatusTargets.Add(targetEffectiveDate.Date);
            return Task.FromResult(Status(
                requestedPersonId,
                targetEffectiveDate,
                historical.TargetEffectiveDate.Date == targetEffectiveDate.Date
                    ? [historical]
                    : []));
        }

        public Task<ReleaseObligationStatusDto> ReconcileAsync(
            int requestedPersonId,
            DateTime targetEffectiveDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Status(requestedPersonId, targetEffectiveDate, []));

        public Task<ReleaseObligationDto> AttestAsync(
            int requestedPersonId,
            Guid obligationId,
            DateTime completedOn,
            string? reason = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ReleaseObligationDto> WithdrawAsync(
            int requestedPersonId,
            Guid obligationId,
            DateTime withdrawnOn,
            string reason,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static ReleaseObligationStatusDto Status(
            int requestedPersonId,
            DateTime target,
            IReadOnlyList<ReleaseObligationDto> obligations) =>
            new(
                requestedPersonId,
                target.Date,
                "Consumer",
                "Consumer signature required",
                obligations,
                []);
    }
}
