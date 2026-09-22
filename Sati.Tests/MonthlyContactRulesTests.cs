using Sati.Contracts.V1;
using Sati.Helpers;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Monthly contact: each visit, call, or email starts a 30-day clock, the plan's
/// effective date starts the first one, and service after the clock runs out is
/// blocked until the next contact.
/// </summary>
public sealed class MonthlyContactRulesTests
{
    private static readonly DateTime Effective = new(2026, 1, 1);
    private const BillingComplianceRequirements Contact = BillingComplianceRequirements.MonthlyContact;

    [Fact]
    public void MixedFormAndPhoneCountsAsOneCompletedContact()
    {
        var fact = MonthlyContactRules.ToFact("Form", "Logged", new DateTime(2026, 9, 4), 17,
            (int)(NoteActivity.Form | NoteActivity.Phone));

        Assert.Equal(new DateTime(2026, 9, 4), fact?.OccurredOn);
        Assert.Equal("note:17", fact?.EvidenceId);
    }

    [Fact]
    public void EachContactStartsAThirtyDayClock()
    {
        var obligations = MonthlyContactRules.BuildObligations(
            Effective,
            [Fact(2026, 1, 20, 7), Fact(2026, 3, 15, 9)]);

        Assert.Equal(
            [new DateTime(2026, 1, 31), new DateTime(2026, 2, 19), new DateTime(2026, 4, 14)],
            obligations.Select(item => item.DueDate));
        Assert.Equal(
            [new DateTime(2026, 1, 20), new DateTime(2026, 3, 15), (DateTime?)null],
            obligations.Select(item => item.CompletedDate));
        Assert.Equal("note:7", obligations[0].EvidenceId);
        Assert.Equal("monthly-contact:2026-02-19", obligations[1].ObligationId);

        Assert.Empty(Blockers(obligations, 2026, 2, 19));   // due day is billable
        Assert.NotEmpty(Blockers(obligations, 2026, 2, 20));
        // A later contact never cures earlier service.
        Assert.NotEmpty(Blockers(obligations, 2026, 3, 14));
        Assert.Empty(Blockers(obligations, 2026, 3, 15));   // the contact day is billable
        Assert.Empty(Blockers(obligations, 2026, 4, 14));
        Assert.Equal(
            "Monthly contact was due Apr 14, 2026 and was not completed as of this service date.",
            Assert.Single(Blockers(obligations, 2026, 4, 15)));
    }

    [Fact]
    public void TheEffectiveDateStartsTheFirstClock()
    {
        var obligations = MonthlyContactRules.BuildObligations(Effective, []);

        Assert.Empty(Blockers(obligations, 2026, 1, 31));
        Assert.NotEmpty(Blockers(obligations, 2026, 2, 1));
    }

    [Fact]
    public void ContactsOnOrBeforeTheEffectiveDateDoNotMoveTheFirstClock()
    {
        var obligations = MonthlyContactRules.BuildObligations(
            Effective, [Fact(2025, 12, 20), Fact(2026, 1, 1)]);

        var only = Assert.Single(obligations);
        Assert.Equal(new DateTime(2026, 1, 31), only.DueDate);
    }

    [Fact]
    public void SeveralContactsOnOneDayAreOneContact()
    {
        var obligations = MonthlyContactRules.BuildObligations(
            Effective, [Fact(2026, 1, 10, 9), Fact(2026, 1, 10, 4)]);

        Assert.Equal(2, obligations.Count);
        Assert.Equal("note:4", obligations[0].EvidenceId);
    }

    [Fact]
    public void TheGateIsOffByDefault()
    {
        var obligations = MonthlyContactRules.BuildObligations(Effective, []);

        Assert.False(BillingComplianceGate.DefaultRequirements.HasFlag(Contact));
        Assert.Empty(BillingComplianceGate.EvaluateBillingWindow(
            obligations, new DateTime(2026, 6, 1), BillingComplianceGate.DefaultRequirements));
    }

