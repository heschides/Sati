using System.IO;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
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
    public void ChoosingAnotherYearLoadsOnlyThatYearsDocuments()
    {
        var service = new AnnualService();
        var vm = new AnnualDocumentsViewModel(service, null!, new SettingsServiceStub(), new Session());
        vm.SetPerson(Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30), DateTime.Today.AddYears(-1).AddDays(-10), WaiverType.Section21, new Settings()));
        var current = vm.CycleStart!.Value;
        Assert.Contains(vm.Artifacts, artifact => artifact.CycleStart == current);

        vm.NextYearCommand.Execute(null);

        Assert.Equal(current.AddYears(1), vm.CycleStart);
        Assert.Equal(current.AddYears(1), service.RequestedCycles[^1]);
        Assert.All(vm.Artifacts, artifact => Assert.Equal(current.AddYears(1), artifact.CycleStart));
    }

    [Fact]
    public void OverviewOpensOnTheYearInForceEvenWhenNextYearsPacketHasOpened()
    {
        // The next anniversary is 20 days away, inside the 30-day packet window that used
        // to make the page jump ahead to next year.
        var effective = DateTime.Today.AddYears(-1).AddDays(20);
        var vm = new AnnualDocumentsViewModel(new AnnualService(), null!, new SettingsServiceStub(), new Session());
        vm.SetPerson(Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30), effective, WaiverType.Section21, new Settings()));

        Assert.Equal(effective, vm.CycleStart);
        Assert.Equal("In force today", vm.YearBadge);
        Assert.Equal(PlanYearOverview.Label(effective), vm.YearLabel);
        Assert.True(vm.CanGoToNextYear);
    }

    [Fact]
    public void PreviousAndNextStopAtTheFirstYearAndTheNextRenewal()
    {
        var effective = DateTime.Today.AddYears(-2).AddDays(-10);
        var vm = new AnnualDocumentsViewModel(new AnnualService(), null!, new SettingsServiceStub(), new Session());
        vm.SetPerson(Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30), effective, WaiverType.Section21, new Settings()));
        Assert.Equal(2, vm.SelectedYearIndex);

        vm.NextYearCommand.Execute(null);
        Assert.Equal(3, vm.SelectedYearIndex);
        Assert.False(vm.CanGoToNextYear);
        Assert.False(vm.NextYearCommand.CanExecute(null));

        vm.PreviousYearCommand.Execute(null);
        vm.PreviousYearCommand.Execute(null);
        vm.PreviousYearCommand.Execute(null);
        Assert.Equal(0, vm.SelectedYearIndex);
        Assert.False(vm.CanGoToPreviousYear);
        Assert.Contains("first plan year", vm.YearRange);
    }

    [Fact]
    public void AnEarlierYearWithOpenWorkIsAnnouncedAndOneClickAway()
    {
        var effective = DateTime.Today.AddYears(-2).AddDays(-10);
        var person = Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30), effective, WaiverType.Section21, new Settings());
        var vm = new AnnualDocumentsViewModel(new AnnualService(), null!, new SettingsServiceStub(), new Session());
        vm.SetPerson(person);

        // Nothing was ever recorded, so the first plan year is still open.
        Assert.True(vm.HasEarlierYearNotice);
        Assert.StartsWith(PlanYearOverview.Label(effective), vm.EarlierYearNotice);

        vm.JumpToEarlierOpenYearCommand.Execute(null);

        Assert.Equal(0, vm.SelectedYearIndex);
        Assert.False(vm.HasEarlierYearNotice);
        Assert.Equal("Overdue", vm.YearBadgeState);
    }

    [Fact]
    public void AClientWithoutAnEffectiveDateShowsNoPlanYear()
    {
        var person = Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30), null, WaiverType.Section21, new Settings());
        var vm = new AnnualDocumentsViewModel(new AnnualService(), null!, new SettingsServiceStub(), new Session());
        vm.SetPerson(person);

        Assert.True(vm.HasNoPlanYear);
        Assert.False(vm.HasPlanYears);
        Assert.Null(vm.CycleStart);
    }

    [Fact]
    public void EveryViewIsBuiltFromTheSameItems()
    {
        var vm = new AnnualDocumentsViewModel(new AnnualService(), null!, new SettingsServiceStub(), new Session());
        vm.SetPerson(Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30), DateTime.Today.AddYears(-1).AddDays(-10), WaiverType.Section21, new Settings()));

        var listed = vm.ListSections.SelectMany(section => section.Rows).Select(row => row.Item.Key).OrderBy(key => key).ToList();
        var byPurpose = vm.PurposeCards.SelectMany(card => card.Rows).Concat(vm.OneTimeRows).Select(row => row.Item.Key).OrderBy(key => key).ToList();
        Assert.Equal(listed, byPurpose);
        Assert.Equal(vm.SelectedPlanYear!.Items.Count, listed.Count);
        Assert.Equal(vm.SelectedPlanYear.Items.Count(item => item.DueOn is not null),
            vm.Timeline!.Markers.Sum(marker => marker.Caption.EndsWith(" items", StringComparison.Ordinal)
                ? int.Parse(marker.Caption.Split(' ')[0]) : 1));
        Assert.Equal(vm.SelectedPlanYear.Items.Where(item => item.NeedsYou).Select(item => item.Key).OrderBy(key => key),
            vm.NeedsYouRows.Select(row => row.Item.Key).OrderBy(key => key));
    }

    [Fact]
    public async Task TheChosenViewIsRememberedForThatUser()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sati-annual-view-{Guid.NewGuid():N}.json");
        try
        {
            var preferences = new AnnualFormsViewPreferenceService(
                new DataEnvironmentInfo(SatiDataEnvironment.Demo, "SatiDemo", ApiBaseAddress: new Uri("https://demo.invalid")), path);
            Assert.True(await preferences.SaveForUserAsync(12, AnnualFormsView.Timeline));
            Assert.Equal(AnnualFormsView.List, await preferences.LoadForUserAsync(13));

            var vm = new AnnualDocumentsViewModel(new AnnualService(), null!, new SettingsServiceStub(), new Session(),
                viewPreferences: preferences);
            vm.SetPerson(Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30), DateTime.Today.AddYears(-1), WaiverType.Section21, new Settings()));
            for (var i = 0; i < 100 && vm.View != AnnualFormsView.Timeline; i++) await Task.Delay(20);
            Assert.True(vm.IsTimelineView);

            vm.IsByPurposeView = true;
            for (var i = 0; i < 100 && await preferences.LoadForUserAsync(12) != AnnualFormsView.ByPurpose; i++) await Task.Delay(20);
            Assert.Equal(AnnualFormsView.ByPurpose, await preferences.LoadForUserAsync(12));
            Assert.False(vm.IsListView);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
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
    public async Task TheOnceOnlyFormNeedsAttentionUntilItIsRecordedOnFile()
    {
        var service = new AnnualService();
        var vm = new AnnualDocumentsViewModel(service, null!, new SettingsServiceStub(), new Session());
        vm.SetPerson(Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30),
            DateTime.Today.AddYears(-1), WaiverType.Section21, new Settings()));

        var once = Assert.Single(vm.NeedsYouRows, row => row.Title == "DHHS authorized representative");
        Assert.Equal(PlanYearWorkspace.DhhsDocuments, once.Item.Workspace);
        Assert.Single(vm.OneTimeRows);
        Assert.DoesNotContain(vm.PurposeCards, card => card.Rows.Contains(once));

        service.AuthorizedRepresentativeOnFile = true;
        await vm.ReloadCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.NeedsYouRows, row => row.Title == "DHHS authorized representative");
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
        Assert.DoesNotContain(vm.NeedsYouRows, row => row.Title == "DHHS authorized representative");
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
        public List<DateTime> RequestedCycles { get; } = [];
        public Task<AnnualDocumentsStatusDto> GetStatusAsync(int id, DateTime cycle)
        {
            RequestedCycles.Add(cycle);
            // One draft per year, stamped with its cycle, so a test can tell which year loaded.
            var draft = new DocumentArtifactDto(cycle.Year, id, 1, AnnualDocumentKind.SafetyPlan.ToString(), cycle,
                DocumentArtifactOrigin.Draft.ToString(), DateTime.UtcNow, 12, null, null, null, [], null);
            return Task.FromResult(new AnnualDocumentsStatusDto(
                new(cycle, cycle.AddDays(-30), cycle.AddYears(1).AddDays(-1), true), [draft], [], "",
                AuthorizedRepresentativeOnFile));
        }
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
