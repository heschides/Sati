using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Models;
using Sati.Models.Billing;
using Sati.ViewModels.Admin;
using Sati.ViewModels.Billing;
using Xunit;

namespace Sati.Tests;

public sealed class BillingComplianceRecoveryUiTests
{
    private static string Root => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(BillingComplianceRecoveryUiTests).Assembly.Location)!,
        "..", "..", "..", "..", ".."));

    [Fact]
    public async Task AdminReviewsAllEligibleNotesByDefaultAndRecordsOnlyThoseLeftSelected()
    {
        var session = AdminSession();
        var active = Person(41, "River, Jamie");
        var admin = new StubAdminService([
            active,
            Person(42, "Inactive, Casey", "Inactive")
        ]);
        var billing = new StubBillingService
        {
            Plan = RecoveryPlan(active.PersonId)
        };
        var viewModel = new BillingComplianceRecoveryViewModel(billing, session, admin);

        await viewModel.LoadPeopleAsync();

        Assert.True(viewModel.CanManageComplianceRecovery);
        Assert.Equal(active, Assert.Single(viewModel.RecoveryPeople));
        viewModel.SelectedRecoveryPerson = active;
        Assert.True(viewModel.LoadRecoveryPlanCommand.CanExecute(null));

        await viewModel.LoadRecoveryPlanCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.RecoveryNotes.Count);
        Assert.All(viewModel.RecoveryNotes, row => Assert.True(row.IsSelected));
        Assert.Contains("Person-centered plan", viewModel.RecoveryNotes[0].BlockerLabel);
        Assert.Contains("due Mar 7, 2026", viewModel.RecoveryNotes[0].BlockerLabel);
        Assert.Contains("completed Mar 10, 2026", viewModel.RecoveryNotes[0].BlockerLabel);
        Assert.Contains("attestation:pcp-2026", viewModel.RecoveryNotes[0].BlockerLabel);
        Assert.True(viewModel.RecordComplianceRecoveryCommand.CanExecute(null));

        viewModel.RecoveryNotes[1].IsSelected = false;
        await viewModel.RecordComplianceRecoveryCommand.ExecuteAsync(null);
        Assert.Contains("Enter an explanation", viewModel.RecoveryStatus, StringComparison.Ordinal);

        viewModel.RecoveryExplanation = "Requirements are now complete; release the selected draft note.";
        await viewModel.RecordComplianceRecoveryCommand.ExecuteAsync(null);
        Assert.Contains("Confirm the administrator attestation", viewModel.RecoveryStatus,
            StringComparison.Ordinal);

        viewModel.RecoveryAttestationConfirmed = true;

        Assert.True(viewModel.RecordComplianceRecoveryCommand.CanExecute(null));
        await viewModel.RecordComplianceRecoveryCommand.ExecuteAsync(null);

        Assert.NotNull(billing.RecordedRequest);
        var request = billing.RecordedRequest!;
        Assert.Equal([701], request.SelectedNoteIds);
        Assert.Equal(
            "Requirements are now complete; release the selected draft note.",
            request.Explanation);
        Assert.True(request.AttestationConfirmed);
        Assert.Empty(viewModel.RecoveryNotes);
        Assert.Contains("recorded for 1 note", viewModel.RecoveryStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangingConsumerWhileAPlanLoadsCannotPublishTheOldConsumersNotes()
    {
        var session = AdminSession();
        var first = Person(41, "River, Jamie");
        var second = Person(42, "Lake, Morgan");
        var pendingPlan = new TaskCompletionSource<BillingComplianceRecoveryPlan>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var billing = new StubBillingService { PendingPlan = pendingPlan };
        var viewModel = new BillingComplianceRecoveryViewModel(
            billing,
            session,
            new StubAdminService([first, second]));
        await viewModel.LoadPeopleAsync();
        viewModel.SelectedRecoveryPerson = first;

        var load = viewModel.LoadRecoveryPlanCommand.ExecuteAsync(null);
        await billing.PlanRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.SelectedRecoveryPerson = second;
        pendingPlan.SetResult(RecoveryPlan(first.PersonId));
        await load;

        Assert.Empty(viewModel.RecoveryNotes);
        Assert.Equal(second, viewModel.SelectedRecoveryPerson);
        Assert.Contains("Select Find eligible notes", viewModel.RecoveryStatus, StringComparison.Ordinal);
        Assert.False(viewModel.IsRecoveryBusy);
    }

    [Fact]
    public async Task APlanForAnotherConsumerIsRejectedBeforeRowsAreDisplayed()
    {
        var session = AdminSession();
        var selected = Person(41, "River, Jamie");
        var viewModel = new BillingComplianceRecoveryViewModel(
            new StubBillingService { Plan = RecoveryPlan(999) },
            session,
            new StubAdminService([selected]));
        await viewModel.LoadPeopleAsync();
        viewModel.SelectedRecoveryPerson = selected;

        await viewModel.LoadRecoveryPlanCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.RecoveryNotes);
        Assert.Contains("did not match the selected consumer", viewModel.RecoveryStatus,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecoveryRemainsUnavailableWithoutAdministratorPermission()
    {
        var session = new SessionService();
        session.SetUser(User.Create(
            9, "case-manager", "Case Manager", "hash", "salt", UserRole.CaseManager, null, 1));
        var admin = new StubAdminService([Person(41, "River, Jamie")]);
        var viewModel = new BillingComplianceRecoveryViewModel(new StubBillingService(), session, admin);

        await viewModel.LoadPeopleAsync();

        Assert.False(viewModel.CanManageComplianceRecovery);
        Assert.Empty(viewModel.RecoveryPeople);
        Assert.Equal(0, admin.PeopleCalls);
        Assert.False(viewModel.LoadRecoveryPlanCommand.CanExecute(null));
    }

    [Fact]
    public void DependencyInjectionSharesRecoveryWorkspaceWithoutRequiringBillingPermission()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBillingService, StubBillingService>();
        services.AddSingleton<ISessionService>(_ => AdminSession());
        services.AddSingleton<IAdminService>(_ => new StubAdminService([Person(41, "River, Jamie")]));
        services.AddSingleton<BillingComplianceRecoveryViewModel>();
        services.AddSingleton<BillingOverviewViewModel>();
        services.AddSingleton<AdminDashboardViewModel>();
        using var provider = services.BuildServiceProvider();

        var recovery = provider.GetRequiredService<BillingComplianceRecoveryViewModel>();
        var billing = provider.GetRequiredService<BillingOverviewViewModel>();
        var admin = provider.GetRequiredService<AdminDashboardViewModel>();
        var session = provider.GetRequiredService<ISessionService>();

        Assert.False(session.CurrentUser!.HasBillingPermissions);
        Assert.True(session.CurrentUser.HasAdminPermissions);
        Assert.Same(recovery, billing.ComplianceRecovery);
        Assert.Same(recovery, admin.ComplianceRecovery);
    }

    [Fact]
    public async Task AdminDashboardAccountClearRemovesRecoveryData()
    {
        var session = AdminSession();
        var adminService = new StubAdminService([Person(41, "River, Jamie")]);
        var recovery = new BillingComplianceRecoveryViewModel(
            new StubBillingService(), session, adminService);
        await recovery.LoadPeopleAsync();
        recovery.SelectedRecoveryPerson = recovery.RecoveryPeople[0];
        var dashboard = new AdminDashboardViewModel(
            adminService, session, complianceRecovery: recovery);

        dashboard.ClearForAccountSwitch();

        Assert.False(recovery.CanManageComplianceRecovery);
        Assert.Null(recovery.SelectedRecoveryPerson);
        Assert.Empty(recovery.RecoveryPeople);
        Assert.Empty(recovery.RecoveryNotes);
    }

    [Fact]
    public void RecoveryWorkspaceIsReachableFromAdminAndSharedWithBilling()
    {
        var workspace = XDocument.Load(Path.Combine(
            Root, "Views", "Billing", "ComplianceRecoveryWorkspace.xaml"));
        var elements = workspace.Descendants().ToArray();

        var panel = Assert.Single(elements, element =>
            (string?)element.Attribute("AutomationProperties.Name") ==
            "Administrator compliance recovery");
        Assert.Contains("CanManageComplianceRecovery", (string?)workspace.Root!.Attribute("Visibility"));

        var noteChoice = Assert.Single(elements, element =>
            element.Name.LocalName == "CheckBox" &&
            (string?)element.Attribute("AutomationProperties.Name") == "{Binding NoteLabel}");
        Assert.Equal("{Binding IsSelected}", (string?)noteChoice.Attribute("IsChecked"));
        Assert.Equal("{Binding BlockerLabel}",
            (string?)noteChoice.Attribute("AutomationProperties.HelpText"));

        var attestation = Assert.Single(elements, element =>
            element.Name.LocalName == "CheckBox" &&
            (string?)element.Attribute("AutomationProperties.Name") ==
            "Administrator compliance recovery attestation");
        Assert.Equal("Wrap", (string?)Assert.Single(attestation.Elements()).Attribute("TextWrapping"));

        var status = Assert.Single(elements, element =>
            (string?)element.Attribute("Text") == "{Binding RecoveryStatus}");
        Assert.Equal("Assertive", (string?)status.Attribute("AutomationProperties.LiveSetting"));

        var billing = XDocument.Load(Path.Combine(Root, "Views", "Billing", "BillingOverviewView.xaml"));
        var admin = XDocument.Load(Path.Combine(Root, "Views", "AdminDashboardView.xaml"));
        Assert.Contains(billing.Descendants(), IsRecoveryWorkspace);
        Assert.Contains(admin.Descendants(), IsRecoveryWorkspace);
        Assert.Contains(admin.Descendants(), element =>
            element.Name.LocalName == "TabItem" &&
            (string?)element.Attribute("Header") == "Compliance recovery");
    }

    [Fact]
    public async Task BillingOnlyOverviewLoadsTheUnresolvedPolicyReviewQueue()
    {
        var session = BillingSession();
        var flag = new BillingCompliancePolicyReviewFlagDto(
            Guid.NewGuid(),
            81,
            new DateTime(2026, 3, 1),
            BillingComplianceRequirements.Pcp,
            41,
            701,
            901,
            new DateTime(2026, 3, 8),
            BillingCompliancePolicyImpactChangeKind.NewlyBlocked,
            [],
            ["form:pcp-2026"],
            new DateTime(2026, 3, 1, 15, 0, 0, DateTimeKind.Utc));
        var service = new StubBillingService { PolicyReviewFlags = [flag] };
        var viewModel = new BillingOverviewViewModel(service, session);

        await viewModel.LoadAsync();

        Assert.True(session.CurrentUser!.HasBillingPermissions);
        Assert.False(session.CurrentUser.HasAdminPermissions);
        Assert.Equal(UserPermissions.Billing, service.PolicyReviewActor?.Permissions);
        var row = Assert.Single(viewModel.BillingPolicyReviewFlags);
        Assert.Equal("Claim record 901 (note 701)", row.RecordLabel);
        Assert.True(viewModel.HasBillingPolicyReviewFlags);
        Assert.Contains("1 unresolved", viewModel.BillingPolicyReviewSummary,
            StringComparison.OrdinalIgnoreCase);

        var view = XDocument.Load(Path.Combine(
            Root, "Views", "Billing", "BillingOverviewView.xaml"));
        Assert.Contains(view.Descendants(), element =>
            (string?)element.Attribute("AutomationProperties.Name") ==
            "Unresolved billing policy review queue");
        Assert.Contains(view.Descendants(), element =>
            (string?)element.Attribute("ItemsSource") ==
            "{Binding BillingPolicyReviewFlags}");
    }

    private static bool IsRecoveryWorkspace(XElement element) =>
        element.Name.LocalName == "ComplianceRecoveryWorkspace" &&
        (string?)element.Attribute("DataContext") == "{Binding ComplianceRecovery}";

    private static SessionService AdminSession()
    {
        var session = new SessionService();
        var admin = User.Create(7, "admin", "Admin", "hash", "salt", UserRole.Admin, null, 1);
        admin.Permissions = UserPermissions.Administration;
        session.SetUser(admin);
        return session;
    }

    private static SessionService BillingSession()
    {
        var session = new SessionService();
        var biller = User.Create(
            8, "billing", "Billing", "hash", "salt", UserRole.CaseManager, null, 1);
        biller.Permissions = UserPermissions.Billing;
        session.SetUser(biller);
        return session;
    }

    private static AdminPersonListItemDto Person(int id, string name, string status = "Active") =>
        new(id, name, 1, 7, "Case Manager", Status: status);

    private static BillingComplianceRecoveryPlan RecoveryPlan(int personId)
    {
        var obligation = new BillingRecoveryObligationOption(
            "form:pcp-2026",
            "Person-centered plan",
            new DateTime(2026, 3, 7),
            new DateTime(2026, 3, 10),
            "attestation:pcp-2026");
        return new BillingComplianceRecoveryPlan(
            1,
            personId,
            [obligation],
            [
                new BillingRecoveryNoteOption(701, new DateTime(2026, 3, 8), [obligation.ObligationId]),
                new BillingRecoveryNoteOption(702, new DateTime(2026, 3, 9), [obligation.ObligationId])
            ],
            []);
    }

    private sealed class StubBillingService : IBillingService
    {
        public BillingComplianceRecoveryPlan? Plan { get; init; }
        public TaskCompletionSource<BillingComplianceRecoveryPlan>? PendingPlan { get; init; }
        public TaskCompletionSource PlanRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CreateBillingComplianceRecoveryRequest? RecordedRequest { get; private set; }
        public IReadOnlyList<BillingCompliancePolicyReviewFlagDto> PolicyReviewFlags { get; init; } = [];
        public AgencyActor? PolicyReviewActor { get; private set; }

        public Task<BillingComplianceRecoveryPlan> PrepareComplianceRecoveryAsync(
            AgencyActor actor, int personId, CancellationToken cancellationToken = default)
        {
            PlanRequested.TrySetResult();
            return PendingPlan?.Task ?? Task.FromResult(Plan ?? RecoveryPlan(personId));
        }

        public Task<Sati.Contracts.V1.BillingComplianceRecoveryDecision> RecordComplianceRecoveryAsync(
            AgencyActor actor,
            int personId,
            CreateBillingComplianceRecoveryRequest request,
            CancellationToken cancellationToken = default)
        {
            RecordedRequest = request;
            var plan = Plan ?? RecoveryPlan(personId);
            return Task.FromResult(new Sati.Contracts.V1.BillingComplianceRecoveryDecision(
                Guid.NewGuid(),
                actor.AgencyId,
                personId,
                actor.UserId,
                DateTime.UtcNow,
                request.Explanation!.Trim(),
                true,
                plan.Obligations,
                request.SelectedNoteIds));
        }

        public Task<BillingConfiguration> GetBillingConfigurationAsync(AgencyActor actor) =>
            Task.FromResult(new BillingConfiguration("T1016", null, 20m, "", "", "", "", ""));
        public Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync(AgencyActor actor) =>
            Task.FromResult<IEnumerable<Note>>([]);
        public Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync(AgencyActor actor) =>
            Task.FromResult<IEnumerable<BillingPeriod>>([]);
        public Task<IReadOnlyList<RemittanceClaimOutcomeDto>> GetRemittanceOutcomesAsync(AgencyActor actor) =>
            Task.FromResult<IReadOnlyList<RemittanceClaimOutcomeDto>>([]);
        public Task<IReadOnlyList<BillingCompliancePolicyReviewFlagDto>>
            GetBillingCompliancePolicyReviewFlagsAsync(AgencyActor actor)
        {
            PolicyReviewActor = actor;
            return Task.FromResult(PolicyReviewFlags);
        }
        public BillingValidationResult ValidateNoteForBilling(Note note) =>
            new(true, note, []);

        public Task<BillingPeriod> GetOrCreateBillingPeriodAsync(AgencyActor actor, int userId, int month, int year) => throw new NotSupportedException();
        public Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(AgencyActor actor, int userId) => throw new NotSupportedException();
        public Task<ClaimLine> CreateClaimLineAsync(AgencyActor actor, int noteId, bool isComplianceException = false, string? complianceExceptionReason = null) => throw new NotSupportedException();
        public Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(AgencyActor actor, int userId) => throw new NotSupportedException();
        public Task SubmitBillingPeriodAsync(AgencyActor actor, int billingPeriodId) => throw new NotSupportedException();
        public Task ReturnBillingPeriodToDraftAsync(AgencyActor actor, int billingPeriodId) => throw new NotSupportedException();
        public Task SaveBillingConfigurationAsync(AgencyActor actor, BillingConfiguration configuration) => throw new NotSupportedException();
        public Task<IReadOnlyList<BillingSubmissionHistoryDto>> GetSubmissionHistoryAsync(AgencyActor actor) => throw new NotSupportedException();
        public Task<IReadOnlyList<RemittanceDepositDto>> GetRemittanceDepositsAsync(AgencyActor actor) => throw new NotSupportedException();
    }

    private sealed class StubAdminService(IReadOnlyList<AdminPersonListItemDto> people) : IAdminService
    {
        public int PeopleCalls { get; private set; }

        public Task<List<AdminPersonListItemDto>> GetPeopleAsync(CancellationToken cancellationToken = default)
        {
            PeopleCalls++;
            return Task.FromResult(people.ToList());
        }

        public Task<AdminOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminOperationsDto> GetOperationsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminIncidentDashboardDto> GetIncidentsAsync(int days = 30, int take = 250, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IncidentGroupDto> UpdateIncidentStatusAsync(long incidentId, string status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> ExportAuditCsvAsync(DateTime fromUtc, DateTime toUtc, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TestConsumerDeletionResultDto> DeleteTestConsumerAsync(int personId, int expectedRevision, string attestation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<AdminActivityDto>> GetActivityAsync(int days = 30, int take = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<PersonVersionDto>> GetPersonHistoryAsync(int personId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> ExportPersonHistoryPdfAsync(int personId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LegalHoldDto> PlaceLegalHoldAsync(PlaceLegalHoldRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LegalHoldDto> ReleaseLegalHoldAsync(int legalHoldId, string? releaseNote, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<LegalHoldDto>> GetLegalHoldsAsync(int personId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsumerDeletionResultDto> DeleteConsumerInWindowAsync(int personId, int expectedRevision, string attestation, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
