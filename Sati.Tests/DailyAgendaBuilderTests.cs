using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class DailyAgendaBuilderTests
{
    private static readonly DateTime Today = new(2026, 9, 1);

    [Fact]
    public void LookbackIncludesFormsTooOldForUpcomingWindowWithoutDuplicatingLateEvents()
    {
        var person = PersonWithForms(
            "Alex",
            new Form(FormType.Q1R, Today.AddDays(-120)));
        var upcoming = new StubUpcomingEventService(
            Event("Q1 Review — Alex", Today.AddDays(-120), UpcomingEventKind.LateReview),
            Event("PCP — Alex", Today.AddDays(10), UpcomingEventKind.OpenReview));

        var result = new DailyAgendaBuilder(upcoming).Build([person], new Settings(), Today);

        Assert.Single(result.OverdueItems);
        Assert.Equal("Q1 Review", result.OverdueItems[0].Title);
        Assert.Single(result.UpcomingItems);
        Assert.Equal("PCP — Alex", result.UpcomingItems[0].Title);
    }

    [Fact]
    public void AgendaCarriesTheExactFormAndAnnualTargetInsteadOfOnlyItsType()
    {
        var target = new DateTime(2026, 3, 7);
        var overdue = new Form(
            FormType.PCP,
            Today.AddDays(-2),
            targetEffectiveDate: target)
        {
            Id = 704
        };
        var person = PersonWithForms("Alex", overdue);
        var upcomingTarget = target.AddYears(1);
        var upcoming = new UpcomingEvent
        {
            PersonId = person.Id,
            ClientName = person.FullName,
            Title = "PCP — Alex",
            Date = upcomingTarget,
            Kind = UpcomingEventKind.OpenReview,
            FormType = FormType.PCP,
            FormId = 811,
            TargetEffectiveDate = upcomingTarget
        };

        var result = new DailyAgendaBuilder(new StubUpcomingEventService(upcoming))
            .Build([person], new Settings(), Today);

        var overdueItem = Assert.Single(result.OverdueItems);
        Assert.Equal(704, overdueItem.FormId);
        Assert.Equal(target, overdueItem.TargetEffectiveDate);
        var upcomingItem = Assert.Single(result.UpcomingItems);
        Assert.Equal(811, upcomingItem.FormId);
        Assert.Equal(upcomingTarget, upcomingItem.TargetEffectiveDate);
        Assert.Equal($"form:{person.Id}:811", upcomingItem.Key);
    }

    [Fact]
    public void LookbackIncludesNonBillingFormsAndLabelsTheirBillingImpactAccurately()
    {
        var person = PersonWithForms(
            "Alex",
            new Form(FormType.PrivacyPractices, Today.AddDays(-2)),
            new Form(FormType.Q2R, Today.AddDays(-1)));

        var result = new DailyAgendaBuilder(new StubUpcomingEventService())
            .Build([person], new Settings(), Today);

        Assert.Equal(2, result.OverdueTotal);
        Assert.Contains(result.OverdueItems, item =>
            item.Title == "Privacy Practices" && !item.BlocksBilling);
        Assert.Contains(result.OverdueItems, item =>
            item.Title == "Q2 Review" && item.BlocksBilling);
    }

    [Fact]
    public void LookbackShowsFiveOldestWhileKeepingTheTrueTotal()
    {
        var forms = Enumerable.Range(1, 8)
            .Select(index => new Form(
                FormType.PrivacyPractices,
                Today.AddDays(-index)))
            .ToArray();
        var person = PersonWithForms("Alex", forms);

        var result = new DailyAgendaBuilder(new StubUpcomingEventService())
            .Build([person], new Settings(), Today);

        Assert.Equal(8, result.OverdueTotal);
        Assert.Equal(5, result.OverdueItems.Count);
        Assert.Equal(
            [Today.AddDays(-8), Today.AddDays(-7), Today.AddDays(-6), Today.AddDays(-5), Today.AddDays(-4)],
            result.OverdueItems.Select(item => item.DueDate));
    }

    [Fact]
    public void BuildingAgendaNeverChangesFormCompletionState()
    {
        var incomplete = new Form(FormType.Q3R, Today.AddDays(-4));
        var completed = new Form(FormType.SafetyPlan, Today.AddDays(-8));
        completed.SetInitialCompletion(Today.AddDays(-2));
        var person = PersonWithForms("Alex", incomplete, completed);
        var before = person.Forms
            .Select(form => (form.IsCompliant, form.CompletedDate))
            .ToArray();

        _ = new DailyAgendaBuilder(new StubUpcomingEventService())
            .Build([person], new Settings(), Today);

        Assert.Equal(before, person.Forms
            .Select(form => (form.IsCompliant, form.CompletedDate))
            .ToArray());
    }

    [Fact]
    public void QuietCaseloadSuggestsSoonestIncompleteAssessmentForm()
    {
        var later = PersonWithForms(
            "Later Person",
            new Form(FormType.ComprehensiveAssessment, Today.AddDays(90)));
        var sooner = PersonWithForms(
            "Sooner Person",
            new Form(FormType.ComprehensiveAssessment, Today.AddDays(45)));

        var result = new DailyAgendaBuilder(new StubUpcomingEventService())
            .Build([later, sooner], new Settings(), Today);

        Assert.NotNull(result.AssessmentSuggestion);
        Assert.Equal("Sooner Person", result.AssessmentSuggestion.PersonName);
        Assert.Equal(Today.AddDays(45), result.AssessmentSuggestion.DueDate);
    }

    [Fact]
    public void UpcomingSectionIsOrderedAndCappedAtFive()
    {
        var events = Enumerable.Range(1, 7)
            .Reverse()
            .Select(index => Event(
                $"Item {index}",
                Today.AddDays(index),
                UpcomingEventKind.OpenReview))
            .ToArray();

        var result = new DailyAgendaBuilder(new StubUpcomingEventService(events))
            .Build([PersonWithForms("Alex")], new Settings(), Today);

        Assert.Equal(5, result.UpcomingItems.Count);
        Assert.Equal(
            Enumerable.Range(1, 5).Select(index => $"Item {index}"),
            result.UpcomingItems.Select(item => item.Title));
        Assert.Null(result.AssessmentSuggestion);
    }

    [Fact]
    public void ScheduledNotesAreLeftForTodaysWorkInsteadOfBeingOfferedTwiceAtSignIn()
    {
        var result = new DailyAgendaBuilder(new StubUpcomingEventService(
                Event("Scheduled email — Alex", Today.AddDays(2), UpcomingEventKind.ScheduledEmail),
                Event("Q2 Review — Alex", Today.AddDays(3), UpcomingEventKind.OpenReview, FormType.Q2R)))
            .Build([PersonWithForms("Alex")], new Settings(), Today);

        var item = Assert.Single(result.UpcomingItems);
        Assert.Equal("Q2 Review — Alex", item.Title);
        Assert.Equal(FormType.Q2R, item.FormType);
    }

    [Fact]
    public void ExactReleaseRecipientsReplaceTheLegacyCategoryRowInTheAgenda()
    {
        var target = new DateTime(2026, 1, 1);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var legacy = new Form(
            FormType.Release_Medical,
            Today.AddDays(-3),
            completedOn: null,
            targetEffectiveDate: target);
        var person = PersonWithForms("Alex Person", legacy);
        person.ReleaseComplianceSnapshots =
        [
            ReleaseFact("medical-one", "Medical One", target, Today.AddDays(-3), firstId),
            ReleaseFact("medical-two", "Medical Two", target, Today.AddDays(-2), secondId)
        ];
        var settings = new Settings
        {
            BillingComplianceRequirements = BillingComplianceGate.DefaultRequirements |
                                            BillingComplianceRequirements.MedicalRelease
        };

        var result = new DailyAgendaBuilder(new StubUpcomingEventService())
            .Build([person], settings, Today);

        Assert.Equal(2, result.OverdueTotal);
        Assert.Contains(result.OverdueItems, item =>
            item.Title.Contains("Medical One", StringComparison.Ordinal) &&
            item.BlocksBilling &&
            item.ReleaseObligationId == firstId &&
            item.TargetEffectiveDate == target);
        Assert.Contains(result.OverdueItems, item =>
            item.Title.Contains("Medical Two", StringComparison.Ordinal) &&
            item.BlocksBilling &&
            item.ReleaseObligationId == secondId &&
            item.TargetEffectiveDate == target);
    }

    [Fact]
    public void ReleaseMatrixCellAggregatesEveryRecipientWithoutCollapsingThem()
    {
        var target = new DateTime(2026, 1, 1);
        var person = PersonWithForms("Alex Person");
        var completed = ReleaseFact("medical-one", "Medical One", target, Today.AddDays(-3)) with
        {
            Attestations =
            [
                new("medical-one", Today.AddDays(-4),
                    new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc))
            ]
        };
        person.ReleaseComplianceSnapshots =
        [
            completed,
            ReleaseFact("medical-two", "Medical Two", target, Today.AddDays(-2))
        ];

        var cell = new Sati.ViewModels.ReleaseCellViewModel(
            person, ReleaseObligationCategory.Medical, Today);

        Assert.Equal(FormCellStatus.Overdue, cell.Status);
        Assert.Contains("1/2", cell.CellText, StringComparison.Ordinal);
        Assert.Equal(Today.AddDays(-2), cell.DueDate);
    }

    private static Person PersonWithForms(string fullName, params Form[] forms)
    {
        var parts = fullName.Split(' ', 2);
        var person = Person.CreatePerson(
            31,
            parts[0],
            parts.Length == 2 ? parts[1] : string.Empty,
            string.Empty,
            new DateTime(1990, 1, 1),
            new DateTime(2025, 1, 1),
            WaiverType.Section21,
            new Settings());
        person.Forms = [.. forms];
        return person;
    }

    private static UpcomingEvent Event(
        string title,
        DateTime date,
        UpcomingEventKind kind,
        FormType? formType = null) => new()
    {
        ClientName = "Alex",
        Title = title,
        Date = date,
        Kind = kind,
        FormType = formType
    };

    private static ReleaseComplianceFact ReleaseFact(
        string key,
        string recipient,
        DateTime target,
        DateTime due,
        Guid? obligationId = null) =>
        new(
            key,
            ReleaseObligationCategory.Medical,
            due,
            target,
            RetiredOn: null,
            Attestations: [],
            obligationId ?? Guid.NewGuid(),
            target,
            target.AddDays(-90),
            recipient);

    private sealed class StubUpcomingEventService(params UpcomingEvent[] events)
        : IUpcomingEventService
    {
        public List<UpcomingEvent> GenerateEvents(
            IEnumerable<IEventSource> people,
            Settings settings,
            DateTime? asOf = null) => [.. events];

        public UpcomingEvent? NextFormSuggestion(
            IEventSource person,
            Settings settings,
            DateTime? asOf = null) => null;
    }
}
