using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services.Billing;
using Sati.ViewModels.Supervisor;
using Xunit;

namespace Sati.Tests;

public sealed class LocalFormWorkBillingTests
{
    private static readonly DateTime Due = new(2026, 9, 3);

    [Fact]
    public void LateReviewRemainsBlockedAtBillingReleaseEvenAfterCompletion()
    {
        var note = ReviewNote(Due.AddDays(1), Due.AddDays(1));
        note.ComplianceOverride = true;
        note.OverrideReason = "Supervisor reviewed the historical compliance gap.";

        var result = BillingService.ValidateNoteForBilling(
            note, BillingCompliancePolicyContext.Default());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, reason => reason.Contains("after its", StringComparison.Ordinal));
    }

    [Fact]
    public void RevokedAttestationBlocksLinkedNote()
    {
        var note = ReviewNote(Due, null);

        var reasons = BillingService.EvaluateFormWorkBilling(note);

        Assert.Contains(reasons, reason => reason.Contains("no current completion", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingExactLinkBlocksFormNote()
    {
        var note = ReviewNote(Due, Due);
        note.FormId = null;

        var reasons = BillingService.EvaluateFormWorkBilling(note);

        Assert.Contains(reasons, reason => reason.Contains("not linked", StringComparison.Ordinal));
    }

    [Fact]
    public void CorrectedOnTimeDateRequiresTheNoteActivityDateToMatch()
    {
        var note = ReviewNote(Due.AddDays(1), Due);

        Assert.Contains(BillingService.EvaluateFormWorkBilling(note),
            reason => reason.Contains("does not match", StringComparison.Ordinal));

        note.EventDate = Due;
        Assert.Empty(BillingService.EvaluateFormWorkBilling(note));
    }

    [Fact]
    public void ReleaseAndOrdinaryServiceNotesStayOutsideFormWorkRule()
    {
        var release = ReviewNote(Due.AddDays(1), null);
        release.FormType = FormType.Release_Agency;
        Assert.Empty(BillingService.EvaluateFormWorkBilling(release));

        var visit = ReviewNote(Due.AddDays(1), null);
        visit.NoteType = NoteType.Visit;
        Assert.Empty(BillingService.EvaluateFormWorkBilling(visit));
    }

    [Fact]
    public void SupervisorQueueClassifiesLateFormWorkAsNonbillable()
    {
        var note = ReviewNote(Due.AddDays(1), Due.AddDays(1));
        note.Status = NoteStatus.Logged;

        var queueDecision = SupervisorService.ServiceDateCompliance(
            note, BillingCompliancePolicyContext.Default());

        Assert.False(queueDecision.Passed);
        Assert.Contains(queueDecision.Reasons,
            reason => reason.Contains("completed after", StringComparison.Ordinal));
    }

    [Fact]
    public void SupervisorRowHidesOverrideForPureFormWorkHold()
    {
        var note = ReviewNote(Due.AddDays(1), Due.AddDays(1));
        note.Status = NoteStatus.Logged;
        var decision = SupervisorService.ServiceDateCompliance(
            note, BillingCompliancePolicyContext.Default());
        note.ComplianceFailureReasons = decision.Reasons;
        note.ComplianceBlockers = decision.Blockers ?? [];

        var row = new PendingNoteViewModel(note);

        Assert.True(row.HasHardFormWorkHold);
        Assert.False(row.CanOverrideOrdinaryBlockers);
    }

    [Fact]
    public void AdminCorrectionActionRequiresExactNonReleaseFormLink()
    {
        var note = ReviewNote(Due, Due);

        Assert.True(new PendingNoteViewModel(note, isAdmin: true).CanAdminCorrectSourceDate);
        Assert.False(new PendingNoteViewModel(note, isAdmin: false).CanAdminCorrectSourceDate);

        note.FormId = null;
        Assert.False(new PendingNoteViewModel(note, isAdmin: true).CanAdminCorrectSourceDate);

        note.FormId = 17;
        note.FormType = FormType.Release_Agency;
        Assert.False(new PendingNoteViewModel(note, isAdmin: true).CanAdminCorrectSourceDate);
    }

    private static Note ReviewNote(DateTime activityDate, DateTime? completedOn)
    {
        var person = Person.Rehydrate(7, 9);
        person.Forms.Add(new Form(FormType.Q3R, Due, completedOn)
        {
            Id = 17,
            PersonId = 7
        });
        var note = Note.Create("Review", activityDate, NoteStatus.Approved, 15,
            person.Id, FormType.Q3R, NoteType.Form, formId: 17);
        note.Person = person;
        return note;
    }
}