    [Fact]
    public void UnloadedHistoryBlocksWithANamedReasonOnlyWhenRequired()
    {
        var obligations = MonthlyContactRules.BuildObligations(Effective, contacts: null);

        Assert.Contains("contact history not loaded", Assert.Single(Blockers(obligations, 2026, 1, 2)));
        Assert.Empty(BillingComplianceGate.EvaluateBillingWindow(
            obligations, new DateTime(2026, 1, 2), BillingComplianceRequirements.All & ~Contact));
        Assert.Empty(MonthlyContactRules.BuildObligations(initialEffectiveDate: null, contacts: null));
    }

    [Theory]
    [InlineData("Visit", true)]
    [InlineData("Phone", true)]
    [InlineData("Email", true)]
    [InlineData("Contact", true)]
    [InlineData("Form", false)]
    [InlineData("Other", false)]
    [InlineData("Reminder", false)]
    [InlineData(null, false)]
    public void OnlyVisitsCallsAndEmailsAreContacts(string? noteType, bool counts) =>
        Assert.Equal(counts, MonthlyContactRules.IsContactType(noteType));

    [Theory]
    [InlineData("Pending", true)]
    [InlineData("Logged", true)]
    [InlineData("HeldForCompliance", true)]
    [InlineData("Approved", true)]
    [InlineData("Returned", true)]
    [InlineData("ComplianceBlocked", true)]
    [InlineData("Scheduled", false)]
    [InlineData("Cancelled", false)]
    [InlineData("Delayed", false)]
    [InlineData("Abandoned", false)]
    [InlineData(null, false)]
    public void OnlyNotesThatHappenedCount(string? status, bool counts) =>
        Assert.Equal(counts, MonthlyContactRules.HasOccurred(status));

    [Fact]
    public void EveryNoteStatusIsClassified()
    {
        // A status added later must be placed deliberately on one side or the other.
        var occurred = new[] { "Pending", "Logged", "HeldForCompliance", "Approved", "Returned", "ComplianceBlocked" };
        var notOccurred = new[] { "Scheduled", "Cancelled", "Delayed", "Abandoned" };
        Assert.Equal(
            Enum.GetNames<NoteStatus>().Order(),
            occurred.Concat(notOccurred).Order());
    }

    [Fact]
    public void ANoteBeingSavedReplacesItsStoredCopy()
    {
        IReadOnlyList<ContactFact> stored = [Fact(2026, 1, 10, 5), Fact(2026, 1, 20, 6)];

        var changedAway = MonthlyContactRules.WithCandidate(stored, 6, candidate: null);
        Assert.Equal(["note:5"], changedAway.Select(item => item.EvidenceId));

        var moved = MonthlyContactRules.WithCandidate(stored, 6, Fact(2026, 1, 25, 6));
        Assert.Equal(
            [new DateTime(2026, 1, 10), new DateTime(2026, 1, 25)],
            moved.Select(item => item.OccurredOn));

        var added = MonthlyContactRules.WithCandidate(stored, 0, new ContactFact(new DateTime(2026, 2, 1)));
        Assert.Equal(3, added.Count);
    }

    [Fact]
    public void StatusIsOverdueOnTheThirtyFirstDay()
    {
        IReadOnlyList<ContactFact> contacts = [Fact(2026, 3, 1)];

        var dayThirty = MonthlyContactRules.Status(Effective, contacts, new DateTime(2026, 3, 31));
        var dayThirtyOne = MonthlyContactRules.Status(Effective, contacts, new DateTime(2026, 4, 1));

        Assert.Equal(new DateTime(2026, 3, 1), dayThirty.LastContactOn);
        Assert.Equal(30, dayThirty.DaysSinceLastContact);
        Assert.False(dayThirty.IsOverdue);
        Assert.True(dayThirtyOne.IsOverdue);
        Assert.Equal(new DateTime(2026, 3, 31), dayThirtyOne.NextContactDueOn);
    }

    [Fact]
    public void StatusIgnoresFutureContactsAndFallsBackToTheEffectiveDate()
    {
        var status = MonthlyContactRules.Status(
            Effective, [Fact(2026, 5, 1)], new DateTime(2026, 2, 15));

        Assert.Null(status.LastContactOn);
        Assert.True(status.IsOverdue);
        Assert.Equal(new DateTime(2026, 1, 31), status.NextContactDueOn);

        var newClient = MonthlyContactRules.Status(Effective, [], new DateTime(2026, 1, 20));
        Assert.False(newClient.IsOverdue);
    }

