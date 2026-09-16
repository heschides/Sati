using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

public sealed class AnnualDocumentSelectionTests
{
    [Fact]
    public void ChangingSafetyCycleClearsLoadedPlanBeforeAnyAction()
    {
        var service = new SafetyService();
        var vm = new SafetyPlanViewModel(service, new Session());
        vm.SetPerson(Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30), DateTime.Today.AddYears(-1), WaiverType.Section21, new Settings()));
        Assert.True(vm.CanEdit);
        vm.CycleStart = vm.CycleStart!.Value.AddYears(1);
        Assert.False(vm.CanEdit);
        Assert.Empty(vm.Sections);
    }

    [Fact]
    public void SafetyWorkspaceSelectsTheUpcomingTargetOnceItsWindowOpens()
    {
        var service = new SafetyService();
        var vm = new SafetyPlanViewModel(service, new Session());
        var effective = DateTime.Today.AddYears(-1).AddDays(30).Date;
        var person = Person.CreatePerson(
            12,
            "Synthetic",
            "Upcoming",
            "",
            DateTime.Today.AddYears(-30),
            effective,
            WaiverType.Section21,
            new Settings());

        vm.SetPerson(person);

        Assert.Equal(effective.AddYears(1), vm.CycleStart);
    }

    [Fact]
    public void SafetyWorkspaceHonorsTheConfiguredAvailabilityWindow()
    {
        var service = new SafetyService();
        var configured = new Settings
        {
            SafetyPlanOpenDaysBefore = 10,
            SafetyPlanDaysBeforeAnniversary = 0
        };
        var vm = new SafetyPlanViewModel(
            service, new Session(), new SettingsServiceStub(configured));
        var effective = DateTime.Today.AddYears(-1).AddDays(30).Date;
        var person = Person.CreatePerson(
            12,
            "Synthetic",
            "Configured",
            "",
            DateTime.Today.AddYears(-30),
            effective,
            WaiverType.Section21,
            new Settings());

        vm.SetPerson(person);

        Assert.Equal(effective, vm.CycleStart);
    }

    [Fact]
    public void ChangingPacketCycleClearsPriorArtifactsAndReceiptAction()
    {
        var vm = new AnnualDocumentsViewModel(new AnnualService(), null!, new SettingsServiceStub(), new Session());
        vm.SetPerson(Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30), DateTime.Today.AddYears(-1), WaiverType.Section21, new Settings()));
        Assert.True(vm.CanSavePacket);
        vm.CycleStart = vm.CycleStart!.Value.AddYears(1);
        Assert.False(vm.CanSavePacket);
        Assert.Empty(vm.Artifacts);
    }

    // The packet cycle comes from the agency's AnnualPacketOpenDaysBefore. Before that
    // setting loads there is no cycle to suggest, so a failed load must leave the
    // selection empty rather than a guess made from the 30-day default.
    [Fact]
    public void PacketCycleStaysUnselectedWhenAgencySettingsCannotLoad()
    {
        var vm = new AnnualDocumentsViewModel(new AnnualService(), null!, new FailingSettingsService(), new Session());
        vm.SetPerson(Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30), DateTime.Today.AddYears(-1), WaiverType.Section21, new Settings()));
        Assert.Null(vm.CycleStart);
        Assert.False(vm.CanSavePacket);
    }

    private sealed class FailingSettingsService : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromException<Settings>(new InvalidOperationException("synthetic"));
        public Task SaveAsync(Settings settings) => throw new NotSupportedException();
    }

    [Fact]
    public async Task AnnualWorkflowSeparatesEachRequiredDocumentAndHidesTheOnceOnlyFormWhenRecorded()
    {
        var service = new AnnualService();
        var vm = new AnnualDocumentsViewModel(service, null!, new SettingsServiceStub(), new Session());
        vm.SetPerson(Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30),
            DateTime.Today.AddYears(-1), WaiverType.Section21, new Settings()));

        Assert.Equal(
            ["DHHS release", "Medical Provider Release", "Agency Release", "Safety Plan", "Privacy Practices", "DHHS Authorized Representative"],
            vm.DocumentWorkflow.Select(item => item.DisplayName).ToArray());
        Assert.All(vm.DocumentWorkflow, item => Assert.Equal("Not started", item.Status));

        service.AuthorizedRepresentativeOnFile = true;
        await vm.ReloadCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.DocumentWorkflow, item => item.DisplayName == "DHHS Authorized Representative");
        Assert.Contains("already recorded on file", vm.AuthorizedRepresentativeRecordedMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecordingTheOnceOnlyFormRequiresANoteAndRemovesItFromAnnualWork()
    {
        var service = new AnnualService();
        var vm = new AnnualDocumentsViewModel(service, null!, new SettingsServiceStub(), new Session());
        vm.SetPerson(Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30),
            DateTime.Today.AddYears(-1), WaiverType.Section21, new Settings()));
        Assert.False(vm.CanRecordAuthorizedRepresentativeOnFile);

        vm.AuthorizedRepresentativeOnFileNote = "Verified the signed paper copy in the agency record.";
        Assert.True(vm.CanRecordAuthorizedRepresentativeOnFile);
        await vm.RecordAuthorizedRepresentativeOnFileCommand.ExecuteAsync(null);

        Assert.True(service.AuthorizedRepresentativeOnFile);
        Assert.False(vm.NeedsAuthorizedRepresentative);
        Assert.DoesNotContain(vm.DocumentWorkflow, item => item.DisplayName == "DHHS Authorized Representative");
    }

    private sealed class Session : ISessionService
    {
        public bool AllowComplianceOverride { get; set; }
        public User? CurrentUser { get; private set; } = User.Create(12, "synthetic", "Synthetic Author", "hash", "salt", UserRole.CaseManager, null, 1);
        public void SetUser(User user) => CurrentUser = user;
    }
    private sealed class SettingsServiceStub(Settings? value = null) : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(value ?? new Settings());
        public Task SaveAsync(Settings settings) => throw new NotSupportedException();
    }
    private sealed class AnnualService : IAnnualDocumentService
    {
        public bool AuthorizedRepresentativeOnFile { get; set; }
        public Task<AnnualDocumentsStatusDto> GetStatusAsync(int id, DateTime cycle) => Task.FromResult(
            new AnnualDocumentsStatusDto(new(cycle, cycle.AddDays(-30), cycle.AddYears(1).AddDays(-1), true), [], [], "",
                AuthorizedRepresentativeOnFile));
        public Task<DocumentAcknowledgmentDto> AcknowledgeAsync(int id, AcknowledgeDocumentRequest request) => throw new NotSupportedException();
        public Task<DocumentArtifactDto> RecordAuthorizedRepresentativeOnFileAsync(int id, DateTime cycle, string note)
        {
            AuthorizedRepresentativeOnFile = true;
            return Task.FromResult(new DocumentArtifactDto(91, id, 1,
                AnnualDocumentKind.DhhsAuthorizedRepresentative.ToString(), cycle,
                DocumentArtifactOrigin.RecordedAsExternal.ToString(), DateTime.UtcNow, 12,
                null, null, null, [], note));
        }
        public Task<VerifyDocumentResult> VerifyAsync(int id, VerifyDocumentRequest request) => throw new NotSupportedException();
        public Task<AgencyReleaseResult> SavePacketAsync(int id, DateTime cycle) => throw new NotSupportedException();
    }
    private sealed class SafetyService : ISafetyPlanService
    {
        public Task<SafetyPlanDto?> GetAsync(int id, DateTime cycle) => Task.FromResult<SafetyPlanDto?>(
            new(1, id, 12, cycle, "Draft", 1, 0, DateTime.UtcNow, DateTime.UtcNow, null, null, null, null, SafetyPlanRules.EmptyDocumentJson()));
        public Task<SafetyPlanDto> StartAsync(int id, DateTime cycle) => throw new NotSupportedException();
        public Task<SafetyPlanDto> ChangeAsync(SafetyPlanDto plan, string action, string? document = null, string? reason = null) => throw new NotSupportedException();
        public Task<AgencyReleaseResult> GenerateAsync(int id, DateTime cycle) => throw new NotSupportedException();
    }
}
