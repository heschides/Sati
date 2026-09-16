using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class DashboardFormComplianceTests
{
    [Fact]
    public void TaskBoardUsesTargetEffectiveDateAndKeepsPredueAnnualWorkInItsCycle()
    {
        var today = new DateTime(2026, 4, 1);
        var currentTarget = new DateTime(2026, 3, 7);
        var person = Person.CreatePerson(
            31, "Cycle", "Selection", string.Empty, new DateTime(1990, 1, 1),
            currentTarget.AddYears(-1), WaiverType.None, new Settings());
        person.Forms.Clear();
        var current = new Form(
            FormType.ComprehensiveAssessment,
            currentTarget.AddDays(-90),
            targetEffectiveDate: currentTarget);
        var next = new Form(
            FormType.ComprehensiveAssessment,
            currentTarget.AddYears(1).AddDays(-90),
            targetEffectiveDate: currentTarget.AddYears(1));
        var tooFar = new Form(
            FormType.ComprehensiveAssessment,
            currentTarget.AddYears(2).AddDays(-90),
            targetEffectiveDate: currentTarget.AddYears(2));
        var historical = new Form(
            FormType.ComprehensiveAssessment,
            currentTarget.AddYears(-1).AddDays(-90),
            targetEffectiveDate: currentTarget.AddYears(-1));
        person.Forms.AddRange([tooFar, next, current, historical]);

        Assert.Same(historical, CaseManagerDashboardViewModel.SelectBoardForm(
            person, FormType.ComprehensiveAssessment, today));

        historical.SetInitialCompletion(today);
        Assert.Same(current, CaseManagerDashboardViewModel.SelectBoardForm(
            person, FormType.ComprehensiveAssessment, today));

        current.SetInitialCompletion(today);
        Assert.Same(next, CaseManagerDashboardViewModel.SelectBoardForm(
            person, FormType.ComprehensiveAssessment, today));

        next.SetInitialCompletion(today);
        Assert.Null(CaseManagerDashboardViewModel.SelectBoardForm(
            person, FormType.ComprehensiveAssessment, today));
    }

    [Fact]
    public void ReleaseBoardUsesExactRecipientObligationsAndSuppressesFixedFormsPerReconciledCycle()
    {
        var today = new DateTime(2026, 4, 1);
        var currentTarget = new DateTime(2026, 3, 7);
        var nextTarget = currentTarget.AddYears(1);
        var currentId = Guid.NewGuid();
        var nextId = Guid.NewGuid();
        var historicalId = Guid.NewGuid();
        var person = Person.CreatePerson(
            31, "Release", "Board", string.Empty, new DateTime(1990, 1, 1),
            currentTarget.AddYears(-1), WaiverType.None, new Settings());
        person.Forms.Clear();
        person.Forms.AddRange(
        [
            new Form(FormType.Release_Medical, currentTarget,
                targetEffectiveDate: currentTarget),
            new Form(FormType.Release_Agency, nextTarget,
                targetEffectiveDate: nextTarget)
        ]);
        person.ReleaseComplianceSnapshots =
        [
            ReleaseFact("historical-dhhs", ReleaseObligationCategory.Dhhs,
                currentTarget.AddYears(-1), currentTarget.AddYears(-1), historicalId, null),
            ReleaseFact("current-medical", ReleaseObligationCategory.Medical,
                currentTarget, currentTarget, currentId, "Dr. Exact"),
            ReleaseFact("next-agency", ReleaseObligationCategory.Agency,
                nextTarget, nextTarget, nextId, "Service Exact"),
            ReleaseFact("too-far", ReleaseObligationCategory.Dhhs,
                nextTarget.AddYears(1), nextTarget.AddYears(1), Guid.NewGuid(), null)
        ];

        var rows = CaseManagerDashboardViewModel.BuildReleaseRowsForPerson(
            person, new Settings(), today);

        Assert.Equal([historicalId, currentId, nextId], rows.Select(row => row.ObligationId));
        Assert.Contains("Dr. Exact", rows[1].TypeLabel);
        Assert.Contains("Service Exact", rows[2].TypeLabel);
        Assert.Contains("days overdue", rows[0].AutomationName);
        Assert.DoesNotContain(rows, row => row.ObligationKey.StartsWith("legacy-form:"));
    }

    [Fact]
    public void AgendaFormResolutionFailsClosedInsteadOfRedirectingToTheCurrentCycle()
    {
        var currentTarget = new DateTime(2026, 3, 7);
        var upcomingTarget = currentTarget.AddYears(1);
        var person = Person.CreatePerson(
            31, "Exact", "Routing", string.Empty, new DateTime(1990, 1, 1),
            currentTarget.AddYears(-1), WaiverType.None, new Settings());
        person.Forms.Clear();
        var current = new Form(FormType.PCP, currentTarget,
            targetEffectiveDate: currentTarget) { Id = 41 };
        var upcoming = new Form(FormType.PCP, upcomingTarget,
            targetEffectiveDate: upcomingTarget) { Id = 42 };
        person.Forms.AddRange([current, upcoming]);

        Assert.Same(upcoming, CaseManagerDashboardViewModel.ResolveAgendaForm(
            person, FormType.PCP, upcoming.Id, upcomingTarget, currentTarget.AddMonths(6)));
        Assert.Same(upcoming, CaseManagerDashboardViewModel.ResolveAgendaForm(
            person, FormType.PCP, null, upcomingTarget, currentTarget.AddMonths(6)));
        Assert.Null(CaseManagerDashboardViewModel.ResolveAgendaForm(
            person, FormType.PCP, upcoming.Id, currentTarget, currentTarget.AddMonths(6)));
        Assert.Null(CaseManagerDashboardViewModel.ResolveAgendaForm(
            person, FormType.PCP, 9999, upcomingTarget, currentTarget.AddMonths(6)));
    }

    [Fact]
    public async Task ClientProfileFormsStayLockedAndChangeOnlyAfterADatedAttestation()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var harness = await DashboardHarness.CreateAsync(fixture);
        var (person, form) = harness.AddOverdueQuarterlyReview(FormType.Q3R);
        var clients = harness.Dashboard.Clients;

        clients.People.Add(person);
        Assert.True(clients.PeopleView.MoveCurrentTo(person));
        var peopleView = clients.PeopleView;
        var currentPerson = clients.PeopleView.CurrentItem;
        clients.SelectedPerson = person;
        var revisionBeforeAttestation = clients.CompliancePresentationRevision;

        Assert.False(clients.IsFormsEditingUnlocked);
        Assert.False(clients.ToggleFormCommand.CanExecute(FormType.Q3R));
        await clients.ToggleFormCommand.ExecuteAsync(FormType.Q3R);
        Assert.False(clients.Attestation.IsVisible);
        Assert.False(clients.Q3RCompliant);
        Assert.True(clients.HasSelectedPersonComplianceIssues);

        clients.ToggleFormsEditingCommand.Execute(null);
        Assert.True(clients.IsFormsEditingUnlocked);
        Assert.True(clients.ToggleFormCommand.CanExecute(FormType.Q3R));

        await clients.ToggleFormCommand.ExecuteAsync(FormType.Q3R);
        Assert.True(clients.Attestation.IsVisible);
        Assert.Null(clients.Attestation.CompletionDate);
        Assert.False(clients.Attestation.CompleteAttestationCommand.CanExecute(null));
        Assert.False(clients.Q3RCompliant);

        var completedOn = DateTime.Today.AddDays(-2);
        clients.Attestation.CompletionDate = completedOn;
        Assert.True(clients.Attestation.CompleteAttestationCommand.CanExecute(null));
        await clients.Attestation.CompleteAttestationCommand.ExecuteAsync(null);

        Assert.Equal(completedOn, form.CompletedDate);
        Assert.True(clients.Q3RCompliant);
        Assert.False(clients.HasSelectedPersonComplianceIssues);
        Assert.True(clients.CompliancePresentationRevision > revisionBeforeAttestation);
        Assert.Same(peopleView, clients.PeopleView);
        Assert.Same(currentPerson, clients.PeopleView.CurrentItem);

        var revisionBeforeRevocation = clients.CompliancePresentationRevision;
        await clients.ToggleFormForAsync(person, FormType.Q3R);
        clients.Attestation.RevocationReason = "Recorded against the wrong review.";
        await clients.Attestation.RevokeAttestationCommand.ExecuteAsync(null);

        Assert.Null(form.CompletedDate);
        Assert.False(clients.Q3RCompliant);
        Assert.True(clients.HasSelectedPersonComplianceIssues);
        Assert.True(clients.CompliancePresentationRevision > revisionBeforeRevocation);
        Assert.Same(peopleView, clients.PeopleView);
        Assert.Same(currentPerson, clients.PeopleView.CurrentItem);

        var nextPerson = Person.CreatePerson(
            fixture.CaseManagerOne.Id,
            "Next",
            "Profile",
            string.Empty,
            new DateTime(1990, 1, 1),
            DateTime.Today.AddMonths(-2),
            WaiverType.Section21,
            new Settings());
        clients.SelectedPerson = nextPerson;

        Assert.False(clients.IsFormsEditingUnlocked);
        Assert.False(clients.ToggleFormCommand.CanExecute(FormType.Q3R));
    }

    [Fact]
    public async Task PcpAndAssessmentRequireAnExplicitEvergreenAttestation()
    {
        var service = new RecordingFormService();
        var viewModel = new FormAttestationViewModel(service);
        var effectiveDate = DateTime.Today.AddMonths(-6);
        var form = new Form(FormType.PCP, DateTime.Today.AddDays(20)) { PersonId = 42 };

        viewModel.Begin(form, effectiveDate, "Person-Centered Plan — Demo Consumer");
        viewModel.CompletionDate = DateTime.Today;

        Assert.True(viewModel.RequiresEvergreenConfirmation);
        Assert.Contains("completed in Evergreen", viewModel.AttestationStatement,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CompleteAttestationCommand.CanExecute(null));

        viewModel.HasConfirmedEvergreenCompletion = true;

        Assert.True(viewModel.CompleteAttestationCommand.CanExecute(null));
        await viewModel.CompleteAttestationCommand.ExecuteAsync(null);
        Assert.Equal(DateTime.Today, form.CompletedDate);
    }

    [Fact]
    public void DashboardRendersTheAttestationPanelOpenedByItsFormActions()
    {
        var view = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Views", "CaseManagerDashboardContentView.xaml"));

        Assert.Contains("<views:FormAttestationControl", view, StringComparison.Ordinal);
        Assert.Contains("DataContext=\"{Binding Attestation}\"", view, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CompletionPath.DashboardToggle)]
    [InlineData(CompletionPath.TaskBoard)]
    [InlineData(CompletionPath.ClientOverview)]
    public async Task EveryCompletionPathRefreshesMatrixAndRemovesLateReviewEvent(
        CompletionPath path)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var harness = await DashboardHarness.CreateAsync(fixture);
        var (person, form) = harness.AddOverdueQuarterlyReview(FormType.Q3R);

        Assert.Equal(FormCellStatus.Overdue, harness.Dashboard.Matrix!.Rows.Single().Q3R.Status);
        Assert.Contains(harness.Dashboard.UpcomingEvents,
            item => item.Kind == UpcomingEventKind.LateReview && item.Title.StartsWith("Q3 Review"));

        switch (path)
        {
            case CompletionPath.DashboardToggle:
                harness.Dashboard.SelectedPerson = person;
                await harness.Dashboard.ToggleFormCommand.ExecuteAsync(FormType.Q3R);
                break;
            case CompletionPath.TaskBoard:
                await harness.Dashboard.MarkFormCompletedCommand.ExecuteAsync(new FormTaskRow(
                    form,
                    person.FullName,
                    "Q3 Review",
                    form.DueDate.AddDays(-30),
                    DateTime.Today));
                break;
            case CompletionPath.ClientOverview:
                await harness.Dashboard.Clients.ToggleFormForAsync(person, FormType.Q3R);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(path), path, null);
        }

        var attestation = path == CompletionPath.ClientOverview
            ? harness.Dashboard.Clients.Attestation
            : harness.Dashboard.Attestation;
        var explicitCompletion = DateTime.Today.AddDays(-2);
        Assert.Null(form.CompletedDate);
        Assert.True(attestation.IsVisible);
        Assert.Null(attestation.CompletionDate);
        attestation.CompletionDate = explicitCompletion;
        await attestation.CompleteAttestationCommand.ExecuteAsync(null);

        Assert.Equal(explicitCompletion, form.CompletedDate);
        Assert.Equal(FormCellStatus.Complete, harness.Dashboard.Matrix.Rows.Single().Q3R.Status);
        Assert.DoesNotContain(harness.Dashboard.UpcomingEvents,
            item => item.Kind == UpcomingEventKind.LateReview && item.Title.StartsWith("Q3 Review"));
    }

    private sealed class DashboardHarness
    {
        private readonly Settings settings;
        private readonly UpcomingEventService upcomingEvents;
        private readonly MutablePersonService people;

        private DashboardHarness(
            CaseManagerDashboardViewModel dashboard,
            Settings settings,
            UpcomingEventService upcomingEvents,
            MutablePersonService people)
        {
            Dashboard = dashboard;
            this.settings = settings;
            this.upcomingEvents = upcomingEvents;
            this.people = people;
        }

        public CaseManagerDashboardViewModel Dashboard { get; }

        public static async Task<DashboardHarness> CreateAsync(NoteEntryFixture fixture)
        {
            var session = new SessionService();
            session.SetUser(fixture.CaseManagerOne);
            var people = new MutablePersonService();
            var notes = fixture.NotesFromAnotherSession();
            var settings = new Settings { ReviewDaysAfterDue = 30 };
            var settingsService = new FixedSettingsService(settings);
            var forms = new RecordingFormService();
            var exemptDates = new EmptyExemptDateService();
            var upcomingEvents = new UpcomingEventService();
            var noteEntry = fixture.NoteEntry(people: people, notes: notes);
            var notesLog = new NotesWindowViewModel(
                people, session, notes, fixture.NoteEntry(people: people, notes: notes));
            var clients = new NewClientViewModel(
                people,
                session,
                notes,
                forms,
                settingsService,
                null!,
                new StubPersonContactService(),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!);
            var calendar = new CalendarViewModel(exemptDates, notes, session);
            var reviews = new ReviewsViewModel(session, people, null!, settingsService, forms);
            var dashboard = new CaseManagerDashboardViewModel(
                people,
                notes,
                settingsService,
                new StubIncentiveService(),
                session,
                upcomingEvents,
                forms,
                noteEntry,
                notesLog,
                clients,
                calendar,
                exemptDates,
                null!,
                reviews,
                null!,
                null!,
                new GuidanceViewModel(),
                new HelperReferenceViewModel());

            await dashboard.InitializeAsync();
            return new DashboardHarness(dashboard, settings, upcomingEvents, people);
        }

        public (Person Person, Form Form) AddOverdueQuarterlyReview(FormType type)
        {
            var person = Person.CreatePerson(
                Dashboard.LoggedInUser!.Id,
                "Quarterly",
                "Review",
                string.Empty,
                new DateTime(1990, 1, 1),
                DateTime.Today.AddMonths(-6),
                WaiverType.Section21,
                settings);
            var form = person.GetCurrentCycleForm(type, DateTime.Today)!;
            // This fixture exercises one attestation and its presentation refresh.
            // Keep every other default billing requirement satisfied so the named
            // review is the only blocker under the corrected whole-cycle gate.
            foreach (var other in person.Forms.Where(candidate =>
                         !ReferenceEquals(candidate, form) &&
                         BillingComplianceGate.IsRequired(
                             candidate.Type.ToString(),
                             BillingComplianceGate.DefaultRequirements)))
            {
                other.SetInitialCompletion(DateTime.Today.AddDays(-2));
            }
            form.DueDate = DateTime.Today.AddDays(-1);
            form.SetInitialCompletion(null);

            people.Items.Clear();
            people.Items.Add(person);
            Dashboard.People.Clear();
            Dashboard.People.Add(person);
            Dashboard.Matrix!.Rebuild(Dashboard.People, DateTime.Today);
            Dashboard.UpcomingEvents.Clear();
            foreach (var item in upcomingEvents.GenerateEvents(Dashboard.People, settings))
                Dashboard.UpcomingEvents.Add(item);

            return (person, form);
        }
    }

    public enum CompletionPath
    {
        DashboardToggle,
        TaskBoard,
        ClientOverview
    }

    private static ReleaseComplianceFact ReleaseFact(
        string key,
        ReleaseObligationCategory category,
        DateTime dueOn,
        DateTime target,
        Guid obligationId,
        string? recipient) => new(
        key,
        category,
        dueOn,
        target,
        null,
        [],
        obligationId,
        target,
        target.AddDays(-90),
        recipient);

    private sealed class MutablePersonService : IPersonService
    {
        public List<Person> Items { get; } = [];

        public Task<Person> AddPersonAsync(Person person) => Task.FromResult(person);
        public Task<List<Person>> GetAllPeopleAsync(int userId) => Task.FromResult(Items.ToList());
        public Task<Person> EditPersonAsync(Person person) => Task.FromResult(person);
        public Task<string?> GetJournalAsync(int personId) => Task.FromResult<string?>(null);
        public Task SaveJournalAsync(int personId, string? journal) => Task.CompletedTask;
        public Task<JournalReminderResult> AddJournalReminderAsync(int personId, string text) =>
            Task.FromResult(new JournalReminderResult(text));
        public Task<CaseloadOwnershipDto> TransferOwnershipAsync(int personId, int targetUserId, int expectedRevision) =>
            throw new NotSupportedException();
        public Task<PersonStatusDto> SetPersonStatusAsync(
            int personId, string status, string? note, int expectedRevision) =>
            throw new NotSupportedException();
        public Task<CredibleMatchLookupResult> FindCredibleMatchesAsync(
            IReadOnlyList<string> credibleClientIds,
            IReadOnlyList<string>? maineCareIds = null,
            IReadOnlyList<PersonNameBirthDate>? nameBirthDates = null) =>
            Task.FromResult(CredibleMatchLookupResult.Empty);
        public Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId) =>
            Task.FromResult<List<PersonSummary>>([]);
    }

    private sealed class RecordingFormService : IFormService
    {
        public Task UpdateFormAsync(Form form) => Task.CompletedTask;
        public Task AttestAsync(Form form, DateTime completedOn, int? evidenceNoteId = null)
        {
            form.Attest(FormAttestation.Attested(
                completedOn, AttestationActorKind.CaseManager, 31, DateTime.UtcNow));
            return Task.CompletedTask;
        }
        public Task RevokeAttestationAsync(Form form, string reason)
        {
            form.RevokeAttestation(FormAttestation.Revoked(
                AttestationActorKind.CaseManager, 31, DateTime.UtcNow, reason));
            return Task.CompletedTask;
        }
        public Task OpenFormAsync(Form form) => Task.CompletedTask;
        public Task DeleteFormsAsync(IEnumerable<Form> forms) => Task.CompletedTask;
    }

    private sealed class FixedSettingsService(Settings settings) : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(settings);
        public Task SaveAsync(Settings value) => Task.CompletedTask;
    }

    private sealed class EmptyExemptDateService : IExemptDateService
    {
        public Task<List<ExemptDate>> GetByYearAsync(int userId, int year) => Task.FromResult<List<ExemptDate>>([]);
        public Task<ExemptDate> AddAsync(int userId, DateTime date, string? reason = null) =>
            throw new NotSupportedException();
        public Task RemoveAsync(int id) => throw new NotSupportedException();
    }

    private static string RepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, ".."));

    private sealed class StubIncentiveService : IIncentiveService
    {
        public Task<(Incentive incentive, bool wasCreated)> GetOrCreateAsync(int userId, int month, int year) =>
            Task.FromResult((new Incentive
            {
                UserId = userId,
                Month = month,
                Year = year
            }, false));

        public Task SaveAsync(Incentive incentive) => Task.CompletedTask;
        public Task<int> GetRemainingEligibleDaysAsync(
            int month,
            int year,
            HashSet<DateTime> daysAlreadyWorked,
            HashSet<DateTime> exemptDates) => Task.FromResult(0);
        public Task<int> GetEligibleDaysAsync(DateTime startInclusive, DateTime endInclusive) => Task.FromResult(0);
        public Task<List<Incentive>> GetHistoryAsync(int userId) => Task.FromResult<List<Incentive>>([]);
    }
}