    [Fact]
    public void TheClientListWordsTheStatusWithoutRelyingOnColour()
    {
        Assert.Equal(string.Empty, MonthlyContactPresentation.Describe(null));
        Assert.Equal("Last contact 03/01/26", MonthlyContactPresentation.Describe(
            MonthlyContactRules.Status(Effective, [Fact(2026, 3, 1)], new DateTime(2026, 3, 31))));
        Assert.Equal("Last contact 03/01/26 · overdue", MonthlyContactPresentation.Describe(
            MonthlyContactRules.Status(Effective, [Fact(2026, 3, 1)], new DateTime(2026, 4, 1))));
        Assert.Equal("No contact recorded · overdue", MonthlyContactPresentation.Describe(
            MonthlyContactRules.Status(Effective, [], new DateTime(2026, 2, 1))));
    }

    [Fact]
    public void APersonWithoutLoadedHistoryIsReportedNotTreatedAsUncontacted()
    {
        var person = PersonWithReviewsSatisfied();
        person.ContactFactsForCompliance = null;

        Assert.Null(person.GetMonthlyContactStatus(new DateTime(2026, 1, 5)));
        Assert.Contains(
            person.EvaluateBillingWindow(new DateTime(2026, 1, 5), Contact),
            reason => reason.Contains("contact history not loaded", StringComparison.Ordinal));
    }

    [Fact]
    public void ANewPersonHasAKnownEmptyHistory()
    {
        var person = Person.CreatePerson(
            31, "New", "Client", string.Empty, new DateTime(1990, 1, 1),
            Effective, WaiverType.None, new Settings());

        Assert.NotNull(person.ContactFactsForCompliance);
        Assert.Empty(person.EvaluateBillingWindow(new DateTime(2026, 1, 20), Contact));
        Assert.NotEmpty(person.EvaluateBillingWindow(new DateTime(2026, 2, 5), Contact));
    }

    [Fact]
    public void AVisitBeingLoggedCountsForItsOwnDate()
    {
        var person = PersonWithReviewsSatisfied();
        var serviceDate = new DateTime(2026, 2, 15);
        var visit = Note.Create(string.Empty, serviceDate, NoteStatus.Logged, 30, person.Id,
            noteType: NoteType.Visit);
        var other = Note.Create(string.Empty, serviceDate, NoteStatus.Logged, 30, person.Id,
            noteType: NoteType.Other);

        Assert.NotEmpty(person.EvaluateBillingWindow(serviceDate, Contact));
        Assert.Empty(person.EvaluateBillingWindow(serviceDate, Contact, contactCandidate: visit));
        Assert.NotEmpty(person.EvaluateBillingWindow(serviceDate, Contact, contactCandidate: other));
    }

    [Fact]
    public void TheCurrentStateGateReportsAnOverdueContact()
    {
        var person = PersonWithReviewsSatisfied();
        person.ContactFactsForCompliance = [Fact(2026, 1, 10)];

        Assert.True(person.EvaluateComplianceGate(new DateTime(2026, 2, 9), requirements: Contact).Passed);
        Assert.Contains(
            "Monthly contact was due Feb 9, 2026 and is incomplete.",
            person.EvaluateComplianceGate(new DateTime(2026, 2, 10), requirements: Contact).Reasons);
    }

    private static Person PersonWithReviewsSatisfied()
    {
        var person = Person.CreatePerson(
            31, "Contact", "Rules", string.Empty, new DateTime(1990, 1, 1),
            Effective, WaiverType.None, new Settings());
        person.Forms.Clear();
        return person;
    }

    private static IReadOnlyList<string> Blockers(
        IReadOnlyList<ComplianceFormSnapshot> obligations, int year, int month, int day) =>
        BillingComplianceGate.EvaluateBillingWindow(obligations, new DateTime(year, month, day), Contact);

    private static ContactFact Fact(int year, int month, int day, int? noteId = null) =>
        new(new DateTime(year, month, day), MonthlyContactRules.EvidenceIdFor(noteId));
}
